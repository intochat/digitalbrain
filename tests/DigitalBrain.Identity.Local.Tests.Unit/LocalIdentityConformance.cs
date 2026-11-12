using System.Reflection;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Contracts;
using DigitalBrain.Identity.Local.Directory;
using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class LocalIdentityConformance : IdentityConformance<BoundIdentity>, IAsyncLifetime
{
    private const string PrivilegedModule = "privileged-module";

    private static readonly ModuleDeclarations Declarations = new([new ModuleManifest([], [], [], [], [])
    {
        Id = PrivilegedModule,
        Privileged =
        [
            new(Capability.Identity.V1, nameof(ISessions.Issue)),
            new(Capability.Identity.V1, nameof(ISessions.End)),
            new(Capability.Identity.V1, nameof(ISessions.Validate)),
        ],
    }]);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "db-dir-" + Guid.NewGuid().ToString("N"));
    private readonly AmbientCallerScope _callers = new();
    private SimpleServiceProvider? _provider;
    private TestOS? _os;
    private BoundIdentity? _identity;
    private BoundIdentity? _grants;
    private BoundSessions? _sessions;

    public ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        TestOS? os = null;
        services.AddLocalIdentity((contract, fullKey) => ResolveNeuron(os!, contract, fullKey), _directory);
        _provider = services.BuildServiceProvider();
        _os = os = TestOS.Local(options => options.Services = _provider);
        _os.Register<DirectoryNeuron>();
        _os.Register<SessionsNeuron>();

        var directoryPort = _provider.GetRequiredService<IIdentityDirectoryPort>();
        var sessionPort = _provider.GetRequiredService<IIdentitySessionPort>();
        _identity = new BoundIdentity(directoryPort, () => _callers.Current);
        _grants = new BoundIdentity(directoryPort, () => _callers.Current);
        _sessions = new BoundSessions(sessionPort, () => _callers.Current, Declarations.RequirePrivileged);
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

    protected override Task<IdentitySubject> Connect(CancellationToken ct)
        => Task.FromResult(new IdentitySubject(
            _identity!,
            _grants!,
            _callers,
            async (principalId, token) => (await _identity!.RegisterPrincipal(principalId, principalId, token).ConfigureAwait(false)).Member)
        {
            Sessions = _sessions,
            Description = _sessions,
            PrivilegedModule = PrivilegedModule,
        });

    internal static object ResolveNeuron(TestOS os, Type contract, string fullKey)
    {
        var separator = fullKey.IndexOf('/', StringComparison.Ordinal);
        var brain = fullKey[..separator];
        var key = fullKey[(separator + 1)..];
        var type = contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)!.Type;
        var id = new NeuronId(new BrainId(brain), type, key);
        if (contract == typeof(IDirectory))
        {
            return os.Get<IDirectory>(id);
        }

        if (contract == typeof(ISessionStore))
        {
            return os.Get<ISessionStore>(id);
        }

        throw new InvalidOperationException($"Unsupported neuron contract {contract.FullName}.");
    }
}
