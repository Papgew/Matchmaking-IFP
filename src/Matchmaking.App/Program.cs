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
