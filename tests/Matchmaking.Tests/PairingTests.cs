using Matchmaking.Core;
using Xunit;

namespace Matchmaking.Tests;

public class PairingTests
{
    private static MatchRecord Raw(string id, int mmr, int kills = 0, int deaths = 0) =>
        new(id, mmr, kills, deaths, 3.0, true);

    [Fact] public void NormalPairingUsesClosestMmrValuesAndIsDeterministic()
    {
        var input = new[] { Raw("B", 1500), Raw("C", 1010), Raw("A", 1000), Raw("D", 1510) };
        var first = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(input, RatingPolicies.WinLoss, 10)).Value;
        var second = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(input, RatingPolicies.WinLoss, 10)).Value;
        Assert.Equal(2, first.Pairs.Length);
        Assert.Equal("A", first.Pairs[0].PlayerA);
        Assert.Equal("C", first.Pairs[0].PlayerB);
        Assert.Equal(10, first.Pairs[0].Difference);
        Assert.Empty(first.Waiting);
        Assert.Equal(first.Pairs.ToArray(), second.Pairs.ToArray());
    }

    [Fact] public void NoCompatiblePartnerRemainsWaiting()
    {
        var plan = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(
            new[] { Raw("A", 1000), Raw("B", 2000) }, RatingPolicies.WinLoss, 100)).Value;
        Assert.Empty(plan.Pairs);
        Assert.Equal(2, plan.Waiting.Length);
    }

    [Fact] public void EmptyPoolProducesEmptyPlan()
    {
        var plan = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(
            Array.Empty<MatchRecord>(), RatingPolicies.WinLoss, 100)).Value;
        Assert.Empty(plan.Pairs);
        Assert.Empty(plan.Waiting);
    }

    [Fact] public void ExactGapBoundaryCanPair()
    {
        var plan = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(
            new[] { Raw("A", 1000), Raw("B", 1050) }, RatingPolicies.WinLoss, 50)).Value;
        Assert.Single(plan.Pairs);
    }

    [Fact] public void NegativeGapProducesDomainFailure()
    {
        Assert.Equal("INVALID_PAIR_GAP",
            Assert.IsType<Result<PairingPlan>.Failure>(PairingPlanner.Create(
                new[] { Raw("A", 1000) }, RatingPolicies.WinLoss, -1)).Error.Code);
    }

    [Fact] public void DuplicatePlayerIsRejectedWithoutAnyPairs()
    {
        Assert.Equal("DUPLICATE_PLAYER",
            Assert.IsType<Result<PairingPlan>.Failure>(PairingPlanner.Create(
                new[] { Raw("A", 1000), Raw("A", 1005) }, RatingPolicies.WinLoss, 100)).Error.Code);
    }

    [Fact] public void InvalidMatchIsExcludedAndInputIsUnchanged()
    {
        var input = new List<MatchRecord> { Raw("A", 1000), Raw("B", 1000), Raw("Bad", 1000) with { Kills = -1 } };
        var before = input.ToArray();
        var plan = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(input, RatingPolicies.WinLoss, 0)).Value;
        Assert.Single(plan.Pairs);
        Assert.Equal(before, input);
    }

    [Fact] public void GapUsesWidenedArithmeticAtIntegerExtremes()
    {
        var plan = Assert.IsType<Result<PairingPlan>.Success>(PairingPlanner.Create(
            new[] { Raw("A", 0), Raw("B", int.MaxValue - 30) }, RatingPolicies.WinLoss, 100)).Value;
        Assert.Empty(plan.Pairs);
        Assert.Equal(2, plan.Waiting.Length);
    }
}
