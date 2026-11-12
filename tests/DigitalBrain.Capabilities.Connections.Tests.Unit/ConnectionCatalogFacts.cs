namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Connections.Catalog;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Requests;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using Xunit;

public sealed class ConnectionCatalogFacts
{
    [Fact]
    public void CatalogPutReturnsSameInstanceWhenIdentical()
    {
        var catalog = new ConnectionCatalog();
        var row = new CatalogRow("b/connection/demo/1", "demo", "1", ConnectionStatus.Connected, "alice@example.com", null);

        var updated = catalog.Put(row);
        Assert.Single(updated.Rows);

        // Put identical row returns same reference
        var same = updated.Put(row);
        Assert.Same(updated, same);

        // Put with change returns new reference
        var changed = updated.Put(row with { Status = ConnectionStatus.Expired });
        Assert.NotSame(updated, changed);
        Assert.Equal(ConnectionStatus.Expired, changed.RowOf("b/connection/demo/1")?.Status);
    }

    [Fact]
    public async Task CatalogNeuronProjectsLifecycleSignals()
    {
        var connectors = new[] { new ConnectorDefinition("demo", "Demo", ["api-key"], []) };
        var offers = new DefaultConnectorOffers(connectors);
        await using var os = TestOS.Local(options =>
        {
            var services = new DemoServices(connectors, offers);
            options.Services = services;
        });

        os.Register<SignalEmitterNeuron>();
        os.Register<ConnectionCatalogNeuron>();
        var brain = new BrainId("test-brain");
        var catalogNeuron = os.Get<IConnectionCatalog>(IConnectionCatalog.Id(brain.Value));
        var emitter = os.Get<ISignalEmitter>(new NeuronId(brain, "signal-emitter", "main"));

        // Activate catalog neuron
        await catalogNeuron.Read();

        // Publish ConnectionConnected
        await emitter.Connected("demo", "1", "api-key", "alice@example.com");
        await os.Idle();

        var catalog = await catalogNeuron.Read();
        Assert.Single(catalog.Rows);
        Assert.Equal(ConnectionStatus.Connected, catalog.Rows[0].Status);
        Assert.Equal("alice@example.com", catalog.Rows[0].Account);

        var initialRevision = catalog.Revision;

        // Identical ConnectionConnected does NOT bump revision
        await emitter.Connected("demo", "1", "api-key", "alice@example.com");
        await os.Idle();

        var catalogAfterIdentical = await catalogNeuron.Read();
        Assert.Equal(initialRevision, catalogAfterIdentical.Revision);

        // ConnectionDisconnected removes the row
        await emitter.Disconnected("demo", "1");
        await os.Idle();

        var catalogAfterDisconnect = await catalogNeuron.Read();
        Assert.Empty(catalogAfterDisconnect.Rows);
        Assert.True(catalogAfterDisconnect.Revision > initialRevision);
    }

    [Neuron("signal-emitter")]
    private sealed class SignalEmitterNeuron : Neuron, ISignalEmitter
    {
        public Task Connected(string connectorId, string connectionId, string authMethodId, string account)
            => Publish(new ConnectionConnected(connectorId, connectionId, authMethodId, account));

        public Task Disconnected(string connectorId, string connectionId)
            => Publish(new ConnectionDisconnected(connectorId, connectionId));
    }

    private interface ISignalEmitter
    {
        Task Connected(string connectorId, string connectionId, string authMethodId, string account);
        Task Disconnected(string connectorId, string connectionId);
    }

    private sealed class DemoServices(
        IReadOnlyList<ConnectorDefinition> connectors,
        IConnectorOffers offers) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IReadOnlyList<ConnectorDefinition>)) { return connectors; }
            if (serviceType == typeof(IConnectorOffers)) { return offers; }
            return null;
        }
    }
}
