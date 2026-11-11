using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Requests;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Contracts;
using DigitalBrain.Identity.Local.Connections;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class LocalConnectionsConformance : ConnectionsConformance<LocalConnections>, IAsyncLifetime
{
    private const string ConnectorId = "kit-connector";

    private static readonly ConnectorDefinition KitConnector = new(
        ConnectorId,
        "Kit Connector",
        ["token"],
        []);

    private static readonly ConnectionNeuronTypes NeuronTypes = new([typeof(KitConnectorModule)]);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "db-conn-" + Guid.NewGuid().ToString("N"));
    private readonly AmbientCallerScope _callers = new();
    private SimpleServiceProvider? _provider;
    private TestOS? _os;
    private ICapabilityConnections? _connections;

    public ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEnumerable<ConnectorDefinition>>([KitConnector]);
        services.AddSingleton<IReadOnlyList<ConnectorDefinition>>([KitConnector]);
        services.AddSingleton(NeuronTypes);

        TestOS? os = null;
        services.AddLocalIdentity((contract, fullKey) => ResolveNeuron(os!, contract, fullKey), _directory);
        _provider = services.BuildServiceProvider();
        _os = os = TestOS.Local(options => options.Services = _provider);
        _os.Register<ConnectionIndexNeuron>();
        _os.Register<ConnectionRequestsNeuron>();
        _connections = _provider.GetRequiredService<ICapabilityConnections>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_os is not null)
        {
            await _os.DisposeAsync();
        }

        if (System.IO.Directory.Exists(_directory))
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
    }

    protected override Task<ConnectionsSubject> Connect(CancellationToken ct)
    {
        var brain = new BrainId($"kit-brain-{Guid.NewGuid():N}");
        var requests = _os!.Get<IConnectionRequests>(IConnectionCatalogUpdates.Id(brain.Value));
        var index = _os.Get<IConnectionIndex>(IConnectionIndex.Id(brain.Value));
        return Task.FromResult(new ConnectionsSubject(_connections!, requests, _callers)
        {
            Brain = brain,
            ConnectorKind = ConnectorId,
            Capability = Capability.Storage.V1,
            Index = index,
            RequestedSignals = _ => Task.FromResult<IReadOnlyList<ConnectionRequested>>(
                [.. _os.Journal(brain).Signals(IConnectionCatalogUpdates.Id(brain.Value))
                    .Select(entry => entry.Signal)
                    .OfType<ConnectionRequested>()]),
        });
    }

    private static object ResolveNeuron(TestOS os, Type contract, string fullKey)
    {
        if (ConnectionKeys.Is(fullKey))
        {
            var connectionId = ConnectionKeys.IdFor(fullKey);
            if (contract == typeof(IStorage))
            {
                return os.Get<IStorage>(connectionId);
            }

            if (contract == typeof(IConnection))
            {
                return os.Get<IConnection>(connectionId);
            }
        }

        var separator = fullKey.IndexOf('/', StringComparison.Ordinal);
        var brain = fullKey[..separator];
        var key = fullKey[(separator + 1)..];
        var type = contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)!.Type;
        var id = new NeuronId(new BrainId(brain), type, key);
        if (contract == typeof(IConnectionRequests))
        {
            return os.Get<IConnectionRequests>(id);
        }

        if (contract == typeof(IConnectionIndex))
        {
            return os.Get<IConnectionIndex>(id);
        }

        throw new InvalidOperationException($"Unsupported neuron contract {contract.FullName}.");
    }

    [Module("kit-connector-module")]
    private sealed class KitConnectorModule : DigitalBrain.Modules.Module
    {
        public static ModuleManifest Manifest { get; } = new([Capability.Storage.V1], [], [], [], [])
        {
            Id = "kit-connector-module",
        };
    }

    [Neuron(ConnectorId)]
    private sealed class KitConnectorNeuron : ConnectionNeuron<ConnectionState>
    {
        protected override Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct)
            => Task.FromResult(ConnectionProbeResult.Connected());
    }
}
