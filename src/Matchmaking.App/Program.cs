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
    var results = MatchmakingPipeline.Run(parsed.Matches, policy);
    Console.WriteLine($"=== Policy {name.ToUpperInvariant()} ({parsed.Matches.Length} parsed, {results.Length} accepted) ===");
    Console.WriteLine($"{"Player",-8}{"Old",6}{"Delta",7}{"New",6}  Rank");
    foreach (var r in results)
        Console.WriteLine($"{r.PlayerId,-8}{r.OldMmr,6}{r.MmrDelta,7:+#;-#;0}{r.NewMmr,6}  {r.Rank}");
    Console.WriteLine();
}
return 0;
