using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class StateReaderFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "ledger", "default");

    [Fact]
    public async Task ReadReturnsTheStateRecordAsJson()
    {
        var context = new RecordingContext(Id);
        var neuron = new StateNeuron<Ledger>(new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Ledger), typeof(Books)])), new UnboundSlotResolver());
        NeuronAttachment.Attach(neuron, context);
        await neuron.Receive(new Charging(5m), TestContext.Current.CancellationToken);
        IStateReader reader = neuron;

        var record = await reader.Read("ledger");
        var other = await reader.Read("week");

        Assert.Equal(5m, record.GetProperty("spent").GetDecimal());
        Assert.Equal(JsonValueKind.Null, other.ValueKind);
        Assert.Equal("synapse.state", typeof(IStateReader).GetCustomAttribute<NeuronAliasAttribute>()!.Alias);
        Assert.NotNull(typeof(IStateReader).GetMethod(nameof(IStateReader.Read))!.GetCustomAttribute<ReadOnlyCallAttribute>());
    }
}
