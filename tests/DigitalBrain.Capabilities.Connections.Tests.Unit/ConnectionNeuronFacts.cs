using DigitalBrain.Neurons;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Testing;

namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

public sealed class ConnectionNeuronFacts
{
    [Fact]
    public async Task ConnectThenReadIsConnected()
    {
        await using var os = CreateTestOS();
        var key = ConnectionKeys.For("b", "demo", "1");
        var connection = os.Get<IConnection>(ConnectionKeys.IdFor(key));

        using var _ = ConnectionNeuron<DemoConnectionState>.StampCaller(new CapabilityCaller("ada", "ada", "b"));
        var connected = await connection.Connect("api-key", "secret-token-value");
        var read = await connection.Read();

        Assert.Equal(ConnectionStatus.Connected, connected.Status);
        Assert.Equal(ConnectionStatus.Connected, read.Status);
        Assert.Equal("api-key", read.AuthMethodId);
        Assert.Equal("demo", read.ConnectorId);
        Assert.Equal("1", read.ConnectionId);
    }

    [Fact]
    public async Task DisconnectThenReadIsDisconnected()
    {
        await using var os = CreateTestOS();
        var key = ConnectionKeys.For("b", "demo", "1");
        var connection = os.Get<IConnection>(ConnectionKeys.IdFor(key));

        using var _ = ConnectionNeuron<DemoConnectionState>.StampCaller(new CapabilityCaller("ada", "ada", "b"));
        await connection.Connect("api-key", "secret-token-value");
        await connection.Disconnect();
        var read = await connection.Read();

        Assert.Equal(ConnectionStatus.Disconnected, read.Status);
    }

    [Fact]
    public async Task ShareIntoThenIsSharedInto()
    {
        await using var os = CreateTestOS();
        var key = ConnectionKeys.For("b", "demo", "1");
        var connection = os.Get<IConnection>(ConnectionKeys.IdFor(key));

        Assert.False(await connection.IsSharedInto("circle-1", "app-a"));
        await connection.ShareInto("circle-1", "app-a");
        Assert.True(await connection.IsSharedInto("circle-1", "app-a"));
        await connection.UnshareFrom("circle-1", "app-a");
        Assert.False(await connection.IsSharedInto("circle-1", "app-a"));
    }

    private static TestOS CreateTestOS()
    {
        var services = new DemoServices(
            new FakeSecretSealer(),
            [new ConnectorDefinition("demo", "Demo", ["api-key"], [])]);
        var os = TestOS.Local(options => options.Services = services);
        os.Register<DemoConnectionNeuron>();
        os.Register<StubConnectionIndexNeuron>();
        os.Register<StubConnectionCatalogUpdatesNeuron>();
        return os;
    }

    private sealed class DemoConnectionState : ConnectionState;

    [Neuron("demo")]
    private sealed class DemoConnectionNeuron : ConnectionNeuron<DemoConnectionState>
    {
        protected override Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct)
            => Task.FromResult(ConnectionProbeResult.Connected());
    }

    [Neuron(IConnectionIndex.TypeName)]
    private sealed class StubConnectionIndexNeuron : Neuron, IConnectionIndex
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<string[]> List() => Task.FromResult(_keys.ToArray());

        public Task Track(string connectionKey)
        {
            _keys.Add(connectionKey);
            return Task.CompletedTask;
        }

        public Task Untrack(string connectionKey)
        {
            _keys.Remove(connectionKey);
            return Task.CompletedTask;
        }
    }

    [Neuron(IConnectionCatalogUpdates.TypeName)]
    private sealed class StubConnectionCatalogUpdatesNeuron : Neuron, IConnectionCatalogUpdates
    {
        public Task Changed(string connectionKey) => Task.CompletedTask;
    }

    private sealed class FakeSecretSealer : ISecretSealer
    {
        public SealedSecret Seal(string name, string plaintext) => new() { KeyId = "mk-test", Cipher = $"sealed:{name}:{plaintext}" };

        public string Open(SealedSecret secret)
        {
            const string prefix = "sealed:";
            if (secret.Cipher.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = secret.Cipher[prefix.Length..];
                var colon = rest.IndexOf(':', StringComparison.Ordinal);
                if (colon >= 0) { return rest[(colon + 1)..]; }
            }
            return secret.Cipher;
        }
    }

    private sealed class DemoServices(ISecretSealer sealer, IReadOnlyList<ConnectorDefinition> connectors) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ISecretSealer)) { return sealer; }
            if (serviceType == typeof(IReadOnlyList<ConnectorDefinition>)) { return connectors; }
            if (serviceType == typeof(TimeProvider)) { return TimeProvider.System; }
            return null;
        }
    }
}
