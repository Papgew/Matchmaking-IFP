namespace Matchmaking.Core;

// Only the factory can create a validated match; raw MatchRecord remains a CSV input model.
public sealed class ValidatedMatch
{
    public MatchRecord Record { get; }
    private ValidatedMatch(MatchRecord record) => Record = record;

    public static ValidationOutcome Create(MatchRecord? record)
    {
        if (record is null) return new ValidationOutcome.Invalid("Match is missing.");
        if (string.IsNullOrWhiteSpace(record.PlayerId))
            return new ValidationOutcome.Invalid("PlayerId is required.");
        if (record.CurrentMmr < 0) return new ValidationOutcome.Invalid("CurrentMmr cannot be negative.");
        if (record.Kills < 0 || record.Deaths < 0)
            return new ValidationOutcome.Invalid("Kills and Deaths cannot be negative.");
        if (!double.IsFinite(record.MatchDurationMinutes) || record.MatchDurationMinutes < Stages.MinMatchMinutes)
            return new ValidationOutcome.Invalid($"Match duration must be at least {Stages.MinMatchMinutes} minutes.");
        return new ValidationOutcome.Valid(new ValidatedMatch(record));
    }
}

public abstract record ValidationOutcome
{
    private ValidationOutcome() { }
    public sealed record Valid(ValidatedMatch Match) : ValidationOutcome;
    public sealed record Invalid(string Reason) : ValidationOutcome;
}

public abstract record MatchState
{
    private MatchState() { }
    public sealed record Pending(ValidatedMatch Match) : MatchState;
    public sealed record Confirmed(ValidatedMatch Match) : MatchState;
    public sealed record Completed(PlayerMatchResult Result) : MatchState;
}

public abstract record TransitionOutcome
{
    private TransitionOutcome() { }
    public sealed record Success(MatchState State) : TransitionOutcome;
    public sealed record Rejected(string Reason, MatchState OriginalState) : TransitionOutcome;
}

public static class MatchWorkflow
{
    public static ValidationOutcome Validate(MatchRecord? raw) => ValidatedMatch.Create(raw);

    public static MatchState.Pending Start(ValidatedMatch match) => new(match);

    public static TransitionOutcome Confirm(MatchState state) => state switch
    {
        MatchState.Pending pending => new TransitionOutcome.Success(new MatchState.Confirmed(pending.Match)),
        MatchState.Confirmed => new TransitionOutcome.Rejected("Match is already confirmed.", state),
        MatchState.Completed => new TransitionOutcome.Rejected("Completed match cannot be confirmed again.", state),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static TransitionOutcome Complete(MatchState state, Func<MatchRecord, int> policy) => state switch
    {
        MatchState.Pending => new TransitionOutcome.Rejected("Match must be confirmed before completion.", state),
        MatchState.Completed => new TransitionOutcome.Rejected("Match is already completed.", state),
        MatchState.Confirmed confirmed => CompleteConfirmed(confirmed, policy),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static TransitionOutcome CompleteConfirmed(MatchState.Confirmed confirmed, Func<MatchRecord, int> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var results = Stages.BuildPlayerResults(Stages.ApplyMmrPolicy(new[] { confirmed.Match.Record }, policy));
        return new TransitionOutcome.Success(new MatchState.Completed(results[0]));
    }

}
