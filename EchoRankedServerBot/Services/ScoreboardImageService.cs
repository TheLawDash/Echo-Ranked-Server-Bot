using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace EchoRankedServerBot.Services;

public class ScoreboardImageService(ILogger<ScoreboardImageService> logger)
{
    // Column center X positions
    private const int NameCenterX = 225;
    private const int PtSx = 450;
    private const int AsTx = 540;
    private const int Sx = 625;
    private const int StLx = 710;
    private const int StNx = 795;
    private const int PnGx = 877;
    private const int MvPx = 960;

    // Y positions for each player slot (orange team: indices 0-3, blue team: indices 4-7)
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

            using var bitmap = SKBitmap.Decode(templatePath);
            if (bitmap is null)
            {
                logger.LogError("Failed to decode template image at {TemplatePath}", templatePath);
                return null;
            }

            using var canvas = new SKCanvas(bitmap);

            var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            using var font = new SKFont(typeface, 32);
            using var paint = new SKPaint();
            paint.Color = SKColors.White;
            paint.IsAntialias = true;

            // Draw player names and stats
            var actualIndex = 0;
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
                    var teamDifference = 4 - team.Players.Count;

                    foreach (var player in team.Players.TakeWhile(_ => playerIndex < NameYs.Length))
                    {
                        font.Size = 32;

                        DrawCenteredText(canvas, player.Name ?? "", font, paint, NameCenterX, NameYs[playerIndex]);
                        DrawCenteredText(canvas, player.Stats?.Points.ToString() ?? "0", font, paint, PtSx, NameYs[playerIndex]);
                        DrawCenteredText(canvas, player.Stats?.Assists.ToString() ?? "0", font, paint, AsTx, NameYs[playerIndex]);
                        DrawCenteredText(canvas, player.Stats?.Saves.ToString() ?? "0", font, paint, Sx, NameYs[playerIndex]);
                        DrawCenteredText(canvas, player.Stats?.Steals.ToString() ?? "0", font, paint, StLx, NameYs[playerIndex]);
                        DrawCenteredText(canvas, player.Stats?.Stuns.ToString() ?? "0", font, paint, StNx, NameYs[playerIndex]);
                        DrawCenteredText(canvas, player.Ping?.ToString() ?? "0", font, paint, PnGx, NameYs[playerIndex]);

                        // Draw MVP score with smaller font
                        font.Size = 24;
                        if (actualIndex < playerScores.Count)
                        {
                            DrawCenteredText(canvas, playerScores[actualIndex].Score.ToString("F1"), font, paint, MvPx, NameYs[playerIndex] + 5);
                        }

                        playerIndex++;
                        actualIndex++;
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
            DrawCenteredText(canvas, echoMatchData.OrangePoints?.ToString() ?? "0", font, paint, 165, 535);
            DrawCenteredText(canvas, echoMatchData.BluePoints?.ToString() ?? "0", font, paint, 875, 535);

            // Draw game clock or "GAME OVER"
            font.Size = 36;
            if (echoMatchData.GameStatus == "post_match")
            {
                DrawCenteredText(canvas, "GAME OVER", font, paint, 515, 515);
            }
            else
            {
                DrawCenteredText(canvas, echoMatchData.GameClockDisplay ?? "00:00", font, paint, 515, 515);
            }

            // Draw MVP name and score
            font.Size = 36;
            var mvp = playerScores.OrderByDescending(p => p.Score).FirstOrDefault()?.Player;
            if (mvp is not null)
            {
                var mvpScoreEntry = playerScores.Find(x => x.Player?.Name == mvp.Name);
                canvas.DrawText(mvp.Name ?? "", 125, 63, SKTextAlign.Left, font, paint);
                canvas.DrawText(mvpScoreEntry?.Score.ToString("F3") ?? "0.000", 760, 66, SKTextAlign.Left, font, paint);
            }

            // Draw round scores
            font.Size = 62;
            canvas.DrawText(echoMatchData.OrangeRoundScore?.ToString() ?? "0", 300, 535, SKTextAlign.Left, font, paint);
            canvas.DrawText(echoMatchData.BlueRoundScore?.ToString() ?? "0", 675, 535, SKTextAlign.Left, font, paint);

            // Encode to PNG MemoryStream
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

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

    private static void DrawCenteredText(SKCanvas canvas, string text, SKFont font, SKPaint paint, int centerX, int y)
    {
        var textWidth = font.MeasureText(text);
        canvas.DrawText(text, centerX - textWidth / 2, y, SKTextAlign.Left, font, paint);
    }
}
