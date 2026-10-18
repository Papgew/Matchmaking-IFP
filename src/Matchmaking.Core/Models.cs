using System.Collections.Immutable;

namespace Matchmaking.Core;

public enum Rank { Bronze, Silver, Gold, Platinum, Diamond }

public sealed record MatchRecord(
    string PlayerId,
    int CurrentMmr,
    int Kills,
    int Deaths,
    double MatchDurationMinutes,
    bool IsWin);

public sealed record RatedMatch(MatchRecord Match, int MmrDelta, int NewMmr);

public sealed record PlayerMatchResult(
    string PlayerId,
    int OldMmr,
    int MmrDelta,
    int NewMmr,
    Rank Rank);

public sealed record ParseOutcome(
    ImmutableArray<MatchRecord> Matches,
    ImmutableArray<string> Errors)
{
    public bool IsValid => Errors.IsEmpty;
}
