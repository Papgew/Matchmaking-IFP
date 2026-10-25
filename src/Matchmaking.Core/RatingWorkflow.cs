using System.Collections.Immutable;

namespace Matchmaking.Core;

public static class RatingWorkflow
{
    public static Option<MatchRecord> FindPlayer(IEnumerable<MatchRecord> records, string playerId)
    {
        foreach (var record in records)
            if (string.Equals(record.PlayerId, playerId, StringComparison.Ordinal))
                return new Option<MatchRecord>.Some(record);
        return new Option<MatchRecord>.None();
    }

    public static Result<MatchRecord> RequirePlayer(IEnumerable<MatchRecord> records, string playerId) =>
        FindPlayer(records, playerId) switch
        {
            Option<MatchRecord>.Some found => Result<MatchRecord>.Ok(found.Value),
            Option<MatchRecord>.None => Result<MatchRecord>.Fail("PLAYER_NOT_FOUND", $"Player '{playerId}' was not found."),
            _ => throw new InvalidOperationException("Unknown lookup state.")
        };

    public static Result<ValidatedMatch> Validate(MatchRecord record) => MatchWorkflow.Validate(record) switch
    {
        ValidationOutcome.Valid valid => Result<ValidatedMatch>.Ok(valid.Match),
        ValidationOutcome.Invalid invalid => Result<ValidatedMatch>.Fail("INVALID_MATCH", invalid.Reason),
        _ => throw new InvalidOperationException("Unknown validation outcome.")
    };

    public static Result<MatchState> Confirm(MatchState state) => MatchWorkflow.Confirm(state) switch
    {
        TransitionOutcome.Success success => Result<MatchState>.Ok(success.State),
        TransitionOutcome.Rejected rejected => Result<MatchState>.Fail("INVALID_TRANSITION", rejected.Reason),
        _ => throw new InvalidOperationException("Unknown transition outcome.")
    };

    public static Result<MatchState.Completed> Complete(MatchState state, Func<MatchRecord, int> policy) =>
        MatchWorkflow.Complete(state, policy) switch
        {
            TransitionOutcome.Success { State: MatchState.Completed completed } => Result<MatchState.Completed>.Ok(completed),
            TransitionOutcome.Rejected rejected => Result<MatchState.Completed>.Fail("INVALID_TRANSITION", rejected.Reason),
            _ => throw new InvalidOperationException("Unexpected completion outcome.")
        };

    public static Result<PlayerMatchResult> RatePlayer(
        IEnumerable<MatchRecord> records, string playerId, Func<MatchRecord, int> policy) =>
        RequirePlayer(records, playerId)
            .Bind(Validate)
            .Map(valid => (MatchState)MatchWorkflow.Start(valid))
            .Bind(Confirm)
            .Bind(state => Complete(state, policy))
            .Map(completed => completed.Result);

    public static Result<ImmutableArray<PlayerMatchResult>> RateAll(
        IEnumerable<MatchRecord> records, Func<MatchRecord, int> policy)
    {
        var snapshot = records.ToImmutableArray();
        var builder = ImmutableArray.CreateBuilder<PlayerMatchResult>();
        foreach (var record in snapshot)
        {
            var result = Validate(record)
                .Map(valid => (MatchState)MatchWorkflow.Start(valid))
                .Bind(Confirm)
                .Bind(state => Complete(state, policy))
                .Map(completed => completed.Result);
            switch (result)
            {
                case Result<PlayerMatchResult>.Success success:
                    builder.Add(success.Value);
                    break;
                case Result<PlayerMatchResult>.Failure failure:
                    return new Result<ImmutableArray<PlayerMatchResult>>.Failure(failure.Error);
            }
        }
        return Result<ImmutableArray<PlayerMatchResult>>.Ok(builder.ToImmutable()
            .OrderByDescending(r => r.NewMmr)
            .ThenBy(r => r.PlayerId, StringComparer.Ordinal).ToImmutableArray());
    }
}
