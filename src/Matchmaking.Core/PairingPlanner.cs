using System.Collections.Immutable;

namespace Matchmaking.Core;

// Additional domain result: a real player pairing, not another view of a rating record.
public sealed record MatchPair(string PlayerA, string PlayerB, int MmrA, int MmrB, int Difference);
public sealed record PairingPlan(ImmutableArray<MatchPair> Pairs, ImmutableArray<PlayerMatchResult> Waiting);

public static class PairingPlanner
{
    // Workflow 3: identify compatible players from their policy-adjusted ratings.
    // Greedy closest-neighbour pairing after stable ordering; one player is used at most once.
    public static Result<PairingPlan> Create(
        IEnumerable<MatchRecord> records, Func<MatchRecord, int> policy, int maxMmrGap)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(policy);
        if (maxMmrGap < 0)
            return Result<PairingPlan>.Fail("INVALID_PAIR_GAP", "Maximum MMR gap cannot be negative.");

        var valid = Stages.FilterValidMatches(records);
        var rated = MatchmakingPipeline.Run(valid, policy);
        if (rated.GroupBy(player => player.PlayerId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
            return Result<PairingPlan>.Fail("DUPLICATE_PLAYER", "A pairing pool cannot contain the same PlayerId twice.");

        var sorted = rated.OrderBy(player => player.NewMmr)
            .ThenBy(player => player.PlayerId, StringComparer.Ordinal).ToArray();
        var pairs = ImmutableArray.CreateBuilder<MatchPair>();
        var waiting = ImmutableArray.CreateBuilder<PlayerMatchResult>();
        for (var i = 0; i < sorted.Length;)
        {
            if (i + 1 < sorted.Length && (long)sorted[i + 1].NewMmr - sorted[i].NewMmr <= maxMmrGap)
            {
                var a = sorted[i];
                var b = sorted[i + 1];
                pairs.Add(new MatchPair(a.PlayerId, b.PlayerId, a.NewMmr, b.NewMmr,
                    b.NewMmr - a.NewMmr));
                i += 2;
            }
            else
            {
                waiting.Add(sorted[i]);
                i++;
            }
        }

        return Result<PairingPlan>.Ok(new PairingPlan(pairs.ToImmutable(), waiting.ToImmutable()));
    }
}
