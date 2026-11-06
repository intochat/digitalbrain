using DigitalBrain;
using DigitalBrain.Capabilities;
using DigitalBrain.Confirmation;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Confirmations;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public sealed class ConfirmationRuntimeFacts
{
    private const string Brain = "ada";

    [Fact]
    public async Task PersonApprovesWithinExpiry()
    {
        var (neuron, context) = await Start();
        await neuron.Begin(Plan("p-person"), "app.files");
        using (Person()) { await neuron.Resolve("p-person", true); }

        var result = await neuron.Result("p-person");
        Assert.Equal((ConfirmationOutcome.Approved, ConfirmationDenyReason.None), (result!.Outcome, result.Reason));
        Assert.Equal(PrincipalKind.Person, Audit(context).Approver!.Kind);
    }

    [Fact]
    public async Task AgentResolveIsSelfApproval()
    {
        var (neuron, context) = await Start();
        await neuron.Begin(Plan("p-agent"), "app.files");
        using (Stamp(CapabilityCallers.Of("agent", "ada", Brain, CallerKind.Agent))) { await neuron.Resolve("p-agent", true); }

        var result = await neuron.Result("p-agent");
        Assert.Equal((ConfirmationOutcome.Denied, ConfirmationDenyReason.SelfApproval), (result!.Outcome, result.Reason));
        Assert.Equal(PrincipalKind.Agent, Audit(context).Approver!.Kind);
    }

    [Fact]
    public async Task AppResolveIsSelfApproval()
    {
        var (neuron, context) = await Start();
        await neuron.Begin(Plan("p-app"), "app.files");
        using (Stamp(CapabilityCallers.Of("app.files", "ada", Brain, CallerKind.App, "app.files"))) { await neuron.Resolve("p-app", true); }

        var result = await neuron.Result("p-app");
        Assert.Equal(ConfirmationDenyReason.SelfApproval, result!.Reason);
        Assert.Equal(PrincipalKind.App, Audit(context).Approver!.Kind);
    }

    [Fact]
    public async Task UnstampedResolveIsRejected()
    {
        var (neuron, _) = await Start();
        await neuron.Begin(Plan("p-open"), "app.files");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Resolve("p-open", true));
        Assert.Null(await neuron.Result("p-open"));
    }

    [Fact]
    public async Task ExpiryWithoutResolve()
    {
        var (neuron, context) = await Start();
        await neuron.Begin(Plan("p-expire"), "app.files");

        await context.Timers.Advance(TimeSpan.FromMinutes(5));

        var result = await neuron.Result("p-expire");
        Assert.Equal((ConfirmationOutcome.Expired, ConfirmationDenyReason.Expired), (result!.Outcome, result.Reason));
        Assert.Null(Audit(context).Approver);
    }

    [Fact]
    public async Task PolicyNeverSkips()
    {
        var (neuron, context) = await Start(ConfirmationPolicy.Never);
        await neuron.Begin(Plan("p-never"), "app.files");

        var result = await neuron.Result("p-never");
        Assert.True(result!.Approved);
        Assert.True(result.Skipped);
        var audit = Audit(context);
        Assert.True(audit.Skipped);
        Assert.Null(audit.Approver);
        Assert.DoesNotContain("\\", audit.TargetLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestartKeepsThePendingPlan()
    {
        var (neuron, context) = await Start();
        await neuron.Begin(Plan("p-restart"), "app.files");
        await NeuronAttachment.Deactivate(neuron, DeactivationCause.Requested, TestContext.Current.CancellationToken);
        var carried = (await context.MemoryState.Load<ConfirmationState>())!;

        var again = new FakeNeuronContext(context.Id);
        await again.MemoryState.Save(carried, context.MemoryState.Signals);
        var restarted = new ConfirmationsNeuron(Options.Create(new ConfirmationOptions()));
        again.Timers.OnFire = name => NeuronAttachment.Remind(restarted, name, CancellationToken.None);
        NeuronAttachment.Attach(restarted, again);
        await NeuronAttachment.Activate(restarted, TestContext.Current.CancellationToken);

        Assert.Null(await restarted.Result("p-restart"));
        using (Person()) { await restarted.Resolve("p-restart", true); }
        Assert.Equal(ConfirmationOutcome.Approved, (await restarted.Result("p-restart"))!.Outcome);
    }

    [Fact]
    public async Task AppRelayIsSelfApproval()
    {
        var (neuron, context) = await Start();
        await neuron.Begin(Plan("p-relay"), "app.files");
        using (Stamp(CapabilityCallers.Of("app.files", "ada", Brain, CallerKind.App, "app.files")))
        {
            await neuron.ResolveAsRequester("p-relay", true);
        }

        var result = await neuron.Result("p-relay");
        Assert.Equal((ConfirmationOutcome.Denied, ConfirmationDenyReason.SelfApproval), (result!.Outcome, result.Reason));
        Assert.Equal(PrincipalKind.App, Audit(context).Approver!.Kind);
    }

    [Fact]
    public async Task ResolvedConfirmations_AreTrimmed_WhenExceedingLimit()
    {
        var (neuron, context) = await Start();
        using (Person())
        {
            for (var i = 1; i <= 60; i++)
            {
                var plan = new ConfirmationPlan($"p-{i}", ConfirmationAction.Delete, true, "Title", "Prompt", "A", "D", "t.txt");
                await neuron.Begin(plan, "app.files");
                await neuron.Resolve($"p-{i}", true);
            }
        }

        var state = (await context.MemoryState.Load<ConfirmationState>())!;
        Assert.True(state.Entries.Length <= 50, $"Expected at most 50 resolved entries, but found {state.Entries.Length}");
        Assert.Contains(state.Entries, e => e.Request.PlanId == "p-60");
        Assert.DoesNotContain(state.Entries, e => e.Request.PlanId == "p-1");
    }

    private static ConfirmationPlan Plan(string id)
        => new(id, ConfirmationAction.Delete, true, "Delete a.txt?", "This removes a.txt from Drive. You cannot undo this.", "Delete", "Keep", "a.txt");

    private static async Task<(ConfirmationsNeuron Neuron, FakeNeuronContext Context)> Start(ConfirmationPolicy policy = ConfirmationPolicy.DestructiveOnly)
    {
        var options = new ConfirmationOptions();
        if (policy != ConfirmationPolicy.DestructiveOnly) { options.Brains[Brain] = policy; }
        var neuron = new ConfirmationsNeuron(Options.Create(options));
        var context = new FakeNeuronContext(new NeuronId(new BrainId(Brain), "runtime.confirmations", IConfirmations.ConfirmationsNeuronKey));
        context.Timers.OnFire = name => NeuronAttachment.Remind(neuron, name, CancellationToken.None);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, TestContext.Current.CancellationToken);
        return (neuron, context);
    }

    private static IDisposable Person() => Stamp(CapabilityCallers.Of("ada", "ada", Brain));

    private static IDisposable Stamp(CapabilityCaller caller)
    {
        global::Orleans.Runtime.RequestContext.Set(HostNeuronContext.CallerKey, "");
        return RuntimeCallerContext.Stamp(caller);
    }

    private static ConfirmationResolved Audit(FakeNeuronContext context)
        => Assert.Single(context.Published.OfType<ConfirmationResolved>());
}
