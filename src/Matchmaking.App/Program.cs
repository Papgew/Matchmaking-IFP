using System.Collections.Immutable;
using System.Globalization;
using Matchmaking.Core;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

// Run from the repository root. All paths are explicit or relative to the working directory.
var path = args.Length > 0 ? args[0] : "data/matches.csv";
var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "both";

var loaded = CsvDataLoader.Load(path);
if (loaded is Result<IReadOnlyList<MatchRecord>>.Failure fileFailure)
{
    PrintError(fileFailure.Error);
    return 1;
}

var source = ((Result<IReadOnlyList<MatchRecord>>.Success)loaded).Value;
var playable = Stages.FilterValidMatches(source);
foreach (var record in source.Where(record => !Stages.IsPlayableMatch(record)))
{
    if (ValidatedMatch.Create(record) is ValidationOutcome.Invalid invalid)
        Console.Error.WriteLine($"SKIPPED {record.PlayerId}: {invalid.Reason}");
}

switch (mode)
{
    case "reports":
    {
        if (args.Length > 3) { PrintUsage(); return 2; }
        var built = ReportWorkflow.Build(playable, RatingPolicies.WinLoss, NativeBonus.Compute);
        if (built is Result<ImmutableArray<ReportEntry>>.Failure failure)
        {
            PrintError(failure.Error);
            return 1;
        }

        // Lazy query is enumerated once. Both reports use a stable snapshot.
        var entries = MatchReports.StreamRankedEntries(
            ((Result<ImmutableArray<ReportEntry>>.Success)built).Value, 0).ToArray();
        var score25 = MatchReports.CreateScore(25);
        var score40 = MatchReports.CreateScore(40);
        var players = MatchReports.PlayerComparison(entries, 0, score25, score40);
        var ranks = MatchReports.RankDistribution(entries);

        Console.WriteLine("=== Player comparison: Policy A, native bonus, configured scores ===");
        foreach (var row in players)
            Console.WriteLine($"{row.PlayerId,-6} MMR={row.NewMmr,5} Rank={row.Rank,-9} Native={row.NativeBonus,3:+#;-#;0} Score25={row.ScoreA,3:+#;-#;0} Score40={row.ScoreB,3:+#;-#;0}");
        Console.WriteLine("=== Rank distribution ===");
        foreach (var rank in ranks)
            Console.WriteLine($"{rank.Rank,-9} Players={rank.Players} TotalMMR={rank.TotalNewMmr} NativeTotal={rank.TotalNativeBonus}");

        if (args.Length == 3)
        {
            var written = ExportReport(args[2], players);
            if (written is Result<int>.Failure exportFailure)
            {
                PrintError(exportFailure.Error);
                return 1;
            }
            Console.WriteLine($"Exported {players.Length} players to {args[2]}");
        }
        return 0;
    }

    case "pairs":
    {
        if (args.Length > 4) { PrintUsage(); return 2; }
        var selected = args.Length > 2 ? args[2] : "A";
        if (!PolicySelector.TryGet(selected, out var policy))
        {
            PrintError(new DomainError("UNKNOWN_POLICY", "Use policy A or B for pairing."));
            return 2;
        }
        if (args.Length > 3 &&
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            PrintError(new DomainError("INVALID_PAIR_GAP", "Maximum MMR gap must be a nonnegative integer."));
            return 2;
        }
        var gap = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 100;
        var proposed = PairingPlanner.Create(playable, policy, gap);
        if (proposed is Result<PairingPlan>.Failure failure)
        {
            PrintError(failure.Error);
            return 1;
        }
        var plan = ((Result<PairingPlan>.Success)proposed).Value;
        Console.WriteLine($"=== Player pairs (policy {selected.ToUpperInvariant()}, max gap {gap}) ===");
        foreach (var pair in plan.Pairs)
            Console.WriteLine($"{pair.PlayerA} ({pair.MmrA}) + {pair.PlayerB} ({pair.MmrB}); gap={pair.Difference}");
        Console.WriteLine($"Pairs: {plan.Pairs.Length}, waiting: {plan.Waiting.Length}");
        foreach (var waiting in plan.Waiting)
            Console.WriteLine($"Waiting: {waiting.PlayerId}, MMR={waiting.NewMmr}");
        return 0;
    }

    case "a":
    case "b":
    case "both":
    {
        if (args.Length > 3) { PrintUsage(); return 2; }
        var policies = mode == "both" ? new[] { "A", "B" } : new[] { mode.ToUpperInvariant() };
        foreach (var name in policies)
        {
            if (!PolicySelector.TryGet(name, out var policy))
                return 2;

            if (args.Length == 3)
            {
                // Lookup explicitly uses Option, then Map/Bind with the safe state workflow.
                var rated = RatingWorkflow.RatePlayer(source, args[2], policy);
                if (rated is Result<PlayerMatchResult>.Failure failure)
                {
                    PrintError(failure.Error);
                    return 1;
                }
                PrintRatings(name, new[] { ((Result<PlayerMatchResult>.Success)rated).Value });
            }
            else
            {
                // Assignment 5's named pipeline is used in the real application.
                // Stage 1 filters invalid domain records; stages 2/3 rate and rank.
                var rated = MatchmakingPipeline.Run(source, policy);
                PrintRatings(name, rated);
            }
        }
        return 0;
    }

    default:
        PrintError(new DomainError("UNKNOWN_MODE", "Use A, B, both, reports, or pairs."));
        PrintUsage();
        return 2;
}

static Result<int> ExportReport(string outputPath, IEnumerable<PlayerReportRow> rows)
{
    try
    {
        using var writer = new StreamWriter(outputPath, false);
        writer.WriteLine("PlayerId,NewMmr,Rank,NativeBonus,Score25,Score40");
        var count = 0;
        foreach (var row in rows)
        {
            writer.WriteLine($"{row.PlayerId},{row.NewMmr},{row.Rank},{row.NativeBonus},{row.ScoreA},{row.ScoreB}");
            count++;
        }
        return Result<int>.Ok(count);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Result<int>.Fail("REPORT_ACCESS_DENIED", ex.Message);
    }
    catch (IOException ex)
    {
        return Result<int>.Fail("REPORT_IO_ERROR", ex.Message);
    }
    catch (ArgumentException ex)
    {
        return Result<int>.Fail("REPORT_INVALID_PATH", ex.Message);
    }
}

static void PrintRatings(string policyName, IEnumerable<PlayerMatchResult> results)
{
    Console.WriteLine($"=== Policy {policyName.ToUpperInvariant()} ===");
    Console.WriteLine($"{"Player",-8}{"Old",6}{"Delta",7}{"New",6}  Rank");
    foreach (var row in results)
        Console.WriteLine($"{row.PlayerId,-8}{row.OldMmr,6}{row.MmrDelta,7:+#;-#;0}{row.NewMmr,6}  {row.Rank}");
    Console.WriteLine();
}

static void PrintError(DomainError error) =>
    Console.Error.WriteLine($"{error.Code}: {error.Message}");

static void PrintUsage()
{
    Console.WriteLine("Matchmaking & Skill Rating (.NET 8)");
    Console.WriteLine("Run from the repository root:");
    Console.WriteLine("  dotnet run --project src/Matchmaking.App -- data/matches.csv both");
    Console.WriteLine("  dotnet run --project src/Matchmaking.App -- data/matches.csv A P001");
    Console.WriteLine("  dotnet run --project src/Matchmaking.App -- data/matches.csv pairs A 100");
    Console.WriteLine("  dotnet run --project src/Matchmaking.App -- data/matches.csv reports player-report.csv");
}
