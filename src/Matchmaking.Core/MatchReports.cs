using System.Collections.Immutable;

namespace Matchmaking.Core;

public sealed record ReportEntry(MatchRecord Match, PlayerMatchResult Rating, int NativeBonus);
public sealed record RankTotal(Rank Rank, int Players, int TotalNewMmr, int TotalNativeBonus);
public sealed record PlayerReportRow(string PlayerId, int NewMmr, Rank Rank, int NativeBonus, int ScoreA, int ScoreB);

public static class MatchReportExtensions
{
    // Deferred, reusable domain filter.
    public static IEnumerable<ReportEntry> WithMinimumMmr(this IEnumerable<ReportEntry> entries, int minimum) =>
        entries.Where(entry => entry.Rating.NewMmr >= minimum);

    // Deliberate materialisation of grouped and aggregated domain values.
    public static ImmutableArray<RankTotal> ToRankTotals(this IEnumerable<ReportEntry> entries) =>
        entries.GroupBy(entry => entry.Rating.Rank)
            .Select(group => new RankTotal(group.Key, group.Count(),
                group.Sum(item => item.Rating.NewMmr), group.Sum(item => item.NativeBonus)))
            .OrderByDescending(total => total.TotalNewMmr)
            .ThenBy(total => total.Rank)
            .ToImmutableArray();
}

public static class MatchReports
{
    // Partially apply winWeight to obtain a match-scoring function.
    public static Func<MatchRecord, int> CreateScore(int winWeight) =>
        match => (match.IsWin ? winWeight : -winWeight) +
                 Math.Clamp(match.Kills - match.Deaths, -3, 3);

    // Report 1: per-player rating and two configured score calculations.
    public static ImmutableArray<PlayerReportRow> PlayerComparison(
        IEnumerable<ReportEntry> entries, int minimumMmr,
        Func<MatchRecord, int> scoreA, Func<MatchRecord, int> scoreB) =>
        entries.WithMinimumMmr(minimumMmr)
            .Select(entry => new PlayerReportRow(entry.Match.PlayerId, entry.Rating.NewMmr,
                entry.Rating.Rank, entry.NativeBonus, scoreA(entry.Match), scoreB(entry.Match)))
            .OrderByDescending(row => row.NewMmr)
            .ThenBy(row => row.PlayerId, StringComparer.Ordinal)
            .ToImmutableArray();

    // Report 2: grouped rank distribution for administrators.
    public static ImmutableArray<RankTotal> RankDistribution(IEnumerable<ReportEntry> entries) =>
        entries.ToRankTotals();

    // Building the iterator does not read any entries until enumerated.
    public static IEnumerable<ReportEntry> StreamRankedEntries(
        IEnumerable<ReportEntry> entries, int minimumMmr)
    {
        foreach (var entry in entries)
            if (entry.Rating.NewMmr >= minimumMmr)
                yield return entry;
    }
}

public static class ReportWorkflow
{
    public static Result<ImmutableArray<ReportEntry>> Build(
        IEnumerable<MatchRecord> records, Func<MatchRecord, int> policy,
        Func<int, int, int, Result<int>> nativeBonus)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(nativeBonus);

        var snapshot = records.ToImmutableArray();
        var builder = ImmutableArray.CreateBuilder<ReportEntry>();
        foreach (var match in snapshot)
        {
            var rated = RatingWorkflow.RatePlayer(new[] { match }, match.PlayerId, policy);
            if (rated is Result<PlayerMatchResult>.Failure failed)
                return new Result<ImmutableArray<ReportEntry>>.Failure(failed.Error);
            var rating = ((Result<PlayerMatchResult>.Success)rated).Value;

            var bonus = nativeBonus(match.Kills, match.Deaths, 2);
            if (bonus is Result<int>.Failure nativeFailed)
                return new Result<ImmutableArray<ReportEntry>>.Failure(nativeFailed.Error);

            builder.Add(new ReportEntry(match, rating, ((Result<int>.Success)bonus).Value));
        }
        return Result<ImmutableArray<ReportEntry>>.Ok(builder.ToImmutable());
    }
}
