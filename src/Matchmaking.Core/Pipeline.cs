using System.Collections.Immutable;

namespace Matchmaking.Core;

public static class MatchmakingPipeline
{
    public static ImmutableArray<PlayerMatchResult> Run(
        IEnumerable<MatchRecord> matches,
        Func<MatchRecord, int> policy)
    {
        var valid = Stages.FilterValidMatches(matches);
        var rated = Stages.ApplyMmrPolicy(valid, policy);
        return Stages.BuildPlayerResults(rated);
    }
}

public static class PolicySelector
{
    public static bool TryGet(string name, out Func<MatchRecord, int> policy)
    {
        switch (name.Trim().ToUpperInvariant())
        {
            case "A": policy = RatingPolicies.WinLoss; return true;
            case "B": policy = RatingPolicies.PerformanceFirst; return true;
            default: policy = _ => 0; return false;
        }
    }
}
