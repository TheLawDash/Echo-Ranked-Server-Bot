using System.Collections.Concurrent;
using EchoRankedServerBot.Models.Match;

namespace EchoRankedServerBot.Services;

public class MatchStateService
{
    private readonly ConcurrentDictionary<string, EchoMatch> _matches = new();

    public bool TryAdd(EchoMatch match) => _matches.TryAdd(match.MatchId, match);

    public bool TryGet(string matchId, out EchoMatch? match) => _matches.TryGetValue(matchId, out match);

    public EchoMatch? GetByMatchId(string matchId) => _matches.GetValueOrDefault(matchId);

    public EchoMatch? GetByChannelId(ulong channelId) =>
        _matches.Values.FirstOrDefault(m =>
            m.PrivateMatchDetails?.QueueChannelId == channelId);

    public bool TryRemove(string matchId, out EchoMatch? match) => _matches.TryRemove(matchId, out match);

    public IEnumerable<EchoMatch> GetAll() => _matches.Values;

    public void UpdateMatch(string matchId, Action<EchoMatch> action)
    {
        if (!_matches.TryGetValue(matchId, out var match))
            return;

        lock (match.Lock)
        {
            action(match);
        }
    }
}
