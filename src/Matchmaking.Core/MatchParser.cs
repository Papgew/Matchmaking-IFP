using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Matchmaking.Core;

public static class MatchParser
{
    private const int FieldCount = 6;

    public static ParseOutcome ParseCsv(IEnumerable<string>? lines)
    {
        if (lines is null)
            throw new ArgumentNullException(nameof(lines), "Input data is missing (null).");

        var matches = ImmutableArray.CreateBuilder<MatchRecord>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var lineNo = 0;

        foreach (var line in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (lineNo == 1 && line.TrimStart().StartsWith("PlayerId", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseLine(line, lineNo, out var record, out var error))
                matches.Add(record);
            else
                errors.Add(error);
        }

        return new ParseOutcome(matches.ToImmutable(), errors.ToImmutable());
    }

    public static bool TryParseLine(
        string line,
        int lineNo,
        [NotNullWhen(true)] out MatchRecord? record,
        [NotNullWhen(false)] out string? error)
    {
        record = null;
        var f = line.Split(',').Select(s => s.Trim()).ToArray();

        if (f.Length != FieldCount)
        {
            error = $"Line {lineNo}: expected {FieldCount} fields, got {f.Length}.";
            return false;
        }
        if (f[0].Length == 0)
        {
            error = $"Line {lineNo}: PlayerId is empty.";
            return false;
        }
        if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mmr))
        {
            error = $"Line {lineNo}: CurrentMMR '{f[1]}' is not an integer.";
            return false;
        }
        if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kills))
        {
            error = $"Line {lineNo}: Kills '{f[2]}' is not an integer.";
            return false;
        }
        if (!int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var deaths))
        {
            error = $"Line {lineNo}: Deaths '{f[3]}' is not an integer.";
            return false;
        }
        if (!double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || !double.IsFinite(minutes))
        {
            error = $"Line {lineNo}: MatchDurationMinutes '{f[4]}' is not a valid number.";
            return false;
        }
        if (!bool.TryParse(f[5], out var isWin))
        {
            error = $"Line {lineNo}: IsWin '{f[5]}' must be 'true' or 'false'.";
            return false;
        }

        record = new MatchRecord(f[0], mmr, kills, deaths, minutes, isWin);
        error = null;
        return true;
    }
}
