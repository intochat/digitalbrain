using DigitalBrain.Modules;
using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Contracts;
using DigitalBrain.Identity.Local.Vault;
using DigitalBrain.Modules.Configuration;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Identity.Local.Tests.Unit;

public sealed class LocalSecretsConformance : SecretsConformance<ISecrets>, IAsyncLifetime
{
    private const string PrivilegedModule = "kit-auth";

    private static readonly KitNamedSecret NamedSecret = new(PrivilegedModule, "kit-issuer:SigningKey", $"v-{Guid.NewGuid():N}");

    private static readonly ModuleDeclarations Declarations = new([new ModuleManifest([], [], [], [], [Secret.Named("kit-issuer")])
    {
        Id = PrivilegedModule,
    }]);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "db-vault-" + Guid.NewGuid().ToString("N"));
    private readonly AmbientCallerScope _callers = new();
    private readonly ConcurrentQueue<string> _logs = new();
    private SimpleServiceProvider? _provider;
    private TestOS? _os;
    private ISecrets? _secrets;

    public ValueTask InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ModuleConfiguration.ModuleSection(PrivilegedModule) + ":Secrets:" + NamedSecret.Name] = NamedSecret.Value,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        TestOS? os = null;
        services.AddLocalIdentity((contract, fullKey) => ResolveNeuron(os!, contract, fullKey), _directory);
        _provider = services.BuildServiceProvider();
        _os = os = TestOS.Local(options => options.Services = _provider);
        _os.Register<VaultNeuron>();
        _secrets = new PortSecrets(_provider.GetRequiredService<ISecretVaultPort>(), _callers, Declarations, _logs);
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

    protected override Task<SecretsSubject> Connect(CancellationToken ct)
        => Task.FromResult(new SecretsSubject(_secrets!, _callers, () => [.. _logs]) { Named = NamedSecret });

    [Fact(Timeout = Timeout)]
    public async Task SecretSurvivesRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = await Connect(ct);
        var name = $"kit-{Guid.NewGuid():N}";
        var value = $"v-{Guid.NewGuid():N}";
        SecretRef secret;
        using (s.Callers.As(Owner))
        {
            secret = await s.Secrets.Set(name, "Durable key", value, ct);
        }

        await _os!.Restart();

        using (s.Callers.As(Owner))
        {
            var resolved = await s.Secrets.Resolve(secret, ct);
            Assert.Equal(value, resolved);
        }
    }

    private static object ResolveNeuron(TestOS os, Type contract, string fullKey)
    {
        var separator = fullKey.IndexOf('/', StringComparison.Ordinal);
        var brain = fullKey[..separator];
        var key = fullKey[(separator + 1)..];
        var type = contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)!.Type;
        var id = new NeuronId(new BrainId(brain), type, key);
        if (contract == typeof(IVault))
        {
            return os.Get<IVault>(id);
        }

        throw new InvalidOperationException($"Unsupported neuron contract {contract.FullName}.");
    }

    private sealed class PortSecrets(
        ISecretVaultPort port,
        AmbientCallerScope callers,
        ModuleDeclarations declarations,
        ConcurrentQueue<string> logs) : ISecrets
    {
        public async Task<SecretRef> Set(string name, string label, string value, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(value);
            var caller = callers.Current;
            var result = await port.Set(caller, name, label ?? "", value, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case SecretVaultStatus.Ok when result.Secret is not null:
                    logs.Enqueue($"stored {result.Secret.Reference} for {caller.PrincipalId}");
                    return result.Secret;
                case SecretVaultStatus.Denied:
                    logs.Enqueue($"denied {name} to {caller.PrincipalId}");
                    throw new SecretDenied(name);
                default:
                    throw new InvalidOperationException($"secrets@1: the vault port answered {result} to set {name}.");
            }
        }

        public async Task Remove(string name, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var caller = callers.Current;
            var status = await port.Remove(caller, name, cancellationToken).ConfigureAwait(false);
            if (status == SecretVaultStatus.Denied)
            {
                logs.Enqueue($"denied {name} to {caller.PrincipalId}");
                throw new SecretDenied(name);
            }

            logs.Enqueue($"removed {name} for {caller.PrincipalId}");
        }

        public async Task<string> Resolve(SecretRef secret, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(secret);
            var caller = callers.Current;
            var name = NameOf(secret);
            if (secret.IsNamed)
            {
                RequireDeclared(caller, name = secret.Reference[SecretRef.NamedPrefix.Length..]);
            }

            var result = await port.Resolve(caller, secret, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case SecretVaultStatus.Ok when result.Value is not null:
                    logs.Enqueue($"resolved {secret.Reference} for {caller.PrincipalId}");
                    return result.Value;
                case SecretVaultStatus.Denied:
                    logs.Enqueue($"denied {name} to {caller.PrincipalId}");
                    throw new SecretDenied(name);
                default:
                    throw new SecretNotFound(name);
            }
        }

        public async Task<SealedSecret> Seal(string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(context);
            var caller = callers.Current;
            var sealedSecret = await port.Seal(caller, context, plaintext, cancellationToken).ConfigureAwait(false);
            if (sealedSecret is null)
            {
                logs.Enqueue($"denied {context} to {caller.PrincipalId}");
                throw new SecretDenied(context);
            }

            logs.Enqueue($"sealed {context} for {caller.PrincipalId}");
            return sealedSecret;
        }

        public async Task<byte[]> Open(string context, SealedSecret sealedSecret, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(context);
            ArgumentNullException.ThrowIfNull(sealedSecret);
            var caller = callers.Current;
            var plaintext = await port.Open(caller, context, sealedSecret, cancellationToken).ConfigureAwait(false);
            if (plaintext is null)
            {
                logs.Enqueue($"denied {context} to {caller.PrincipalId}");
                throw new SecretDenied(context);
            }

            logs.Enqueue($"opened {context} for {caller.PrincipalId}");
            return plaintext;
        }

        public async Task<SecretRef> Named(string name, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var caller = callers.Current;
            RequireDeclared(caller, name);
            var result = await port.Named(caller, name, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case SecretVaultStatus.Ok when result.Secret is not null:
                    return result.Secret;
                case SecretVaultStatus.NotFound:
                    return SecretRef.Unset(SecretRef.NamedPrefix + name, name);
                default:
                    logs.Enqueue($"denied {name} to {caller.PrincipalId}");
                    throw new SecretDenied(name);
            }
        }

        private void RequireDeclared(CapabilityCaller caller, string name)
        {
            if (declarations.DeclaresSecret(caller.AppId, name))
            {
                return;
            }

            logs.Enqueue($"denied {name} to {caller.PrincipalId}");
            throw new SecretDenied(name);
        }

        private static string NameOf(SecretRef secret)
        {
            var reference = secret.Reference ?? "";
            var slash = reference.LastIndexOf('/');
            var name = slash < 0 ? reference : reference[(slash + 1)..];
            return string.IsNullOrWhiteSpace(name) ? "(empty)" : name;
        }
    }
}
