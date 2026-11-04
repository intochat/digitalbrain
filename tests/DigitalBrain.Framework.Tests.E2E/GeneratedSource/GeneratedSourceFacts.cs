using DigitalBrain.Framework.Tests.E2E.MorningStats;
using DigitalBrain.Rules;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Framework.Tests.E2E.GeneratedSource;

public interface IGsPayments
{
    Task Charge(decimal amount);
}

// Publishes the live generated Charging signal: its type is read from the catalog on each call, as the source declares it.
[Neuron("gs.payments")]
public sealed class GsPayments : Neuron<GsPayments.Count>, IGsPayments
{
    public Task Charge(decimal amount)
    {
        var source = (ISynapseCatalogSource)(Context.Services.GetService(typeof(ISynapseCatalogSource)) ?? throw new InvalidOperationException("no catalog"));
        var type = source.Current.For(WeeklyCapSource.Week).FirstOrDefault(d => d.Kind == SynapseKind.Gate)?.Signal
            ?? throw new InvalidOperationException("no gate is installed");
        return Save(State with { N = State.N + 1 }, (Signal)Activator.CreateInstance(type, amount)!);
    }

    public sealed record Count
    {
        public int N { get; init; }
    }
}

public sealed class GeneratedSourceFacts
{
    private sealed class FakeSql : ISqlRead
    {
        public Task<T> Scalar<T>(string sql, object? args, CancellationToken ct) => Task.FromResult((T)(object)1);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DbBindings =
        new Dictionary<string, IReadOnlyDictionary<string, string>> { [WeeklyCapSource.Week] = new Dictionary<string, string> { ["Db"] = "analytics" } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BrainId Brain = NeuronId.Parse("gs/x/y").Brain;

    private static OS Start() => OS.Local().Register<GsPayments>().With<ISqlRead>("analytics", new FakeSql());

    private static IGsPayments Pay(OS os) => os.Get<IGsPayments>(NeuronId.Parse("gs/gs.payments/p"));

    private static SynapseSource Of(string text) =>
        new(WeeklyCapSource.Key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);

    private static async Task<DeployOutcome> DeployApproved(OS os)
    {
        var outcome = await os.Synapses.Deploy(Brain, Of(WeeklyCapSource.Budget), None, Ct);
        Assert.Equal(DeployStatus.Deployed, outcome.Status);
        Assert.Equal(InstallStatus.Pending, outcome.Installs[WeeklyCapSource.Week].Status);
        await os.Confirmations.Approve(outcome.Installs[WeeklyCapSource.Week].Reason!);
        await os.Idle();
        return outcome;
    }

    [Fact]
    public async Task ThePromiseHoldsFromTextToRefusal()
    {
        await using var os = Start();
        await DeployApproved(os);
        var pay = Pay(os);

        await pay.Charge(120m);
        await pay.Charge(100m);
        await pay.Charge(70m);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(20m));
        await os.Idle();

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ANeedNeutralEditLandsWithoutAsking()
    {
        await using var os = Start();
        await DeployApproved(os);
        var asked = os.Confirmations.Requested.Count;

        var update = await os.Synapses.Deploy(Brain, Of(WeeklyCapSource.Cap200), None, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Deployed, update.Status);
        Assert.Equal(asked, os.Confirmations.Requested.Count);
        await Assert.ThrowsAsync<RuleViolationException>(() => Pay(os).Charge(250m));
        Assert.Empty(os.Synapses.Leaked);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ANewNeedAsksAgainWithTheNewSentence()
    {
        await using var os = Start();
        await DeployApproved(os);
        var asked = os.Confirmations.Requested.Count;

        var update = await os.Synapses.Deploy(Brain, Of(WeeklyCapSource.WithSlot), DbBindings, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Staged, update.Status);
        Assert.Equal(asked + 1, os.Confirmations.Requested.Count);
        var prompt = Assert.Single(os.Confirmations.Waiting);
        Assert.Contains("read-only SQL through Db", prompt.Plan.Body, StringComparison.Ordinal);

        await os.Confirmations.Approve(prompt.PlanId);
        await os.Idle();

        var listed = Assert.Single(await os.Synapses.List(Brain, Ct));
        Assert.Equal((DeployStatus.Deployed, update.Hash), (listed.Status, listed.Hash));
        Assert.NotEmpty(os.Catalog.NeedsOf(WeeklyCapSource.Week).Slots);
        Assert.Empty(os.Synapses.Leaked);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task RetireFreesEverything()
    {
        await using var os = Start();
        var first = await DeployApproved(os);
        await Assert.ThrowsAsync<RuleViolationException>(() => Pay(os).Charge(350m));
        await os.Idle();

        var retired = await os.Synapses.Retire(Brain, WeeklyCapSource.Key, Ct);
        await os.Idle();

        Assert.Equal((DeployStatus.Retired, first.Hash), (retired.Status, retired.Hash));
        Assert.False(os.Catalog.States.ContainsKey(WeeklyCapSource.Week));
        // No gate is left on the alias: the publisher finds no Charging signal to publish, so nothing refuses a charge.
        Assert.Empty(os.Catalog.For(WeeklyCapSource.Week));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pay(os).Charge(350m));
        Assert.Empty(os.Synapses.Leaked);
        Assert.Empty(os.Failures);
    }
}
