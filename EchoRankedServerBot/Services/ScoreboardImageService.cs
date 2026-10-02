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

    private static readonly SKPoint[] FramedRoundWinCenters = [new(425, 582.5f), new(511.5f, 582.5f), new(598, 582.5f)];

    // Coordinates for the 1024px templates; blue rows precede orange rows.
    private static readonly ScoreboardRegions ClassicRegions = new(
        [45, 405, 498, 585, 670, 757, 839, 921, 999],
        [(703, 765), (773, 839), (848, 911), (920, 991),
         (193, 251), (260, 328), (337, 399), (408, 479)],
        new(135, 60, 630, 109), new(770, 60, 994, 109),
        new(80, 528, 246, 611), new(792, 528, 958, 611),
        new(390, 498, 639, 570),
        new(287, 520, 371, 611), new(657, 520, 747, 611));

    private static readonly ScoreboardRegions FramedRegions = new(
        [76, 384, 478, 569, 653, 740, 828, 915, 1006],
        [(695, 763), (771, 838), (847, 914), (923, 990),
         (185, 247), (254, 316), (323, 385), (392, 454)],
        new(184, 52, 435, 102), new(784, 52, 984, 102),
        new(36, 536, 217, 595), new(805, 536, 987, 595),
        new(389, 494, 635, 550),
        new(256, 536, 351, 595), new(674, 536, 769, 595));

    private sealed record ScoreboardRegions(
        float[] ColumnEdges, (float Top, float Bottom)[] Rows,
        SKRect MvpName, SKRect MvpScore, SKRect OrangeScore, SKRect BlueScore,
        SKRect Clock, SKRect OrangeRounds, SKRect BlueRounds)
    {
        public SKRect PlayerCell(int row, int column) => new(
            ColumnEdges[column] + 8, Rows[row].Top + 8,
            ColumnEdges[column + 1] - 8, Rows[row].Bottom - 8);
    }

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
            var regions = options.Value.ScoreboardLayout == ScoreboardLayout.Framed ? FramedRegions : ClassicRegions;

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

                    foreach (var player in teamPlayers.TakeWhile(_ => playerIndex < regions.Rows.Length))
                    {
                        DrawTextInBox(canvas, player.Name ?? "", font, paint, regions.PlayerCell(playerIndex, 0), 32);
                        DrawTextInBox(canvas, player.Stats?.Points.ToString() ?? "0", font, paint, regions.PlayerCell(playerIndex, 1), 32);
                        DrawTextInBox(canvas, player.Stats?.Assists.ToString() ?? "0", font, paint, regions.PlayerCell(playerIndex, 2), 32);
                        DrawTextInBox(canvas, player.Stats?.Saves.ToString() ?? "0", font, paint, regions.PlayerCell(playerIndex, 3), 32);
                        DrawTextInBox(canvas, player.Stats?.Steals.ToString() ?? "0", font, paint, regions.PlayerCell(playerIndex, 4), 32);
                        DrawTextInBox(canvas, player.Stats?.Stuns.ToString() ?? "0", font, paint, regions.PlayerCell(playerIndex, 5), 32);
                        DrawTextInBox(canvas, player.Ping?.ToString() ?? "0", font, paint, regions.PlayerCell(playerIndex, 6), 32);

                        var playerScore = playerScores.Find(x => x.Player?.UserId == player.UserId && x.Player?.Name == player.Name);
                        if (playerScore is not null)
                        {
                            DrawTextInBox(canvas, playerScore.Score.ToString("F1"), font, paint, regions.PlayerCell(playerIndex, 7), 24);
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
            DrawTextInBox(canvas, echoMatchData.OrangePoints?.ToString() ?? "0", font, paint, regions.OrangeScore, 62);
            DrawTextInBox(canvas, echoMatchData.BluePoints?.ToString() ?? "0", font, paint, regions.BlueScore, 62);

            // Draw game clock or "GAME OVER"
            if (echoMatchData.GameStatus == "post_match")
            {
                DrawTextInBox(canvas, "GAME OVER", font, paint, regions.Clock, 36);
            }
            else
            {
                DrawTextInBox(canvas, echoMatchData.GameClockDisplay ?? "00:00", font, paint, regions.Clock, 36);
            }

            // Draw MVP name and score
            var mvp = playerScores.OrderByDescending(p => p.Score).FirstOrDefault()?.Player;
            if (mvp is not null)
            {
                var mvpScoreEntry = playerScores.Find(x => x.Player?.Name == mvp.Name);
                DrawTextInBox(canvas, mvp.Name ?? "", font, paint, regions.MvpName, 36);
                DrawTextInBox(canvas, mvpScoreEntry?.Score.ToString("F3") ?? "0.000", font, paint, regions.MvpScore, 36);
            }

            // Draw round scores
            DrawTextInBox(canvas, echoMatchData.OrangeRoundScore?.ToString() ?? "0", font, paint, regions.OrangeRounds, 62);
            DrawTextInBox(canvas, echoMatchData.BlueRoundScore?.ToString() ?? "0", font, paint, regions.BlueRounds, 62);

            if (options.Value.ScoreboardLayout == ScoreboardLayout.Framed)
                DrawRoundWins(canvas, echoMatchData.OrangeRoundScore, echoMatchData.BlueRoundScore);

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

    private static void DrawRoundWins(SKCanvas canvas, int? orangeRoundScore, int? blueRoundScore)
    {
        var orangeWins = Math.Clamp(orangeRoundScore ?? 0, 0, FramedRoundWinCenters.Length);
        var blueWins = Math.Clamp(blueRoundScore ?? 0, 0, FramedRoundWinCenters.Length - orangeWins);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // Team totals are available, so fill from each team's side rather than implying round order.
        for (var index = 0; index < FramedRoundWinCenters.Length; index++)
        {
            if (index < orangeWins)
                paint.Color = new SKColor(255, 145, 0);
            else if (index >= FramedRoundWinCenters.Length - blueWins)
                paint.Color = new SKColor(0, 174, 239);
            else
                continue;

            // Leave the template's outline visible around the colored interior.
            canvas.DrawCircle(FramedRoundWinCenters[index], 14, paint);
        }
    }

    private static void DrawTextInBox(SKCanvas canvas, string text, SKFont font, SKPaint paint, SKRect area, float size)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        font.Size = size;
        font.MeasureText(text, out var bounds, paint);
        while ((bounds.Width > area.Width || bounds.Height > area.Height) && font.Size > 1)
        {
            font.Size -= 1;
            font.MeasureText(text, out bounds, paint);
        }

        // Center the visible glyphs, including fonts with unusual bearings/ascent.
        var x = area.MidX - bounds.MidX;
        var baseline = area.MidY - bounds.MidY;
        canvas.Save();
        try
        {
            canvas.ClipRect(area);
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
        }
        finally
        {
            canvas.Restore();
        }
    }

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
