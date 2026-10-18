using Matchmaking.Core;
using Xunit;

namespace Matchmaking.Tests;

public class WorkflowTests
{
    private static MatchRecord Raw(double duration = 3, int kills = 0) => new("P001", 1000, kills, 0, duration, true);
    private static MatchState.Pending Pending() => MatchWorkflow.Start(Assert.IsType<ValidationOutcome.Valid>(MatchWorkflow.Validate(Raw())).Match);

    [Fact] public void FactoryAcceptsExactBoundary() => Assert.IsType<ValidationOutcome.Valid>(MatchWorkflow.Validate(Raw()));
    [Fact] public void FactoryRejectsShortDuration() => Assert.Contains("duration", Assert.IsType<ValidationOutcome.Invalid>(MatchWorkflow.Validate(Raw(2.99))).Reason);
    [Fact] public void FactoryRejectsNegativeKills() => Assert.Contains("Kills", Assert.IsType<ValidationOutcome.Invalid>(MatchWorkflow.Validate(Raw(kills: -1))).Reason);
    [Fact] public void FactoryRejectsMissingPlayer() => Assert.Contains("PlayerId", Assert.IsType<ValidationOutcome.Invalid>(MatchWorkflow.Validate(Raw() with { PlayerId = " " })).Reason);
    [Fact] public void FactoryRejectsMissingMatch() => Assert.IsType<ValidationOutcome.Invalid>(MatchWorkflow.Validate(null));
    [Fact] public void CompleteBeforeConfirmRejectedAndStatePreserved()
    {
        var pending = Pending();
        var rejected = Assert.IsType<TransitionOutcome.Rejected>(MatchWorkflow.Complete(pending, RatingPolicies.WinLoss));
        Assert.Same(pending, rejected.OriginalState);
    }
    [Fact] public void ValidFlowCompletesAndCalculatesMmr()
    {
        var confirmed = Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Confirm(Pending()));
        Assert.IsType<MatchState.Confirmed>(confirmed.State);
        var completed = Assert.IsType<MatchState.Completed>(Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Complete(confirmed.State, RatingPolicies.WinLoss)).State);
        Assert.Equal(1025, completed.Result.NewMmr);
    }
    [Fact] public void RepeatedConfirmRejected()
    {
        var confirmed = Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Confirm(Pending())).State;
        var rejected = Assert.IsType<TransitionOutcome.Rejected>(MatchWorkflow.Confirm(confirmed));
        Assert.Same(confirmed, rejected.OriginalState);
    }
    [Fact] public void RepeatedCompletionRejected()
    {
        var confirmed = Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Confirm(Pending())).State;
        var completed = Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Complete(confirmed, RatingPolicies.WinLoss)).State;
        Assert.Same(completed, Assert.IsType<TransitionOutcome.Rejected>(MatchWorkflow.Complete(completed, RatingPolicies.WinLoss)).OriginalState);
    }
    [Fact] public void AllStateCasesHandled()
    {
        var pending = Pending();
        var confirmed = Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Confirm(pending)).State;
        var completed = Assert.IsType<TransitionOutcome.Success>(MatchWorkflow.Complete(confirmed, RatingPolicies.WinLoss)).State;
        Assert.IsType<TransitionOutcome.Rejected>(MatchWorkflow.Complete(pending, RatingPolicies.WinLoss));
        Assert.IsType<TransitionOutcome.Rejected>(MatchWorkflow.Confirm(confirmed));
        Assert.IsType<TransitionOutcome.Rejected>(MatchWorkflow.Confirm(completed));
    }
}
