using Matchmaking.Core;
using Xunit;

namespace Matchmaking.Tests;

public class ResultWorkflowTests
{
    private static MatchRecord Valid(string id = "P001", int mmr = 1000) => new(id, mmr, 0, 0, 3, true);

    [Fact] public void CompleteChainReturnsCorrectRating()
    {
        var result = Assert.IsType<Result<PlayerMatchResult>.Success>(RatingWorkflow.RatePlayer(new[] { Valid() }, "P001", RatingPolicies.WinLoss));
        Assert.Equal(1025, result.Value.NewMmr);
    }

    [Fact] public void MissingPlayerHasUsefulFailure()
    {
        var result = Assert.IsType<Result<PlayerMatchResult>.Failure>(RatingWorkflow.RatePlayer(new[] { Valid() }, "unknown", RatingPolicies.WinLoss));
        Assert.Equal("PLAYER_NOT_FOUND", result.Error.Code);
        Assert.Contains("unknown", result.Error.Message);
    }

    [Fact] public void InvalidMatchHasUsefulFailure()
    {
        var result = Assert.IsType<Result<PlayerMatchResult>.Failure>(RatingWorkflow.RatePlayer(new[] { Valid() with { Kills = -1 } }, "P001", RatingPolicies.WinLoss));
        Assert.Equal("INVALID_MATCH", result.Error.Code);
        Assert.Contains("Kills", result.Error.Message);
    }

    [Fact] public void IllegalTransitionHasUsefulFailure()
    {
        var validated = Assert.IsType<ValidationOutcome.Valid>(MatchWorkflow.Validate(Valid()));
        var pending = MatchWorkflow.Start(validated.Match);
        var result = Assert.IsType<Result<MatchState.Completed>.Failure>(RatingWorkflow.Complete(pending, RatingPolicies.WinLoss));
        Assert.Equal("INVALID_TRANSITION", result.Error.Code);
        Assert.Contains("confirmed", result.Error.Message);
    }

    [Fact] public void LookupFindsZeroMmrRatherThanTreatingItAsMissing()
    {
        var found = Assert.IsType<Option<MatchRecord>.Some>(RatingWorkflow.FindPlayer(new[] { Valid(mmr: 0) }, "P001"));
        Assert.Equal(0, found.Value.CurrentMmr);
        Assert.IsType<Option<MatchRecord>.None>(RatingWorkflow.FindPlayer(new[] { Valid() }, "absent"));
    }

    [Fact] public void MapTransformsSuccess()
    {
        var result = Result<int>.Ok(2).Map(x => x * 3);
        Assert.Equal(6, Assert.IsType<Result<int>.Success>(result).Value);
    }

    [Fact] public void BindContinuesSuccess()
    {
        var result = Result<int>.Ok(2).Bind(x => Result<string>.Ok($"value={x}"));
        Assert.Equal("value=2", Assert.IsType<Result<string>.Success>(result).Value);
    }

    [Fact] public void MapAndBindSkipCallbacksOnFailure()
    {
        var calls = 0;
        var failed = Result<int>.Fail("TEST", "stop");
        var mapped = failed.Map(x => { calls++; return x + 1; });
        var bound = failed.Bind(x => { calls++; return Result<string>.Ok(x.ToString()); });
        Assert.Equal(0, calls);
        Assert.Equal("TEST", Assert.IsType<Result<int>.Failure>(mapped).Error.Code);
        Assert.Equal("TEST", Assert.IsType<Result<string>.Failure>(bound).Error.Code);
    }

    [Fact] public void MissingFileReturnsFailureWithoutPublishingResults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.csv");
        var failure = Assert.IsType<Result<IReadOnlyList<MatchRecord>>.Failure>(CsvDataLoader.Load(path));
        Assert.Equal("FILE_NOT_FOUND", failure.Error.Code);
    }

    [Fact] public void MalformedFileReturnsFailureWithoutPartialSuccess()
    {
        var path = Path.Combine(Path.GetTempPath(), $"matchmaking-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path, new[] { "PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin", "P001,1000,0,0,3,true", "broken,row" });
            var failure = Assert.IsType<Result<IReadOnlyList<MatchRecord>>.Failure>(CsvDataLoader.Load(path));
            Assert.Equal("MALFORMED_CSV", failure.Error.Code);
            Assert.Contains("Line 3", failure.Error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void BatchRejectsInvalidRecordWithoutReturningPartialRatings()
    {
        var result = RatingWorkflow.RateAll(new[] { Valid(), Valid("P002") with { Deaths = -1 } }, RatingPolicies.WinLoss);
        Assert.Equal("INVALID_MATCH", Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<PlayerMatchResult>>.Failure>(result).Error.Code);
    }

    [Fact] public void ValidCsvLoadsSuccessfully()
    {
        var path = Path.Combine(Path.GetTempPath(), $"matchmaking-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin\nP001,0,0,0,3,false\n");
            var success = Assert.IsType<Result<IReadOnlyList<MatchRecord>>.Success>(CsvDataLoader.Load(path));
            Assert.Single(success.Value);
            Assert.Equal(0, success.Value[0].CurrentMmr);
        }
        finally { File.Delete(path); }
    }
}
