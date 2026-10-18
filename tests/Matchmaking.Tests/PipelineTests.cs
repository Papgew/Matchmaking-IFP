using System.Collections.Immutable;
using Matchmaking.Core;
using Xunit;

namespace Matchmaking.Tests;

public class PipelineTests
{
    private static readonly string[] SampleCsv =
    {
        "PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin",
        "P001,1480,10,5,20,true",
        "P002,1520,2,8,15,false",
        "P003,990,5,5,3,true",
        "P004,2100,8,2,2,true",
        "P005,1200,-1,3,10,false",
        "P006,1990,20,0,25,true",
    };

    private static ImmutableArray<MatchRecord> Sample()
    {
        var parsed = MatchParser.ParseCsv(SampleCsv);
        Assert.True(parsed.IsValid);
        return parsed.Matches;
    }

    [Fact]
    public void NormalWorkflow_PolicyA_ProducesDocumentedResult()
    {
        var r = MatchmakingPipeline.Run(Sample(), RatingPolicies.WinLoss);

        var expected = new[]
        {
            new PlayerMatchResult("P006", 1990, 28, 2018, Rank.Platinum),
            new PlayerMatchResult("P001", 1480, 28, 1508, Rank.Gold),
            new PlayerMatchResult("P002", 1520, -28, 1492, Rank.Silver),
            new PlayerMatchResult("P003", 990, 25, 1015, Rank.Silver),
        };
        Assert.Equal(expected, r.ToArray());
    }

    [Fact]
    public void EmptyDataset_ReturnsEmptyResult()
    {
        var parsed = MatchParser.ParseCsv(Array.Empty<string>());
        Assert.True(parsed.IsValid);
        Assert.Empty(parsed.Matches);
        Assert.Empty(MatchmakingPipeline.Run(parsed.Matches, RatingPolicies.WinLoss));
        Assert.Empty(MatchmakingPipeline.Run(parsed.Matches, RatingPolicies.PerformanceFirst));
    }

    [Fact]
    public void Boundary_DurationExactlyThreeMinutes_IsAccepted_JustBelowIsRejected()
    {
        var at = new MatchRecord("X", 1000, 0, 0, 3.0, true);
        var below = at with { MatchDurationMinutes = 2.99 };
        var zeroKills = at with { Kills = 0 };
        var negKills = at with { Kills = -1 };

        var result = Stages.FilterValidMatches(new[] { at, below, zeroKills, negKills });

        Assert.Equal(new[] { at, zeroKills }, result.ToArray());
    }

    [Theory]
    [InlineData(999, Rank.Bronze)]
    [InlineData(1000, Rank.Silver)]
    [InlineData(1499, Rank.Silver)]
    [InlineData(1500, Rank.Gold)]
    [InlineData(1999, Rank.Gold)]
    [InlineData(2000, Rank.Platinum)]
    [InlineData(2500, Rank.Diamond)]
    public void Boundary_RankThresholds(int mmr, Rank expected) =>
        Assert.Equal(expected, Stages.MapRank(mmr));

    [Fact]
    public void Boundary_MmrNeverBelowZero()
    {
        var m = new MatchRecord("L", 10, 0, 9, 10, false);
        var r = Stages.ApplyMmrPolicy(new[] { m }, RatingPolicies.WinLoss);
        Assert.Equal(0, r[0].NewMmr);
    }

    [Fact]
    public void InvalidInput_IsRejectedWithMessages_NoFabricatedMatches()
    {
        var lines = new[]
        {
            "PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin",
            "P001,1480,10,5,20,true",
            "P007,abc,3,2,10,true",
            "P008,1300,4,2,12,maybe",
            "P009,1300,4,2",
            ",1300,4,2,10,true",
            "P010,1300,4,2,NaN,true",
        };

        var parsed = MatchParser.ParseCsv(lines);

        Assert.False(parsed.IsValid);
        Assert.Equal(5, parsed.Errors.Length);
        Assert.Single(parsed.Matches);
        Assert.Equal("P001", parsed.Matches[0].PlayerId);
        Assert.Contains("CurrentMMR", parsed.Errors[0]);
    }

    [Fact]
    public void MissingInput_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MatchParser.ParseCsv(null));
        Assert.Throws<ArgumentNullException>(() => Stages.FilterValidMatches(null!));
        Assert.Throws<ArgumentNullException>(() => Stages.ApplyMmrPolicy(Sample(), null!));
    }

    [Fact]
    public void TwoPolicies_SameInput_DifferInDocumentedCase()
    {
        var a = MatchmakingPipeline.Run(Sample(), RatingPolicies.WinLoss);
        var b = MatchmakingPipeline.Run(Sample(), RatingPolicies.PerformanceFirst);

        var expectedB = new[]
        {
            new PlayerMatchResult("P006", 1990, 68, 2058, Rank.Platinum),
            new PlayerMatchResult("P002", 1520, -10, 1510, Rank.Gold),
            new PlayerMatchResult("P001", 1480, 27, 1507, Rank.Gold),
            new PlayerMatchResult("P003", 990, 1, 991, Rank.Bronze),
        };
        Assert.Equal(expectedB, b.ToArray());

        Assert.Equal(Rank.Silver, a.Single(x => x.PlayerId == "P002").Rank);
        Assert.Equal(Rank.Gold, b.Single(x => x.PlayerId == "P002").Rank);
        Assert.NotEqual(a.ToArray(), b.ToArray());
    }

    [Fact]
    public void PolicySelector_ReturnsPolicies_AndRejectsUnknown()
    {
        Assert.True(PolicySelector.TryGet("a", out var pa));
        Assert.True(PolicySelector.TryGet("B", out var pb));
        Assert.False(PolicySelector.TryGet("Z", out _));
        var m = new MatchRecord("X", 1000, 5, 5, 10, true);
        Assert.Equal(25, pa(m));
        Assert.Equal(RatingPolicies.PerformanceFirst(m), pb(m));
    }

    [Fact]
    public void Purity_StagesDoNotMutateInput_AndAreDeterministic()
    {
        var input = new List<MatchRecord>(Sample());
        var snapshot = input.ToList();

        var f1 = Stages.FilterValidMatches(input);
        Assert.Equal(snapshot, input);
        Assert.Equal(4, f1.Length);

        var r1 = Stages.ApplyMmrPolicy(input, RatingPolicies.PerformanceFirst);
        Assert.Equal(snapshot, input);
        Assert.Equal(6, r1.Length);

        var p1 = MatchmakingPipeline.Run(input, RatingPolicies.WinLoss);
        var p2 = MatchmakingPipeline.Run(input, RatingPolicies.WinLoss);
        Assert.Equal(snapshot, input);
        Assert.Equal(p1.ToArray(), p2.ToArray());
    }

    [Fact]
    public void HigherOrder_PolicyDelegateIsInvokedOncePerMatch()
    {
        var calls = 0;
        Func<MatchRecord, int> counting = m => { calls++; return 7; };

        var r = Stages.ApplyMmrPolicy(Stages.FilterValidMatches(Sample()), counting);

        Assert.Equal(4, calls);
        Assert.All(r, x => Assert.Equal(7, x.MmrDelta));
    }

    [Fact]
    public void PerformanceFirst_ZeroDeaths_UsesDivisorOne()
    {
        var m = new MatchRecord("Z", 1990, 20, 0, 25, true);
        Assert.Equal(68, RatingPolicies.PerformanceFirst(m));
    }
}
