namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

using DigitalBrain.Neurons;
using DigitalBrain.Capabilities.Connections.Catalog;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Testing;
using Xunit;

public sealed class AFailedSaveLeavesTheConnectionUntouched
{
    [Fact]
    public async Task WhenSaveThrowsInMemoryStateRemainsIntact()
    {
        var services = new DemoServices(
            new FakeSecretSealer(),
            [new ConnectorDefinition("faulty", "Faulty", ["api-key"], [])]);
        await using var os = TestOS.Local(options => options.Services = services);
        os.Register<ConnectionCatalogNeuron>();
        os.Register<FaultyConnectionNeuron>();

        var key = ConnectionKeys.For("b", "faulty", "1");
        var connection = os.Get<IConnection>(ConnectionKeys.IdFor(key));

        using var _ = ConnectionNeuron<DemoConnectionState>.StampCaller(new CapabilityCaller("ada", "ada", "b"));

        // First connect succeeds
        FaultyConnectionNeuron.FailSave = false;
        var connected = await connection.Connect("api-key", "secret-value");
        Assert.Equal(ConnectionStatus.Connected, connected.Status);

        // Disconnect fails during save
        FaultyConnectionNeuron.FailSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Disconnect());

        // State remains untouched (still Connected with credentials intact)
        var read = await connection.Read();
        Assert.Equal(ConnectionStatus.Connected, read.Status);
        Assert.Equal("api-key", read.AuthMethodId);

        // When save succeeds again, disconnect finishes cleanly
        FaultyConnectionNeuron.FailSave = false;
        await connection.Disconnect();
        var readDisconnected = await connection.Read();
        Assert.Equal(ConnectionStatus.Disconnected, readDisconnected.Status);
    }

    private sealed record DemoConnectionState : ConnectionState;

    [Neuron("faulty")]
    private sealed class FaultyConnectionNeuron : ConnectionNeuron<DemoConnectionState>
    {
        public static bool FailSave { get; set; }

        protected override Task Record(DemoConnectionState next, Signal fact)
        {
            if (FailSave)
            {
                throw new InvalidOperationException("Simulated journal write failure.");
            }

            return base.Record(next, fact);
        }

        protected override Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct)
            => Task.FromResult(ConnectionProbeResult.Connected());
    }

    private sealed class FakeSecretSealer : ISecretSealer
    {
        public SealedSecret Seal(string name, string plaintext) => new() { KeyId = "mk-test", Cipher = $"sealed:{name}:{plaintext}" };

        public string Open(SealedSecret secret) => secret.Cipher;
    }

    private sealed class DemoServices(ISecretSealer sealer, IReadOnlyList<ConnectorDefinition> connectors) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ISecretSealer)) { return sealer; }
            if (serviceType == typeof(IReadOnlyList<ConnectorDefinition>)) { return connectors; }
            return null;
        }
    }
}
