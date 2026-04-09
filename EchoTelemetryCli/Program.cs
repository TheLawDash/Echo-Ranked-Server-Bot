using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using EchoTelemetryCli.Models;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var nakama = config.GetSection("Nakama");
var baseUrl = nakama["BaseUrl"]!;
var authEndpoint = nakama["AuthEndpoint"]!;
var streamingEndpoint = nakama["StreamingEndpoint"]!;
var username = nakama["Username"]!;
var password = nakama["Password"]!;
var httpKey = nakama["HttpKey"]!;

if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(httpKey))
{
    Console.Error.WriteLine("Fill in Nakama credentials in appsettings.json");
    return 1;
}

var matchId = args.Length > 0 ? args[0] : null;

if (string.IsNullOrWhiteSpace(matchId))
{
    Console.Write("Enter match ID: ");
    matchId = Console.ReadLine()?.Trim();
}

if (string.IsNullOrWhiteSpace(matchId))
{
    Console.Error.WriteLine("No match ID provided.");
    return 1;
}

// Parse spark links: https://echo.taxi/spark://c/MATCHID or spark://c/MATCHID
if (matchId.Contains("taxi") && matchId.Contains("spark"))
    matchId = matchId.Split('/')[6];
else if (matchId.Contains("spark"))
    matchId = matchId.Split('/')[3];

using var http = new HttpClient();

// Authenticate
Console.WriteLine("Authenticating with Nakama...");
var tokenResponse = await http.PostAsJsonAsync(
    $"{baseUrl}{authEndpoint}&http_key={httpKey}",
    new TokenRequest { Username = username, Password = password });

if (!tokenResponse.IsSuccessStatusCode)
{
    var err = await tokenResponse.Content.ReadAsStringAsync();
    Console.Error.WriteLine($"Auth failed ({tokenResponse.StatusCode}): {err}");
    return 1;
}

var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
if (token is null)
{
    Console.Error.WriteLine("Failed to deserialize auth token.");
    return 1;
}

Console.WriteLine("Authenticated. Polling match telemetry...");
Console.WriteLine($"Match ID: {matchId}");
Console.WriteLine(new string('-', 60));

long lastFrameIndex = -1;
int? lastOrangeScore = null;
int? lastBlueScore = null;
string? lastGameStatus = null;
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

while (!cts.Token.IsCancellationRequested)
{
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{streamingEndpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { match_id = matchId }),
            Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request, cts.Token);
        var content = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] API error ({response.StatusCode}): {(content.Length > 200 ? content[..200] : content)}");
            await Task.Delay(5000, cts.Token);
            continue;
        }

        // Debug: print raw response on first poll
        if (lastFrameIndex == -1)
        {
            var preview = content.Length > 500 ? content[..500] + "..." : content;
            Console.WriteLine($"[DEBUG] Raw response: {preview}");
        }

        var data = JsonSerializer.Deserialize<LobbySessionEventsResponse>(content);

        if (data?.Events is null or { Count: 0 })
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No frames yet...");
            await Task.Delay(3000, cts.Token);
            continue;
        }

        foreach (var eventFrame in data.Events)
        {
            var frame = eventFrame.Frame;
            if (frame is null) continue;
            if (frame.FrameIndex is not null && frame.FrameIndex <= lastFrameIndex) continue;

            if (frame.FrameIndex is not null)
                lastFrameIndex = frame.FrameIndex.Value;

            // Print game events
            if (frame.Events is not null)
            {
                foreach (var evt in frame.Events)
                {
                    if (evt.IsGoalScored)
                        PrintEvent(frame.Timestamp, "GOAL SCORED", ConsoleColor.Yellow);
                    if (evt.IsRoundEnded)
                        PrintEvent(frame.Timestamp, "ROUND ENDED", ConsoleColor.Cyan);
                    if (evt.IsMatchEnded)
                        PrintEvent(frame.Timestamp, "MATCH ENDED", ConsoleColor.Red);
                }
            }

            // Print session state changes
            var session = frame.Session;
            if (session is null) continue;

            // Game status change
            if (session.GameStatus != lastGameStatus && session.GameStatus is not null)
            {
                PrintEvent(frame.Timestamp, $"Status: {session.GameStatus}", ConsoleColor.Magenta);
                lastGameStatus = session.GameStatus;
            }

            // Score change
            if (session.OrangePoints != lastOrangeScore || session.BluePoints != lastBlueScore)
            {
                if (lastOrangeScore is not null) // skip initial print
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"  [{frame.Timestamp}] Score: Orange {session.OrangePoints} - Blue {session.BluePoints}");
                    Console.ResetColor();
                }
                lastOrangeScore = session.OrangePoints;
                lastBlueScore = session.BluePoints;
            }

            // Print last score details
            if (session.LastScore?.PersonScored is not null && session.LastScore.PointAmount > 0)
            {
                var ls = session.LastScore;
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"    Scored by: {ls.PersonScored} ({ls.Team}) | {ls.GoalType} | {ls.PointAmount}pts | Speed: {ls.DiscSpeed:F1} | Dist: {ls.DistanceThrown:F1}");
                if (ls.AssistScored is not null)
                    Console.WriteLine($"    Assist: {ls.AssistScored}");
                Console.ResetColor();
            }

            // Print clock
            if (session.GameClockDisplay is not null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Clock: {session.GameClockDisplay} | Map: {session.MapName} | Frame: {frame.FrameIndex}");
                Console.ResetColor();
            }

            // Print teams on first frame
            if (lastFrameIndex <= 1 && session.Teams is not null)
            {
                PrintTeams(session.Teams);
            }
        }

        // Check for match end
        var matchEnded = data.Events.Any(e =>
            e.Frame?.Events is not null &&
            e.Frame.Events.Any(evt => evt.IsMatchEnded));

        if (matchEnded)
        {
            var lastSession = data.Events.LastOrDefault(e => e.Frame?.Session is not null)?.Frame?.Session;
            if (lastSession?.Teams is not null)
            {
                Console.WriteLine();
                Console.WriteLine("=== FINAL STATS ===");
                Console.WriteLine($"Score: Orange {lastSession.OrangePoints} - Blue {lastSession.BluePoints}");
                PrintTeams(lastSession.Teams);
            }
            Console.WriteLine("Match ended. Exiting.");
            break;
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
    }

    await Task.Delay(3000, cts.Token);
}

Console.WriteLine("Done.");
return 0;

static void PrintEvent(string? timestamp, string message, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.WriteLine($">>> [{timestamp}] {message}");
    Console.ResetColor();
}

static void PrintTeams(List<Team> teams)
{
    foreach (var team in teams)
    {
        var color = team.TeamName?.ToLower() == "orange" ? ConsoleColor.DarkYellow : ConsoleColor.Blue;
        Console.ForegroundColor = color;
        Console.WriteLine($"  Team {team.TeamName}:");
        Console.ResetColor();

        if (team.Players is null) continue;
        foreach (var player in team.Players)
        {
            var stats = player.Stats;
            var left = player.PlayerLeft == true ? " [LEFT]" : "";
            Console.WriteLine($"    {player.Name}{left} | Pts:{stats?.Points} G:{stats?.Goals} A:{stats?.Assists} Sv:{stats?.Saves} St:{stats?.Stuns} Ping:{player.Ping}");
        }
    }
}
