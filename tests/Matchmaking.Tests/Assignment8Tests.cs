using Matchmaking.Core;
using Xunit;

namespace Matchmaking.Tests;

public class Assignment8Tests
{
    private static MatchRecord Raw(string id = "P001", int mmr = 1000, int kills = 5, int deaths = 2, bool win = true) =>
        new(id, mmr, kills, deaths, 10, win);

    private static ReportEntry Entry(string id, int newMmr, Rank rank, int bonus = 6) =>
        new(Raw(id, newMmr), new PlayerMatchResult(id, newMmr, 0, newMmr, rank), bonus);

    [Fact] public void MinimumMmrExtensionFiltersDomainValues()
    {
        var result = new[] { Entry("low", 900, Rank.Bronze), Entry("high", 1500, Rank.Gold) }.WithMinimumMmr(1000).ToArray();
        Assert.Equal("high", Assert.Single(result).Match.PlayerId);
    }

    [Fact] public void RankTotalsExtensionGroupsAndAggregates()
    {
        var result = new[] { Entry("A", 1500, Rank.Gold, 6), Entry("B", 1400, Rank.Gold, 4), Entry("C", 900, Rank.Bronze, 2) }.ToRankTotals();
        Assert.Equal(Rank.Gold, result[0].Rank);
        Assert.Equal(2, result[0].Players);
        Assert.Equal(2900, result[0].TotalNewMmr);
        Assert.Equal(10, result[0].TotalNativeBonus);
    }

    [Fact] public void PlayerReportUsesTieBreakerAndFiltering()
    {
        var input = new[] { Entry("B", 1500, Rank.Gold), Entry("A", 1500, Rank.Gold), Entry("C", 900, Rank.Bronze) };
        var result = MatchReports.PlayerComparison(input, 1000, MatchReports.CreateScore(25), MatchReports.CreateScore(40));
        Assert.Equal(new[] { "A", "B" }, result.Select(x => x.PlayerId));
    }

    [Fact] public void BothReportsHandleEmptyDataset()
    {
        Assert.Empty(MatchReports.PlayerComparison(Array.Empty<ReportEntry>(), 0, MatchReports.CreateScore(25), MatchReports.CreateScore(40)));
        Assert.Empty(MatchReports.RankDistribution(Array.Empty<ReportEntry>()));
    }

    [Fact] public void ReportWithNoMatchesIsEmpty()
    {
        Assert.Empty(MatchReports.PlayerComparison(new[] { Entry("A", 1000, Rank.Silver) }, 2000,
            MatchReports.CreateScore(25), MatchReports.CreateScore(40)));
    }

    [Fact] public void CurriedConfigurationsGiveDifferentResults()
    {
        var a = MatchReports.CreateScore(25);
        var b = MatchReports.CreateScore(40);
        Assert.Equal(28, a(Raw()));
        Assert.Equal(43, b(Raw()));
    }

    [Fact] public void DeferredProcessingOnlyStartsOnIterationAndSnapshotDoesNotRepeat()
    {
        var observed = 0;
        IEnumerable<ReportEntry> Source()
        {
            observed++;
            yield return Entry("A", 1200, Rank.Silver);
            observed++;
            yield return Entry("B", 900, Rank.Bronze);
        }
        var deferred = MatchReports.StreamRankedEntries(Source(), 1000);
        Assert.Equal(0, observed);
        var snapshot = deferred.ToArray();
        Assert.Equal(2, observed);
        Assert.Single(snapshot);
        Assert.Single(snapshot);
        Assert.Equal(2, observed);
        Assert.Single(deferred);
        Assert.Equal(4, observed);
    }

    [Fact] public void NativeCallMatchesManualCalculation()
    {
        // (10 - 2) * 2 = 16, within [-30, 30].
        var result = Assert.IsType<Result<int>.Success>(NativeBonus.Compute(10, 2, 2));
        Assert.Equal(16, result.Value);
    }

    [Fact] public void NativeCallClampsLargeScore()
    {
        Assert.Equal(30, Assert.IsType<Result<int>.Success>(NativeBonus.Compute(100, 0, 2)).Value);
    }

    [Fact] public void NativeInvalidInputRejectedBeforeCall()
    {
        var calls = 0;
        var result = NativeBonus.ComputeWith(-1, 0, 2, (k, d, w) => { calls++; return 0; });
        Assert.Equal("INVALID_NATIVE_INPUT", Assert.IsType<Result<int>.Failure>(result).Error.Code);
        Assert.Equal(0, calls);
    }

    [Fact] public void NativeLoadFailureBecomesResult()
    {
        var result = NativeBonus.ComputeWith(5, 2, 2, (k, d, w) => throw new DllNotFoundException("test missing library"));
        Assert.Equal("NATIVE_LIBRARY_MISSING", Assert.IsType<Result<int>.Failure>(result).Error.Code);
    }

    [Fact] public void CompleteWorkflowProducesTwoReportsAndUsesNative()
    {
        var built = Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<ReportEntry>>.Success>(
            ReportWorkflow.Build(new[] { Raw("A"), Raw("B", 1500, 1, 2, false) },
                RatingPolicies.WinLoss, NativeBonus.Compute));
        var snapshot = MatchReports.StreamRankedEntries(built.Value, 0).ToArray();
        var players = MatchReports.PlayerComparison(snapshot, 0, MatchReports.CreateScore(25), MatchReports.CreateScore(40));
        var ranks = MatchReports.RankDistribution(snapshot);
        Assert.Equal(2, players.Length);
        Assert.Equal(6, players.Single(x => x.PlayerId == "A").NativeBonus);
        Assert.Equal(2, ranks.Sum(x => x.Players));
    }

    [Fact] public void FailedNativeWorkflowReturnsNoPartialBatch()
    {
        var calls = 0;
        Result<int> Failing(int kills, int deaths, int weight)
        {
            calls++;
            return calls == 2 ? Result<int>.Fail("NATIVE_LIBRARY_MISSING", "controlled failure") : Result<int>.Ok(6);
        }
        var result = ReportWorkflow.Build(new[] { Raw("A"), Raw("B") }, RatingPolicies.WinLoss, Failing);
        Assert.Equal("NATIVE_LIBRARY_MISSING",
            Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<ReportEntry>>.Failure>(result).Error.Code);
        Assert.Equal(2, calls);
    }

    [Fact] public void InvalidDomainRecordPreventsNativeCall()
    {
        var calls = 0;
        var result = ReportWorkflow.Build(new[] { Raw() with { Deaths = -1 } }, RatingPolicies.WinLoss,
            (k, d, w) => { calls++; return Result<int>.Ok(0); });
        Assert.Equal("INVALID_MATCH",
            Assert.IsType<Result<System.Collections.Immutable.ImmutableArray<ReportEntry>>.Failure>(result).Error.Code);
        Assert.Equal(0, calls);
    }

    [Fact] public void StreamWriterReleasesFileAfterSuccess()
    {
        var path = Path.Combine(Path.GetTempPath(), $"a8-{Guid.NewGuid():N}.csv");
        try
        {
            using (var writer = new StreamWriter(path))
                writer.WriteLine("A,1000");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(stream.Length > 0);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void StreamWriterReleasesFileAfterException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"a8-{Guid.NewGuid():N}.csv");
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var writer = new StreamWriter(path);
                writer.WriteLine("test");
                throw new InvalidOperationException("simulated write failure");
            });
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(stream.Length > 0);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void FileBoundaryMissingFileStillFails()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.csv");
        Assert.Equal("FILE_NOT_FOUND", Assert.IsType<Result<IReadOnlyList<MatchRecord>>.Failure>(CsvDataLoader.Load(path)).Error.Code);
    }

    [Fact] public void FileBoundaryMalformedCsvStillFails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"a8-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin\nbad,line\n");
            Assert.Equal("MALFORMED_CSV",
                Assert.IsType<Result<IReadOnlyList<MatchRecord>>.Failure>(CsvDataLoader.Load(path)).Error.Code);
        }
        finally { File.Delete(path); }
    }
}
