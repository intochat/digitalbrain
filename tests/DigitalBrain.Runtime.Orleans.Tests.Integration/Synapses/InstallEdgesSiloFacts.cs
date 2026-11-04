using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

using SynapseSource = DigitalBrain.Runtime.Orleans.Synapses.SynapseSource;

// The InstallEdgesFacts of TestOS on the silo: reaction edges come from the install, brain-wide; gates only for installed aliases.
// Each fact has its own brain.
public sealed class InstallEdgesSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<decimal> Spent(NeuronId id)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id.ToString()));
        var state = await host.InspectState(Ct);
        return state is { } json && json.TryGetProperty("spent", out var spent) ? spent.GetDecimal() : 0m;
    }

    [Fact]
    public async Task ARecordedInstallWiresReactionsFromEveryPublisher()
    {
        var books = NeuronId.Parse("iea/sb.ledger/default");
        await StateNeuronSiloFacts.Install(silo.Services, "iea", "sb.ledger");

        await silo.Get<ISbPayments>("iea/sb.payments/one").Charge(5m);
        await silo.Get<ISbPayments>("iea/sb.payments/two").Charge(7m);
        await StateNeuronSiloFacts.Until(async () => await Spent(books) == 12m, Ct);

        Assert.Equal(12m, await Spent(books));
        Assert.Equal(2, (await silo.History("iea").Read(books, 0, 10, Ct)).Signals.Count(s => s.Signal is StateChanged));
    }

    [Fact]
    public async Task AnInstallReplacesPerSourceEdgesWithTheBrainWideOne()
    {
        var books = NeuronId.Parse("iee/sb.ledger/default");
        var grain = GrainId.Create(NeuronTypeRegistry.AffineGrainType, books.ToString());
        // Plan-1 data: a per-source entry on the state neuron, written before it ever activates.
        var store = silo.Services.GetRequiredKeyedService<IGrainStorage>(RuntimeOptions.Default.ClusterGrainStorage);
        await store.WriteStateAsync(SynapseState.Name, grain, new DigitalBrain.Runtime.Orleans.Tests.Integration.Storage.TestGrainState<SynapseState>
        {
            State = new SynapseState { Sources = [new SynapseSource { Source = "iee/sb.payments/one", Aliases = ["test.sb.charging"], Cursor = 0 }] },
        });

        await StateNeuronSiloFacts.Install(silo.Services, "iee", "sb.ledger");
        await silo.Get<ISbPayments>("iee/sb.payments/one").Charge(5m);
        await silo.Get<ISbPayments>("iee/sb.payments/two").Charge(7m);
        await StateNeuronSiloFacts.Until(async () => await Spent(books) >= 12m, Ct);
        await Task.Delay(500, Ct);

        Assert.Equal(12m, await Spent(books));
        Assert.Equal(2, (await silo.History("iee").Read(books, 0, 10, Ct)).Signals.Count(s => s.Signal is StateChanged));
        var record = await (await silo.Services.GetRequiredService<BrainLogs>().For("iee")).Read("neuron.host/" + books, SynapseState.Name);
        Assert.NotNull(record);
        var entry = Assert.Single(JournalPayloads.DecodeState<SynapseState>(record.Record)!.Sources);
        Assert.Equal("*", entry.Source);
    }

    [Fact]
    public async Task AnUninstalledStateReceivesNothing()
    {
        var books = NeuronId.Parse("ieb/sb.ledger/default");

        await silo.Get<ISbPayments>("ieb/sb.payments/one").Charge(5m);
        await Task.Delay(500, Ct);

        Assert.Equal(0m, await Spent(books));
        Assert.Empty((await silo.History("ieb").Read(books, 0, 10, Ct)).Signals);
    }

    [Fact]
    public async Task RetireStopsDelivery()
    {
        var books = NeuronId.Parse("iec/sb.ledger/default");
        await StateNeuronSiloFacts.Install(silo.Services, "iec", "sb.ledger");
        await silo.Get<ISbPayments>("iec/sb.payments/one").Charge(5m);
        await StateNeuronSiloFacts.Until(async () => await Spent(books) == 5m, Ct);

        await NeuronHosting.Get<IInstalls>(silo.Services, NeuronId.Parse("iec/synapse.installs/default")).Retire("sb.ledger", Ct);
        await silo.Get<ISbPayments>("iec/sb.payments/two").Charge(7m);
        await Task.Delay(500, Ct);

        Assert.Equal(5m, await Spent(books));
    }

    [Fact]
    public async Task AGateInstalledLaterIsFoundOnTheNextPublish()
    {
        var pay = silo.Get<ISbPayments>("ied/sb.payments/one");
        await pay.Charge(400m);

        await StateNeuronSiloFacts.Install(silo.Services, "ied", "sb.week");
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(400m));

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
    }
}
