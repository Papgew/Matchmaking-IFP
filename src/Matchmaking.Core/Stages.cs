using System.Collections.Immutable;

namespace Matchmaking.Core;

public static class Stages
{
    public const double MinMatchMinutes = 3.0;

    public static bool IsPlayableMatch(MatchRecord m) =>
        m.MatchDurationMinutes >= MinMatchMinutes
        && m.Kills >= 0
        && m.Deaths >= 0
        && m.CurrentMmr >= 0;

    public static ImmutableArray<MatchRecord> FilterValidMatches(IEnumerable<MatchRecord> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches.Where(IsPlayableMatch).ToImmutableArray();
    }

    public static ImmutableArray<RatedMatch> ApplyMmrPolicy(
        IEnumerable<MatchRecord> matches,
        Func<MatchRecord, int> policy)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(policy);
        return matches
            .Select(m =>
            {
                var delta = policy(m);
                return new RatedMatch(m, delta, Math.Max(0, m.CurrentMmr + delta));
            })
            .ToImmutableArray();
    }

    public static Rank MapRank(int mmr) => mmr switch
    {
        < 1000 => Rank.Bronze,
        < 1500 => Rank.Silver,
        < 2000 => Rank.Gold,
        < 2500 => Rank.Platinum,
        _ => Rank.Diamond
    };

    public static ImmutableArray<PlayerMatchResult> BuildPlayerResults(IEnumerable<RatedMatch> rated)
    {
        ArgumentNullException.ThrowIfNull(rated);
        return rated
            .Select(r => new PlayerMatchResult(
                r.Match.PlayerId, r.Match.CurrentMmr, r.MmrDelta, r.NewMmr, MapRank(r.NewMmr)))
            .OrderByDescending(p => p.NewMmr)
            .ThenBy(p => p.PlayerId, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
