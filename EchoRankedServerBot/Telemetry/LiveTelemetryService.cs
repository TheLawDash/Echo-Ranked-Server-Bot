using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.EchoApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nevr.Telemetry.V2;

namespace EchoRankedServerBot.Telemetry;

public class LiveTelemetryService(
    IHttpClientFactory factory,
    IOptions<StreamingOptions> options,
    ILogger<LiveTelemetryService> logger)
{
    private sealed class Subscription
    {
        public LiveMatchState State { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
    }

    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public void EnsureSubscribed(string sessionId)
    {
        if (_subscriptions.ContainsKey(sessionId)) return;

        var subscription = new Subscription();
        if (!_subscriptions.TryAdd(sessionId, subscription)) return;

        _ = RunSubscriptionAsync(sessionId, subscription);
    }

    public EchoVrApiSession? TryGetSnapshot(string sessionId) =>
        _subscriptions.TryGetValue(sessionId, out var subscription) ? subscription.State.ToSession() : null;

    public int GetActivePlayerCount(string sessionId) =>
        _subscriptions.TryGetValue(sessionId, out var subscription) ? subscription.State.ActivePlayerCount : 0;

    public void Stop(string sessionId)
    {
        if (!_subscriptions.TryRemove(sessionId, out var subscription)) return;

        subscription.Cts.Cancel();
        logger.LogInformation("Stopped telemetry subscription for session {SessionId}", sessionId);
    }

    private async Task RunSubscriptionAsync(string sessionId, Subscription subscription)
    {
        var ct = subscription.Cts.Token;
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.ReconnectSeconds));
        var waitingLogged = false;

        try
        {
            while (!ct.IsCancellationRequested && !subscription.State.Ended)
            {
                try
                {
                    var streamId = await FindStreamIdAsync(sessionId, ct);
                    if (streamId == null)
                    {
                        if (!waitingLogged)
                        {
                            logger.LogInformation("Waiting for telemetry stream for session {SessionId} to be announced", sessionId);
                            waitingLogged = true;
                        }
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    await ReadStreamAsync(streamId, subscription.State, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Telemetry stream for session {SessionId} failed, reconnecting", sessionId);
                }

                if (!subscription.State.Ended)
                    await Task.Delay(delay, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped
        }
        finally
        {
            subscription.Cts.Dispose();
        }

        if (subscription.State.Ended)
            logger.LogInformation("Telemetry stream for session {SessionId} ended", sessionId);
    }

    /// <summary>
    /// Finds the stream whose match id starts with the session uuid. Stream ids are
    /// "{uuid}.{node}", and the uuid's case differs between Nakama and nevr-stream.
    /// </summary>
    private async Task<string?> FindStreamIdAsync(string sessionId, CancellationToken ct)
    {
        using var client = factory.CreateClient("Streaming");
        var json = await client.GetStringAsync($"{options.Value.BaseUrl.TrimEnd('/')}/api/v3/stream", ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("streams", out var streams)) return null;

        foreach (var stream in streams.EnumerateArray())
        {
            var matchId = stream.GetProperty("match_id").GetString();
            if (matchId != null && matchId.Split('.')[0].Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                return matchId;
        }

        return null;
    }

    private async Task ReadStreamAsync(string streamId, LiveMatchState state, CancellationToken ct)
    {
        var baseUri = new Uri(options.Value.BaseUrl);
        // ?fps only sets the server's ping rate; every frame is still delivered.
        var uri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = $"/api/v3/stream/{streamId}",
            Query = "fps=1"
        }.Uri;

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(uri, ct);
        logger.LogInformation("Connected to telemetry stream {StreamId}", streamId);

        var buffer = new byte[1 << 16];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var total = 0;
            WebSocketReceiveResult result;
            do
            {
                if (total == buffer.Length)
                    Array.Resize(ref buffer, buffer.Length * 2);

                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), ct);
                total += result.Count;
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Binary || total < 4) continue;

            // 4-byte little-endian length, then one Envelope
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0, 4));
            if (length > total - 4)
            {
                logger.LogWarning("Dropped a truncated telemetry message on stream {StreamId}", streamId);
                continue;
            }

            state.Apply(Envelope.Parser.ParseFrom(buffer, 4, length));
        }

        logger.LogInformation("Telemetry stream {StreamId} closed", streamId);
    }
}
