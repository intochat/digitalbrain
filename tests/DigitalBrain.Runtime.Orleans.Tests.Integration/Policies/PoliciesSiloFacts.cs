using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Policies;

[Module("policies-silo-charges")]
public sealed class SiloChargesModule : Module
{
    public static readonly ActionDescriptor Charges = new("charges.charge", "charges", "USD");

    public static ModuleManifest Manifest { get; } = new([], [], [], [], [])
    {
        Actions = [Charges],
    };
}

public sealed class PoliciesSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PolicyRule WeeklyThreeHundred()
        => PolicyGrammar.Parse("r1", "charges.charge", "Sum", "Week", 300m, null, null, null, SiloChargesModule.Charges, TimeProvider.System);

    private async Task<PolicyRule> Adopt(string brain, PolicyRule rule)
    {
        var policies = silo.Get<IPolicies>(IPolicies.Key(brain));
        var outcome = await policies.Propose(rule, Ct);
        Assert.Equal(ProposalStatus.Pending, outcome.Status);
        await StateNeuronSiloFacts.Until(async () => (await policies.List(Ct)).Any(r => r.Id == rule.Id), Ct);
        return Assert.Single(await policies.List(Ct), r => r.Id == rule.Id);
    }

    [Fact]
    public async Task TheCapHoldsAcrossFourCharges()
    {
        const string brain = "pol-cap";
        var policies = silo.Get<IPolicies>(IPolicies.Key(brain));
        var adopted = await Adopt(brain, WeeklyThreeHundred());

        Assert.Contains(await policies.Actions(Ct), a => a.Name == "charges.charge" && a.AmountUnit == "USD");
        Assert.True((await policies.Allow("charges.charge", 120m, null, Ct)).Allowed);
        Assert.True((await policies.Allow("charges.charge", 100m, null, Ct)).Allowed);
        Assert.True((await policies.Allow("charges.charge", 70m, null, Ct)).Allowed);

        var fourth = await policies.Allow("charges.charge", 20m, null, Ct);
        Assert.False(fourth.Allowed);
        Assert.Equal("r1", fourth.RuleId);
        Assert.Equal(adopted.Text, fourth.Reason);

        var history = await silo.History(brain).Read(NeuronId.Parse(IPolicies.Key(brain)), 0, 20, Ct);
        var refused = Assert.Single(history.Signals.Select(s => s.Signal).OfType<PolicyRefused>());
        Assert.Equal(new PolicyRefused("charges.charge", 20m, "r1"), refused);
    }

    [Fact]
    public async Task RulesAndReservationsSurviveASiloRestart()
    {
        const string brain = "pol-restart";
        var policies = silo.Get<IPolicies>(IPolicies.Key(brain));
        _ = await Adopt(brain, WeeklyThreeHundred());

        Assert.True((await policies.Allow("charges.charge", 250m, null, Ct)).Allowed);

        await silo.Restart(Ct);

        var afterRestart = silo.Get<IPolicies>(IPolicies.Key(brain));
        var rules = await afterRestart.List(Ct);
        Assert.Single(rules, r => r.Id == "r1");

        var overCap = await afterRestart.Allow("charges.charge", 60m, null, Ct);
        Assert.False(overCap.Allowed);

        var withinCap = await afterRestart.Allow("charges.charge", 50m, null, Ct);
        Assert.True(withinCap.Allowed);
    }
}
