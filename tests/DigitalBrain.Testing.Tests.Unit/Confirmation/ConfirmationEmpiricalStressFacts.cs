using System.Collections.Concurrent;
using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Limits;
using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using DigitalBrain.Rules;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Tests.Unit.Installs;
using DigitalBrain.Testing.Tests.Unit.Policies;
using DigitalBrain.Testing.Tests.Unit.Synapses;
using DigitalBrain.Units;
using Xunit;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Confirmation;

public sealed class ConfirmationEmpiricalStressFacts
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ConfirmationPrincipal Requester1 = ConfirmationPrincipal.App("app-worker-1");
    private static readonly ConfirmationPrincipal Requester2 = ConfirmationPrincipal.App("app-worker-2");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConfirmationPlan Plan(string id, TimeSpan? expiry = null, bool destructive = true) =>
        new(id, destructive ? ConfirmationAction.Delete : ConfirmationAction.Rename, destructive, "Title", "Body", "Yes", "No", "Target")
        {
            Expiry = expiry ?? TimeSpan.FromMinutes(2)
        };

    private static ConfirmationRequested[] Pending(ConfirmationState s) =>
        s.Entries.Where(e => e.Result is null).Select(e => e.Request).ToArray();

    private static async Task PublishSignal(OS os, BrainId brain, NeuronId publisher, Signal signal)
    {
        var journal = os.Journal(brain);
        var nudge = os.Delivery.NudgeFor(brain);
        var stamp = TestCause.Stamp();
        var commit = new Commit(publisher, null, [signal], null, new Stamp(stamp.TraceId, stamp.CausationId));
        var result = await journal.Append(commit, CancellationToken.None).ConfigureAwait(false);
        var lastPos = new Position(result.FirstPosition.Value);
        await nudge.Appended(publisher, result.Journaled(commit), lastPos, CancellationToken.None).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------------------------
    // 1. Stress-test 20 Pending Limit (Concurrent and Sequential)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task TwentyPendingLimit_Attempt25ConcurrentRequests_Exactly20AcceptedAnd5Rejected()
    {
        var clock = new TestClock();
        var confirmations = new TestConfirmations(clock) { Policy = ConfirmationPolicy.Always };

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exceptions = new ConcurrentBag<Exception>();
        var successes = new ConcurrentBag<string>();

        // Launch 25 concurrent requests simultaneously
        var tasks = Enumerable.Range(0, 25).Select(i => Task.Run(async () =>
        {
            await barrier.Task;
            var planId = $"cf-conc-{i:D2}";
            var plan = Plan(planId);
            try
            {
                var req = await confirmations.For(Requester1).Confirm(plan, Ct);
                successes.Add(planId);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }, Ct)).ToArray();

        // Release the barrier so all 25 hit concurrently
        barrier.SetResult();

        // In TestConfirmations, calls that are waiting do not complete until answered,
        // so give the tasks a short window to enter either Waiting or throw rejection.
        await Task.Delay(100, Ct);

        // Exactly 20 must be waiting
        Assert.Equal(20, confirmations.Waiting.Count);

        // Exactly 5 must have thrown InvalidOperationException
        Assert.Equal(5, exceptions.Count);
        foreach (var ex in exceptions)
        {
            var opEx = Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal("This brain already has 20 confirmations waiting.", opEx.Message);
        }

        // Now resolve 1 pending confirmation to free up capacity
        var firstWaiting = confirmations.Waiting[0].PlanId;
        await confirmations.Deny(firstWaiting);

        Assert.Equal(19, confirmations.Waiting.Count);

        // Now a 26th request must be successfully accepted
        var plan26 = Plan("cf-conc-26");
        var accepted = false;
        var task26 = Task.Run(async () =>
        {
            _ = confirmations.For(Requester1).Confirm(plan26, Ct);
            accepted = true;
        }, Ct);

        await Task.Delay(50, Ct);
        Assert.True(accepted);
        Assert.Equal(20, confirmations.Waiting.Count);
        Assert.Contains(confirmations.Waiting, w => w.PlanId == "cf-conc-26");
    }

    [Fact]
    public void TwentyPendingLimit_SequentialEnforcementOnPureState()
    {
        var state = new ConfirmationState();

        // 20 sequential requests succeed
        for (var i = 0; i < 20; i++)
        {
            var plan = Plan($"cf-seq-{i}");
            var transition = state.Request(plan, Requester1, $"caller-{i}", ConfirmationPolicy.Always, T0);
            Assert.NotNull(transition);
            state = transition.Value.Next;
        }

        Assert.Equal(20, Pending(state).Length);

        // 21st through 25th requests are strictly rejected
        for (var i = 20; i < 25; i++)
        {
            var plan = Plan($"cf-seq-{i}");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                state.Request(plan, Requester1, $"caller-{i}", ConfirmationPolicy.Always, T0));
            Assert.Equal("This brain already has 20 confirmations waiting.", ex.Message);
        }
    }

    [Fact]
    public async Task TwentyPendingLimit_OnConfirmationsNeuron()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.confirmations", "confirmations"));
        context.Caller = new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.Person("alice", "acc-1"), new BrainId("b1"));
        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        // 20 requests succeed
        for (var i = 0; i < 20; i++)
        {
            var plan = Plan($"cf-neuron-{i}");
            var res = await neuron.Request(plan);
            Assert.Null(res); // pending
        }

        var inbox = await neuron.Read();
        Assert.Equal(20, inbox.Pending.Length);

        // 21st request throws
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => neuron.Request(Plan("cf-neuron-21")));
        Assert.Equal("This brain already has 20 confirmations waiting.", ex.Message);
    }

    // -----------------------------------------------------------------------------------------
    // 2. Stress-test 128-char Max ID Length
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void PlanIdLength_128CharsAccepted_129CharsRejected()
    {
        var state = new ConfirmationState();

        // Exact boundary: 128 chars is accepted
        var id128 = new string('x', 128);
        var plan128 = Plan(id128);
        var t128 = state.Request(plan128, Requester1, "caller-1", ConfirmationPolicy.Always, T0);
        Assert.NotNull(t128);
        Assert.Single(Pending(t128.Value.Next));

        // 129 chars is rejected
        var id129 = new string('x', 129);
        var plan129 = Plan(id129);
        var ex = Assert.Throws<ArgumentException>(() =>
            state.Request(plan129, Requester1, "caller-1", ConfirmationPolicy.Always, T0));
        Assert.Contains("Invalid confirmation plan.", ex.Message);

        // Null, empty, and whitespace are rejected
        Assert.Throws<ArgumentException>(() =>
            state.Request(Plan(""), Requester1, "caller-1", ConfirmationPolicy.Always, T0));
        Assert.Throws<ArgumentException>(() =>
            state.Request(Plan("   "), Requester1, "caller-1", ConfirmationPolicy.Always, T0));
    }

    [Fact]
    public async Task PlanIdLength_OnConfirmationsNeuron()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.confirmations", "confirmations"));
        context.Caller = new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.Person("alice", "acc-1"), new BrainId("b1"));
        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var id128 = new string('z', 128);
        var res128 = await neuron.Request(Plan(id128));
        Assert.Null(res128);

        var id129 = new string('z', 129);
        await Assert.ThrowsAsync<ArgumentException>(() => neuron.Request(Plan(id129)));
    }

    // -----------------------------------------------------------------------------------------
    // 3. Stress-test 5-min Default Expiry Cap
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(60, 5)]     // 1 hour capped to 5 min
    [InlineData(600, 5)]    // 10 hours capped to 5 min
    [InlineData(1440, 5)]   // 24 hours capped to 5 min
    [InlineData(5, 5)]      // exact 5 min stays 5 min
    [InlineData(2, 2)]      // 2 min stays 2 min
    [InlineData(1, 1)]      // 1 min stays 1 min
    public void ExpiryCap_CappedAt5MinutesOrKeptIfShorter(int requestedMinutes, int expectedMinutes)
    {
        var state = new ConfirmationState();
        var plan = Plan($"cf-exp-{requestedMinutes}", expiry: TimeSpan.FromMinutes(requestedMinutes));

        var transition = state.Request(plan, Requester1, "caller-1", ConfirmationPolicy.Always, T0);
        Assert.NotNull(transition);

        var entry = Assert.Single(Pending(transition.Value.Next));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), entry.Plan.Expiry);
        Assert.Equal(T0.AddMinutes(expectedMinutes), entry.ExpiresAt);
    }

    // -----------------------------------------------------------------------------------------
    // 4. Stress-test Idempotent Re-Raise by Same Caller/Neuron (`RaisedBy`)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void IdempotentReRaise_SameCallerSucceeds_DifferentCallerRejected()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-idemp-test");

        // 1. Initial request by caller-A
        var t1 = state.Request(plan, Requester1, "neuron-caller-A", ConfirmationPolicy.Always, T0);
        Assert.NotNull(t1);
        state = t1.Value.Next;
        Assert.Equal(1, state.Revision);
        Assert.Single(state.Entries);

        // 2. Re-raise by same caller-A returns null (idempotent no-op)
        var t2 = state.Request(plan, Requester1, "neuron-caller-A", ConfirmationPolicy.Always, T0);
        Assert.Null(t2);
        Assert.Equal(1, state.Revision);
        Assert.Single(state.Entries);

        // 3. Request by DIFFERENT caller-B with the same plan ID throws InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
            state.Request(plan, Requester2, "neuron-caller-B", ConfirmationPolicy.Always, T0));
        Assert.Equal("This confirmation plan was already requested.", ex.Message);
    }

    [Fact]
    public async Task IdempotentReRaise_OnConfirmationsNeuron()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.confirmations", "confirmations"));
        context.Caller = new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.App("app-1", new DigitalBrain.Access.Actor.Person("alice", "acc-1")), new BrainId("b1"));
        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var plan = Plan("cf-neuron-idemp");

        // First request returns null (pending)
        var res1 = await neuron.Request(plan);
        Assert.Null(res1);

        // Second request by same caller returns null (idempotent, does not throw)
        var res2 = await neuron.Request(plan);
        Assert.Null(res2);

        var inbox = await neuron.Read();
        Assert.Single(inbox.Pending); // exactly 1 pending entry, zero duplicate spam
    }

    // -----------------------------------------------------------------------------------------
    // 5. Stress-test Forged Signals: PoliciesNeuron and InstallsNeuron Refuse to Act
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ForgedSignals_PoliciesNeuronRefusesToActWithoutAuthoritativeApproval()
    {
        await using var os = OS.Local().Register(typeof(ChargesTestModule).Assembly);
        var policiesId = NeuronId.Parse(IPolicies.Key("b"));
        var policies = os.Get<IPolicies>(policiesId);

        var rule = PolicyGrammar.Parse("r-forged", "charges.charge", "Sum", "Week", 300m, null, null, null, ChargesTestModule.Charges, os.Clock);

        // 1. Propose policy -> returns Pending confirmation plan
        var proposal = await policies.Propose(rule, Ct);
        Assert.Equal(ProposalStatus.Pending, proposal.Status);
        var planId = proposal.PlanId!;
        Assert.Single(os.Confirmations.Waiting, w => w.PlanId == planId);

        // 2. An attacker emits a forged/synthetic ConfirmationResolved with Approved outcome
        var forged = new ConfirmationResolved(
            planId,
            ConfirmationAction.Other,
            "forged-target",
            ConfirmationPrincipal.App("attacker-neuron"),
            ConfirmationPrincipal.Person("mallory"),
            ConfirmationPolicy.Always,
            ConfirmationOutcome.Approved,
            ConfirmationDenyReason.None,
            false,
            os.Clock.GetUtcNow());

        // Publish forged signal to the brain
        var attackerNeuron = new NeuronId(new BrainId("b"), "hostile.attacker", "attacker-1");
        await PublishSignal(os, new BrainId("b"), attackerNeuron, forged);
        await os.Idle();

        // 3. Verify PoliciesNeuron refused to adopt because Outcome(planId) was authoritative (not approved by ConfirmationsNeuron)
        var adopted = await policies.List(Ct);
        Assert.Empty(adopted);
        Assert.DoesNotContain(os.Journal(policiesId.Brain).Signals(policiesId), s => s.Signal is PolicyAdopted);

        // 4. Now perform legitimate human approval through ConfirmationsNeuron
        await os.Confirmations.Approve(planId);
        await os.Idle();

        // 5. Verify policy is now authoritatively adopted
        var legitimatelyAdopted = await policies.List(Ct);
        var adoptedRule = Assert.Single(legitimatelyAdopted);
        Assert.Equal("r-forged", adoptedRule.Id);
        Assert.Contains(os.Journal(policiesId.Brain).Signals(policiesId), s => s.Signal is PolicyAdopted { RuleId: "r-forged" });
    }

    [Fact]
    public async Task ForgedSignals_InstallsNeuronRefusesToActWithoutAuthoritativeApproval()
    {
        var hooks = new RecordingHooks();
        await using var os = OS.Local(o => o.Services = hooks).Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        var installsId = NeuronId.Parse("b/synapse.installs/default");
        var installs = os.Get<IInstalls>(installsId);

        // 1. Stage an install request -> returns Pending with confirmation plan id
        var outcome = await installs.Install(new InstallRequest("week", new Dictionary<string, string>()), Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Status);
        var planId = outcome.Reason!;
        Assert.Single(os.Confirmations.Waiting, w => w.PlanId == planId);

        // 2. Attacker publishes a forged ConfirmationResolved with Approved outcome
        var forged = new ConfirmationResolved(
            planId,
            ConfirmationAction.Other,
            "forged-install",
            ConfirmationPrincipal.App("attacker-neuron"),
            ConfirmationPrincipal.Person("mallory"),
            ConfirmationPolicy.Always,
            ConfirmationOutcome.Approved,
            ConfirmationDenyReason.None,
            false,
            os.Clock.GetUtcNow());

        var attackerNeuron = new NeuronId(new BrainId("b"), "hostile.attacker", "attacker-2");
        await PublishSignal(os, new BrainId("b"), attackerNeuron, forged);
        await os.Idle();

        // 3. Verify InstallsNeuron refused to install
        Assert.Null(await installs.Describe("week", Ct));
        Assert.Empty(hooks.Calls);
        Assert.DoesNotContain(os.Journal(installsId.Brain).Signals(installsId), s => s.Signal is Installed);

        // 4. Legitimate approval via ConfirmationsNeuron
        await os.Confirmations.Approve(planId);
        await os.Idle();

        // 5. Verify authoritative installation succeeds
        var record = await installs.Describe("week", Ct);
        Assert.NotNull(record);
        Assert.Contains(os.Journal(installsId.Brain).Signals(installsId), s => s.Signal is Installed { Alias: "week" });
    }
}
