using Matchmaking.Core;

var path = args.Length > 0 ? args[0] : "data/matches.csv";
var mode = args.Length > 1 ? args[1] : "both";
var playerId = args.Length > 2 ? args[2] : null;

var loaded = CsvDataLoader.Load(path);
if (loaded is Result<IReadOnlyList<MatchRecord>>.Failure fileFailure)
{
    Console.Error.WriteLine($"{fileFailure.Error.Code}: {fileFailure.Error.Message}");
    return 1;
}
var records = ((Result<IReadOnlyList<MatchRecord>>.Success)loaded).Value;

// Assignment 8: reports use real parsed CSV records, native bonus, and two configured scores.
if (mode.Equals("reports", StringComparison.OrdinalIgnoreCase))
{
    var built = ReportWorkflow.Build(records, RatingPolicies.WinLoss, NativeBonus.Compute);
    if (built is Result<System.Collections.Immutable.ImmutableArray<ReportEntry>>.Failure failed)
    {
        Console.Error.WriteLine($"{failed.Error.Code}: {failed.Error.Message}");
        return 1;
    }

    var entries = MatchReports.StreamRankedEntries(\n        ((Result<System.Collections.Immutable.ImmutableArray<ReportEntry>>.Success)built).Value, 0)\n        .ToArray(); // One stable snapshot reused by both reports.
    var scoreA = MatchReports.CreateScore(25);
    var scoreB = MatchReports.CreateScore(40);
    var players = MatchReports.PlayerComparison(entries, 0, scoreA, scoreB);
    var ranks = MatchReports.RankDistribution(entries);

    Console.WriteLine("=== Player comparison (Policy A, native bonus, two configured scores) ===");
    foreach (var p in players)
        Console.WriteLine($"{p.PlayerId}: MMR={p.NewMmr}, Rank={p.Rank}, NativeBonus={p.NativeBonus}, Score25={p.ScoreA}, Score40={p.ScoreB}");

    Console.WriteLine("=== Rank distribution ===");
    foreach (var rank in ranks)
        Console.WriteLine($"{rank.Rank}: Players={rank.Players}, TotalMMR={rank.TotalNewMmr}, TotalNativeBonus={rank.TotalNativeBonus}");

    // Optional export is owned by the app; writer is disposed even when writing fails.
    if (args.Length > 2)
    {
        try
        {
            using var writer = new StreamWriter(args[2], false);
            writer.WriteLine("PlayerId,NewMmr,Rank,NativeBonus,Score25,Score40");
            foreach (var p in players)
                writer.WriteLine($"{p.PlayerId},{p.NewMmr},{p.Rank},{p.NativeBonus},{p.ScoreA},{p.ScoreB}");
            Console.WriteLine($"Saved player report to {args[2]}");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"REPORT_IO_ERROR: {ex.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"REPORT_ACCESS_DENIED: {ex.Message}");
            return 1;
        }
    }
    return 0;
}

var names = mode.Equals("both", StringComparison.OrdinalIgnoreCase)
    ? new[] { "A", "B" }
    : new[] { mode };

foreach (var name in names)
{
    if (!PolicySelector.TryGet(name, out var policy))
    {
        Console.Error.WriteLine($"UNKNOWN_POLICY: '{name}'. Use A, B or both.");
        return 2;
    }

    if (playerId is not null)
    {
        var single = RatingWorkflow.RatePlayer(records, playerId, policy);
        switch (single)
        {
            case Result<PlayerMatchResult>.Failure failure:
                Console.Error.WriteLine($"{failure.Error.Code}: {failure.Error.Message}");
                return 1;
            case Result<PlayerMatchResult>.Success success:
                PrintResults(name, new[] { success.Value });
                break;
        }
    }
    else
    {
        var all = RatingWorkflow.RateAll(records, policy);
        switch (all)
        {
            case Result<System.Collections.Immutable.ImmutableArray<PlayerMatchResult>>.Failure failure:
                Console.Error.WriteLine($"{failure.Error.Code}: {failure.Error.Message}");
                return 1;
            case Result<System.Collections.Immutable.ImmutableArray<PlayerMatchResult>>.Success success:
                PrintResults(name, success.Value);
                break;
        }
    }
}
return 0;

static void PrintResults(string policyName, IEnumerable<PlayerMatchResult> results)
{
    Console.WriteLine($"=== Policy {policyName.ToUpperInvariant()} ===");
    Console.WriteLine($"{"Player",-8}{"Old",6}{"Delta",7}{"New",6}  Rank");
    foreach (var r in results)
        Console.WriteLine($"{r.PlayerId,-8}{r.OldMmr,6}{r.MmrDelta,7:+#;-#;0}{r.NewMmr,6}  {r.Rank}");
    Console.WriteLine();
}
