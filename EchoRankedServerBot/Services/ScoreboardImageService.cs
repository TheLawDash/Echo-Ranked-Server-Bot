using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace EchoRankedServerBot.Services;

public class ScoreboardImageService(IOptions<BotOptions> options, ILogger<ScoreboardImageService> logger)
{
    private readonly Lazy<SKTypeface> _typeface = new(() => LoadTypeface(options.Value.ScoreboardFontPath, logger));

    // Column center X positions
    private const int NameCenterX = 225;
    private const int PtSx = 450;
    private const int AsTx = 540;
    private const int Sx = 625;
    private const int StLx = 710;
    private const int StNx = 795;
    private const int PnGx = 877;
    private const int MvPx = 960;

    private const float NameMaxWidth = 340;
    private const float StatMaxWidth = 76;
    private const float ScoreMaxWidth = 110;
    private const float ClockMaxWidth = 250;
    private const float MvpNameMaxWidth = 500;
    private const float MvpScoreMaxWidth = 225;

    // Y positions for each player slot (blue team: indices 0-3, orange team: indices 4-7)
    private static readonly int[] NameYs = [713, 785, 860, 935, 200, 270, 345, 420];

    public MemoryStream? GenerateScoreboardAsync(
        string templatePath,
        EchoVrApiSession echoMatchData,
        List<PlayerScore> playerScores,
        string matchId)
    {
        try
        {
            if (echoMatchData.Teams is null || echoMatchData.Teams.Count < 2)
            {
                logger.LogWarning("Scoreboard generation skipped: insufficient team data for match {MatchId}", matchId);
                return null;
            }

            if (echoMatchData.Teams[0].Players is null && echoMatchData.Teams[1].Players is null)
            {
                logger.LogWarning("Scoreboard generation skipped: no players on either team for match {MatchId}", matchId);
                return null;
            }

            if (playerScores.Count == 0)
            {
                logger.LogWarning(
                    "No player scores were available for match {MatchId} when generating the scoreboard. MVP score and MVP name will be blank.",
                    matchId);
            }

            if (!File.Exists(templatePath))
            {
                logger.LogError(
                    "Could not generate the scoreboard for match {MatchId} because the template file at {TemplatePath} was not found.",
                    matchId, templatePath);
                return null;
            }

            using var bitmap = SKBitmap.Decode(templatePath);
            if (bitmap is null)
            {
                logger.LogError(
                    "Could not generate the scoreboard for match {MatchId} because the template image at {TemplatePath} could not be decoded.",
                    matchId, templatePath);
                return null;
            }

            using var canvas = new SKCanvas(bitmap);

            using var font = new SKFont(_typeface.Value, 32);
            using var paint = new SKPaint();
            paint.Color = SKColors.White;
            paint.IsAntialias = true;

            // Draw player names and stats
            var playerIndex = 0;

            foreach (var team in echoMatchData.Teams.Where(team => !string.Equals(team.TeamName, "SPECTATORS", StringComparison.OrdinalIgnoreCase)))
            {
                if (team.Players is null)
                {
                    playerIndex += 4;
                    continue;
                }

                try
                {
                    var teamPlayers = team.Players.Take(4).ToList();
                    var teamDifference = 4 - teamPlayers.Count;

                    foreach (var player in teamPlayers.TakeWhile(_ => playerIndex < NameYs.Length))
                    {
                        font.Size = 32;

                        DrawCenteredText(canvas, player.Name ?? "", font, paint, NameCenterX, NameYs[playerIndex], NameMaxWidth);
                        DrawCenteredText(canvas, player.Stats?.Points.ToString() ?? "0", font, paint, PtSx, NameYs[playerIndex], StatMaxWidth);
                        DrawCenteredText(canvas, player.Stats?.Assists.ToString() ?? "0", font, paint, AsTx, NameYs[playerIndex], StatMaxWidth);
                        DrawCenteredText(canvas, player.Stats?.Saves.ToString() ?? "0", font, paint, Sx, NameYs[playerIndex], StatMaxWidth);
                        DrawCenteredText(canvas, player.Stats?.Steals.ToString() ?? "0", font, paint, StLx, NameYs[playerIndex], StatMaxWidth);
                        DrawCenteredText(canvas, player.Stats?.Stuns.ToString() ?? "0", font, paint, StNx, NameYs[playerIndex], StatMaxWidth);
                        DrawCenteredText(canvas, player.Ping?.ToString() ?? "0", font, paint, PnGx, NameYs[playerIndex], StatMaxWidth);

                        // Draw MVP score with smaller font
                        font.Size = 24;
                        var playerScore = playerScores.Find(x => x.Player?.UserId == player.UserId && x.Player?.Name == player.Name);
                        if (playerScore is not null)
                        {
                            DrawCenteredText(canvas, playerScore.Score.ToString("F1"), font, paint, MvPx, NameYs[playerIndex] + 5, StatMaxWidth);
                        }

                        playerIndex++;
                    }

                    playerIndex += teamDifference;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error drawing team stats for match {MatchId}", matchId);
                }
            }

            // Draw team scores
            font.Size = 62;
            DrawCenteredText(canvas, echoMatchData.OrangePoints?.ToString() ?? "0", font, paint, 165, 535, ScoreMaxWidth);
            DrawCenteredText(canvas, echoMatchData.BluePoints?.ToString() ?? "0", font, paint, 875, 535, ScoreMaxWidth);

            // Draw game clock or "GAME OVER"
            font.Size = 36;
            if (echoMatchData.GameStatus == "post_match")
            {
                DrawCenteredText(canvas, "GAME OVER", font, paint, 515, 515, ClockMaxWidth);
            }
            else
            {
                DrawCenteredText(canvas, echoMatchData.GameClockDisplay ?? "00:00", font, paint, 515, 515, ClockMaxWidth);
            }

            // Draw MVP name and score
            font.Size = 36;
            var mvp = playerScores.OrderByDescending(p => p.Score).FirstOrDefault()?.Player;
            if (mvp is not null)
            {
                var mvpScoreEntry = playerScores.Find(x => x.Player?.Name == mvp.Name);
                DrawText(canvas, mvp.Name ?? "", font, paint, 135, 63, MvpNameMaxWidth);
                DrawText(canvas, mvpScoreEntry?.Score.ToString("F3") ?? "0.000", font, paint, 770, 66, MvpScoreMaxWidth);
            }

            // Draw round scores
            font.Size = 62;
            DrawText(canvas, echoMatchData.OrangeRoundScore?.ToString() ?? "0", font, paint, 300, 535, 70);
            DrawText(canvas, echoMatchData.BlueRoundScore?.ToString() ?? "0", font, paint, 675, 535, 70);

            // Encode to PNG MemoryStream
            using var image = SKImage.FromBitmap(bitmap);
            if (image is null)
            {
                logger.LogError(
                    "Could not generate the scoreboard for match {MatchId} because the rendered bitmap could not be converted to an image.",
                    matchId);
                return null;
            }

            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                logger.LogError(
                    "Could not generate the scoreboard for match {MatchId} because the image failed to encode to PNG.",
                    matchId);
                return null;
            }

            var memoryStream = new MemoryStream();
            data.SaveTo(memoryStream);
            memoryStream.Position = 0;

            return memoryStream;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception generating scoreboard image for match {MatchId}", matchId);
            return null;
        }
    }

    private static void DrawText(SKCanvas canvas, string text, SKFont font, SKPaint paint, float x, int top, float maxWidth, bool centered = false)
    {
        var size = font.Size;
        while (font.MeasureText(text) > maxWidth && font.Size > 10)
            font.Size -= 1;

        var width = font.MeasureText(text);
        var left = centered ? x - width / 2 : x;
        var shrunkTop = top + (size - font.Size) / 2;
        canvas.DrawText(text, left, shrunkTop - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
        font.Size = size;
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, SKFont font, SKPaint paint, int centerX, int top, float maxWidth) =>
        DrawText(canvas, text, font, paint, centerX, top, maxWidth, centered: true);

    private static SKTypeface LoadTypeface(string fontPath, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            var typeface = File.Exists(fontPath) ? SKTypeface.FromFile(fontPath) : null;
            if (typeface != null)
            {
                logger.LogInformation("Scoreboard font {FamilyName} loaded from {FontPath}", typeface.FamilyName, fontPath);
                return typeface;
            }

            logger.LogWarning("The scoreboard font at {FontPath} could not be loaded, falling back to Arial", fontPath);
        }

        return SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
               ?? SKTypeface.Default;
    }
}
