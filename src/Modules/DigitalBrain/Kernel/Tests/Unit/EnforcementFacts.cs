using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class EnforcementFacts
{
    [Fact]
    public async Task PrincipalFromANonEdgeCallerIsRejected()
    {
        RequestContext.Clear();
        var forged = CallerContext(CallerKind.App, TrustedEdge.AuthenticatedHttp);
        Assert.Throws<UntrustedCallerException>(() => CallerContextStamper.Stamp(forged));

        var filter = new CallFilter([]);
        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = forged,
            TargetNeuron = "neuron-1",
            Operation = "Read",
        }, TestContext.Current.CancellationToken);

        Assert.False(decision.Allowed);
        Assert.Equal(CallDenial.UntrustedCaller, decision.Denial);
    }

    [Fact]
    public async Task MissingPrincipalIsNotAuthenticated()
    {
        RequestContext.Clear();
        var filter = new CallFilter([]);
        var anonymous = CallerContext(CallerKind.User, TrustedEdge.AuthenticatedHttp) with { PrincipalId = "" };
        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = anonymous,
            TargetNeuron = "neuron-1",
            Operation = "Read",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(CallDenial.NotAuthenticated, decision.Denial);
    }

    [Fact]
    public async Task EdgeStampedPrincipalWithNoStageAbstainingIsAllowed()
    {
        RequestContext.Clear();
        var stamped = CallerContextStamper.Stamp(CallerContext(CallerKind.User, TrustedEdge.AuthenticatedHttp));
        var filter = new CallFilter([]);
        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = stamped,
            TargetNeuron = "neuron-1",
            Operation = "Read",
        }, TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task FabricatedPrincipalThatDisagreesWithTheEdgeStampIsRejected()
    {
        RequestContext.Clear();
        CallerContextStamper.Stamp(CallerContext(CallerKind.User, TrustedEdge.AuthenticatedHttp) with { PrincipalId = "owner" });
        var forged = CallerContext(CallerKind.User, TrustedEdge.AuthenticatedHttp) with { PrincipalId = "intruder" };
        var filter = new CallFilter([]);
        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = forged,
            TargetNeuron = "neuron-1",
            Operation = "Read",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(CallDenial.UntrustedCaller, decision.Denial);
    }

    [Fact]
    public async Task TheFirstStageDecisionWins()
    {
        RequestContext.Clear();
        var stamped = CallerContextStamper.Stamp(CallerContext(CallerKind.User, TrustedEdge.AuthenticatedHttp));
        var filter = new CallFilter([new FixedStage(CallDecision.Deny(CallDenial.MissingGrant, "denied")), new FixedStage(CallDecision.Allow())]);
        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = stamped,
            TargetNeuron = "neuron-1",
            Operation = "Read",
        }, TestContext.Current.CancellationToken);

        Assert.False(decision.Allowed);
        Assert.Equal(CallDenial.MissingGrant, decision.Denial);
    }

    [Fact]
    public void RequiringAnUnstampedContextFails()
    {
        RequestContext.Clear();
        static void Require() => _ = CallerContextStamper.Require();
        Assert.Throws<UntrustedCallerException>(Require);
    }

    private static CallerContext CallerContext(CallerKind kind, TrustedEdge edge) => new()
    {
        PrincipalId = "owner",
        AccountId = "account-1",
        WorkspaceId = "default",
        Kind = kind,
        StampedBy = edge,
    };

    private sealed class FixedStage(CallDecision decision) : ICallFilterStage
    {
        public ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CallDecision?>(decision);
    }
}
