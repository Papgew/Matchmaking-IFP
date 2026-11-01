using System.Collections.Immutable;

namespace Matchmaking.Core;

public sealed record ReportEntry(MatchRecord Match, PlayerMatchResult Rating, int NativeBonus);
public sealed record RankTotal(Rank Rank, int Players, int TotalNewMmr, int TotalNativeBonus);
public sealed record PlayerReportRow(string PlayerId, int NewMmr, Rank Rank, int NativeBonus, int ScoreA, int ScoreB);

public static class MatchReportExtensions
{
    // Receiver is a domain query: filtering remains composable and deferred.
    public static IEnumerable<ReportEntry> WithMinimumMmr(this IEnumerable<ReportEntry> entries, int minimum) =>
        entries.Where(e => e.Rating.NewMmr >= minimum);

    // Receiver is a domain query: converts matches into reusable rank aggregates.
    public static ImmutableArray<RankTotal> ToRankTotals(this IEnumerable<ReportEntry> entries) =>
        entries.GroupBy(e => e.Rating.Rank)
            .Select(g => new RankTotal(g.Key, g.Count(), g.Sum(x => x.Rating.NewMmr), g.Sum(x => x.NativeBonus)))
            .OrderByDescending(g => g.TotalNewMmr)
            .ThenBy(g => g.Rank)
            .ToImmutableArray();
}

public static class MatchReports
{
    // Currying: configuration is fixed first; a match is supplied later.
    public static Func<MatchRecord, int> CreateScore(int winWeight) =>
        match => (match.IsWin ? winWeight : -winWeight) + Math.Clamp(match.Kills - match.Deaths, -3, 3);

    // Report 1: useful to players comparing their rating and two configured scores.
    public static ImmutableArray<PlayerReportRow> PlayerComparison(
        IEnumerable<ReportEntry> entries, int minimumMmr,
        Func<MatchRecord, int> scoreA, Func<MatchRecord, int> scoreB) =>
        entries.WithMinimumMmr(minimumMmr)
            .Select(e => new PlayerReportRow(e.Match.PlayerId, e.Rating.NewMmr,
                e.Rating.Rank, e.NativeBonus, scoreA(e.Match), scoreB(e.Match)))
            .OrderByDescending(e => e.NewMmr)
            .ThenBy(e => e.PlayerId, StringComparer.Ordinal)
            .ToImmutableArray();

    // Report 2: useful to administrators monitoring rank distribution.
    public static ImmutableArray<RankTotal> RankDistribution(IEnumerable<ReportEntry> entries) =>
        entries.ToRankTotals();

    // Deferred domain stream: no work until iteration. Caller chooses materialisation.
    public static IEnumerable<ReportEntry> StreamEntries(
        IEnumerable<MatchRecord> records,
        Func<MatchRecord, Result<ReportEntry>> build)
    {
        foreach (var match in records)
        {
            var result = build(match);
            if (result is Result<ReportEntry>.Success success)
                yield return success.Value;
        }
    }
}

public static class ReportWorkflow
{
    public static Result<ImmutableArray<ReportEntry>> Build(
        IEnumerable<MatchRecord> records, Func<MatchRecord, int> policy,
        Func<int, int, int, Result<int>> nativeBonus)
    {
        // Snapshot prevents repeat enumeration of a potentially external source.
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
