using DigitalBrain.Kernel;
using DigitalBrain.Limits;
using DigitalBrain.Modules;
using DigitalBrain.Rules;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Policies;

[Module("charges.test")]
public sealed class ChargesTestModule : Module
{
    public static readonly ActionDescriptor Charges = new("charges.charge", "charges", "USD");
    public static readonly ActionDescriptor Drafts = new("gmail.draft.create", "create a mail draft", null);

    public static ModuleManifest Manifest { get; } = new([], [], [], [], [])
    {
        Actions = [Charges, Drafts],
    };
}

public sealed class PoliciesFacts
{
    private static readonly NeuronId Policies = NeuronId.Parse(IPolicies.Key("b"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OS Start(DateTimeOffset? start = null)
        => OS.Local(o => o.Start = start).Register(typeof(ChargesTestModule).Assembly);

    private static PolicyRule WeeklyThreeHundred(TimeProvider clock)
        => PolicyGrammar.Parse("r1", "charges.charge", "Sum", "Week", 300m, null, null, null, ChargesTestModule.Charges, clock);

    private static Task<PolicyRule> Adopt(OS os, PolicyRule rule) => os.AdoptPolicy("b", rule, Ct);

    [Fact]
    public async Task ProposeAsksThePersonAndAdoptsOnApproval()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = WeeklyThreeHundred(os.Clock);

        var outcome = await policies.Propose(rule, Ct);

        Assert.Equal(ProposalStatus.Pending, outcome.Status);
        Assert.Equal("Allow at most 300 USD of charges per week", outcome.Text);
        var waiting = Assert.Single(os.Confirmations.Waiting);
        Assert.Equal("Adopt a promise", waiting.Plan.Title);
        Assert.Equal("Allow at most 300 USD of charges per week", waiting.Plan.Body);
        Assert.True(waiting.Plan.RequiresPerson);

        await os.Confirmations.Approve(waiting.PlanId);
        await os.Idle();

        var adopted = Assert.Single(await policies.List(Ct));
        Assert.Equal("r1", adopted.Id);
        Assert.Equal("Allow at most 300 USD of charges per week", adopted.Text);
        Assert.Contains(await policies.Actions(Ct), a => a.Name == "charges.charge" && a.AmountUnit == "USD");
        Assert.Contains(os.Journal(Policies.Brain).Signals(Policies), s => s.Signal is PolicyProposed { RuleId: "r1" });
        Assert.Contains(os.Journal(Policies.Brain).Signals(Policies), s => s.Signal is PolicyAdopted { RuleId: "r1", Action: "charges.charge" });
    }

    [Fact]
    public async Task ADeniedProposalAdoptsNothing()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = WeeklyThreeHundred(os.Clock);

        var outcome = await policies.Propose(rule, Ct);
        await os.Confirmations.Deny(outcome.PlanId!);
        await os.Idle();

        Assert.Empty(os.Confirmations.Waiting);
        Assert.Empty(await policies.List(Ct));
        var decision = await policies.Allow("charges.charge", 500m, null, Ct);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task TheCapHoldsAcrossFourCharges()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var adopted = await Adopt(os, WeeklyThreeHundred(os.Clock));

        Assert.True((await policies.Allow("charges.charge", 120m, null, Ct)).Allowed);
        Assert.True((await policies.Allow("charges.charge", 100m, null, Ct)).Allowed);
        Assert.True((await policies.Allow("charges.charge", 70m, null, Ct)).Allowed);

        var fourth = await policies.Allow("charges.charge", 20m, null, Ct);
        Assert.False(fourth.Allowed);
        Assert.Equal("r1", fourth.RuleId);
        Assert.Equal(adopted.Text, fourth.Reason);

        var violation = Assert.Throws<RuleViolationException>(() => PolicyGuard.Require(fourth));
        Assert.Equal(adopted.Text, violation.Message);

        var refused = Assert.Single(os.Journal(Policies.Brain).Signals(Policies).Select(s => s.Signal).OfType<PolicyRefused>());
        Assert.Equal(new PolicyRefused("charges.charge", 20m, "r1"), refused);
    }

    [Fact]
    public async Task TwoConcurrentAllowsLetExactlyOneThrough()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = PolicyGrammar.Parse("r1", "charges.charge", "Sum", "Week", 100m, null, null, null, ChargesTestModule.Charges, os.Clock);
        _ = await Adopt(os, rule);

        var first = policies.Allow("charges.charge", 80m, null, Ct);
        var second = policies.Allow("charges.charge", 80m, null, Ct);
        var decisions = await Task.WhenAll(first, second);

        Assert.Equal(1, decisions.Count(d => d.Allowed));
        Assert.Equal(1, decisions.Count(d => !d.Allowed));
    }

    [Fact]
    public async Task ReleaseReturnsAReservation()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        _ = await Adopt(os, WeeklyThreeHundred(os.Clock));

        var first = await policies.Allow("charges.charge", 300m, null, Ct);
        Assert.True(first.Allowed);
        Assert.False((await policies.Allow("charges.charge", 1m, null, Ct)).Allowed);

        await policies.Release(first.DecisionId, Ct);

        var afterRelease = await policies.Allow("charges.charge", 300m, null, Ct);
        Assert.True(afterRelease.Allowed);
    }

    [Fact]
    public async Task TheWeekTurnsOverOnMondayUtc()
    {
        var sundayNight = new DateTimeOffset(2026, 11, 8, 23, 59, 0, TimeSpan.Zero); // Sunday 23:59 UTC
        await using var os = Start(sundayNight);
        var policies = os.Get<IPolicies>(Policies);
        _ = await Adopt(os, WeeklyThreeHundred(os.Clock));

        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);
        Assert.False((await policies.Allow("charges.charge", 10m, null, Ct)).Allowed);

        os.Clock.Advance(TimeSpan.FromMinutes(2)); // Monday 00:01 UTC

        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);
    }

    [Fact]
    public async Task ARollingWindowForgetsOldReservations()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = PolicyGrammar.Parse("r1", "charges.charge", "Sum", "Rolling:02:00:00", 100m, null, null, null, ChargesTestModule.Charges, os.Clock);
        _ = await Adopt(os, rule);

        Assert.True((await policies.Allow("charges.charge", 80m, null, Ct)).Allowed);

        os.Clock.Advance(TimeSpan.FromHours(1));
        Assert.False((await policies.Allow("charges.charge", 50m, null, Ct)).Allowed);

        os.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await policies.Allow("charges.charge", 50m, null, Ct)).Allowed);
    }

    [Fact]
    public async Task AConditionNarrowsTheRule()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = PolicyGrammar.Parse("r1", "gmail.draft.create", "Count", "Day", 1m, "To", "EndsWith", "@corp.com", ChargesTestModule.Drafts, os.Clock);
        _ = await Adopt(os, rule);

        Assert.True((await policies.Allow("gmail.draft.create", 1m, new Dictionary<string, string> { ["To"] = "alice@other.org" }, Ct)).Allowed);
        Assert.True((await policies.Allow("gmail.draft.create", 1m, new Dictionary<string, string> { ["To"] = "bob@other.org" }, Ct)).Allowed);

        Assert.True((await policies.Allow("gmail.draft.create", 1m, new Dictionary<string, string> { ["To"] = "boss@corp.com" }, Ct)).Allowed);
        var secondCorp = await policies.Allow("gmail.draft.create", 1m, new Dictionary<string, string> { ["To"] = "team@corp.com" }, Ct);
        Assert.False(secondCorp.Allowed);
        Assert.Equal("Allow at most 1 count of create a mail draft per day when To EndsWith @corp.com", secondCorp.Reason);
    }

    [Fact]
    public async Task RetireRemovesTheRuleAndItsReservations()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        _ = await Adopt(os, WeeklyThreeHundred(os.Clock));

        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);
        Assert.False((await policies.Allow("charges.charge", 10m, null, Ct)).Allowed);

        await policies.Retire("r1", Ct);

        Assert.Empty(await policies.List(Ct));
        Assert.Contains(os.Journal(Policies.Brain).Signals(Policies), s => s.Signal is PolicyRetired { RuleId: "r1" });
        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);
    }

    [Fact]
    public async Task ANegativeAmountIsRejectedBeforeItCanLowerTheTotal()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        _ = await Adopt(os, WeeklyThreeHundred(os.Clock));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => policies.Allow("charges.charge", -1000m, null, Ct));

        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);
        Assert.False((await policies.Allow("charges.charge", 1m, null, Ct)).Allowed);
    }

    [Fact]
    public async Task AMissingFactMakesAConditionalRuleApply()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = PolicyGrammar.Parse("r1", "gmail.draft.create", "Count", "Day", 1m, "To", "EndsWith", "@corp.com", ChargesTestModule.Drafts, os.Clock);
        _ = await Adopt(os, rule);

        Assert.True((await policies.Allow("gmail.draft.create", 1m, null, Ct)).Allowed);
        var second = await policies.Allow("gmail.draft.create", 1m, new Dictionary<string, string> { ["Subject"] = "hi" }, Ct);
        Assert.False(second.Allowed);
        Assert.Equal("r1", second.RuleId);
    }

    [Fact]
    public async Task ProposeRefusesAnUndeclaredActionWithoutAskingAnyone()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var rule = new PolicyRule("r1", "payments.send", PolicyMeasure.Count, new PolicyWindow(PolicyWindowKind.Day, null), 1m, null, "Send all the money", os.Clock.GetUtcNow());

        var outcome = await policies.Propose(rule, Ct);

        Assert.Equal(ProposalStatus.Refused, outcome.Status);
        Assert.Contains("payments.send", outcome.Reason);
        Assert.Equal(string.Empty, outcome.Text);
        Assert.Empty(os.Confirmations.Waiting);
        Assert.Empty(await policies.List(Ct));
    }

    [Fact]
    public async Task ProposeRefusesAMalformedRuleAndNeverShowsCallerText()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        var negative = new PolicyRule("r1", "charges.charge", PolicyMeasure.Sum, new PolicyWindow(PolicyWindowKind.Week, null), -5m, null, "Allow anything", os.Clock.GetUtcNow());
        var rolling = new PolicyRule("r2", "charges.charge", PolicyMeasure.Sum, new PolicyWindow(PolicyWindowKind.Rolling, null), 5m, null, "Allow anything", os.Clock.GetUtcNow());
        var blank = new PolicyRule(" ", "charges.charge", PolicyMeasure.Sum, new PolicyWindow(PolicyWindowKind.Week, null), 5m, null, "Allow anything", os.Clock.GetUtcNow());

        foreach (var rule in new[] { negative, rolling, blank })
        {
            var outcome = await policies.Propose(rule, Ct);
            Assert.Equal(ProposalStatus.Refused, outcome.Status);
            Assert.StartsWith("grammar:", outcome.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("Allow anything", outcome.Text, StringComparison.Ordinal);
        }

        Assert.Empty(os.Confirmations.Waiting);
    }

    [Fact]
    public async Task ReservationsOutsideTheirWindowArePrunedOnTheNextSave()
    {
        var sunday = new DateTimeOffset(2026, 11, 8, 23, 59, 0, TimeSpan.Zero);
        await using var os = Start(sunday);
        var policies = os.Get<IPolicies>(Policies);
        _ = await Adopt(os, WeeklyThreeHundredRow(os.Clock));
        var perCall = PolicyGrammar.Parse("r2", "gmail.draft.create", "Count", "None", 1m, null, null, null, ChargesTestModule.Drafts, os.Clock);
        _ = await Adopt(os, perCall);

        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);
        Assert.True((await policies.Allow("gmail.draft.create", 1m, null, Ct)).Allowed);
        Assert.Equal(1, await ReservationCount(os));

        os.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True((await policies.Allow("charges.charge", 300m, null, Ct)).Allowed);

        Assert.Equal(1, await ReservationCount(os));
        Assert.False((await policies.Allow("charges.charge", 1m, null, Ct)).Allowed);
    }

    [Fact]
    public async Task GuardRunsTheCallWhenAllowedAndReleasesWhenItThrows()
    {
        await using var os = Start();
        var policies = os.Get<IPolicies>(Policies);
        _ = await Adopt(os, PolicyGrammar.Parse("r1", "gmail.draft.create", "Count", "Day", 1m, null, null, null, ChargesTestModule.Drafts, os.Clock));

        await Assert.ThrowsAsync<InvalidOperationException>(() => policies.Guard("gmail.draft.create", 1m, null, _ => throw new InvalidOperationException("boom"), Ct));

        var ran = await policies.Guard("gmail.draft.create", 1m, null, _ => Task.FromResult(42), Ct);
        Assert.Equal(42, ran);

        var refused = await Assert.ThrowsAsync<RuleViolationException>(() => policies.Guard("gmail.draft.create", 1m, null, _ => Task.FromResult(0), Ct));
        Assert.Contains("create a mail draft", refused.Message, StringComparison.Ordinal);
    }

    private static PolicyRule WeeklyThreeHundredRow(TimeProvider clock) => WeeklyThreeHundred(clock);

    private static async Task<int> ReservationCount(OS os)
    {
        var state = await ((DigitalBrain.Kernel.IBrainObserver)os).State(Policies, Ct);
        return state is { } json && json.TryGetProperty("reservations", out var reservations) ? reservations.GetArrayLength() : -1;
    }
}
