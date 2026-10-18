using Matchmaking.Core;

var path = args.Length > 0 ? args[0] : "data/matches.csv";
var mode = args.Length > 1 ? args[1] : "both";

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}
var lines = File.ReadAllLines(path);
var parsed = MatchParser.ParseCsv(lines);

if (!parsed.IsValid)
{
    Console.Error.WriteLine("Input rejected:");
    foreach (var e in parsed.Errors) Console.Error.WriteLine("  " + e);
    return 1;
}

var names = mode.Equals("both", StringComparison.OrdinalIgnoreCase)
    ? new[] { "A", "B" }
    : new[] { mode };

foreach (var name in names)
{
    if (!PolicySelector.TryGet(name, out var policy))
    {
        Console.Error.WriteLine($"Unknown policy '{name}'. Use A, B or both.");
        return 2;
    }
    var completed = new List<PlayerMatchResult>();
    foreach (var raw in parsed.Matches)
    {
        switch (MatchWorkflow.Validate(raw))
        {
            case ValidationOutcome.Invalid invalid:
                Console.Error.WriteLine($"Skipped {raw.PlayerId}: {invalid.Reason}");
                break;
            case ValidationOutcome.Valid valid:
                var pending = MatchWorkflow.Start(valid.Match);
                var confirmation = MatchWorkflow.Confirm(pending);
                if (confirmation is not TransitionOutcome.Success confirmed)
                    throw new InvalidOperationException("Unexpected confirmation failure.");
                var completion = MatchWorkflow.Complete(confirmed.State, policy);
                if (completion is TransitionOutcome.Success { State: MatchState.Completed done })
                    completed.Add(done.Result);
                else
                    throw new InvalidOperationException("Unexpected completion failure.");
                break;
        }
    }
    var results = completed.OrderByDescending(r => r.NewMmr)
        .ThenBy(r => r.PlayerId, StringComparer.Ordinal).ToArray();
    Console.WriteLine($"=== Policy {name.ToUpperInvariant()} ({parsed.Matches.Length} parsed, {results.Length} accepted) ===");
    Console.WriteLine($"{"Player",-8}{"Old",6}{"Delta",7}{"New",6}  Rank");
    foreach (var r in results)
        Console.WriteLine($"{r.PlayerId,-8}{r.OldMmr,6}{r.MmrDelta,7:+#;-#;0}{r.NewMmr,6}  {r.Rank}");
    Console.WriteLine();
}
return 0;
