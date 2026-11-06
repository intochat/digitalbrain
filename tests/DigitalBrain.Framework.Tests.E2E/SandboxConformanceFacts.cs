using System.Globalization;
using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using DigitalBrain.Access;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Client;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Modules.Configuration;
using DigitalBrain.Runtime.Http;
using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Azure;
using DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;
using DigitalBrain.Runtime.Orleans.Brain;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Enforcement;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using DigitalBrain.Testing.Conformance.Host;
using DigitalBrain.Testing.Conformance.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Framework.Tests.E2E;

[DefaultNeuronType("sandbox.vault-guard")]
public interface ISandboxVaultGuardNeuron
{
    Task<string> GetSecretLabelAsync(string secretName);

    Task<string> UnsafeExposeSecretPlaintextAsync(string secretName);
}

[DefaultNeuronType("sandbox.gated")]
public interface ISandboxGatedNeuron
{
    Task<string> AllowedCapabilityAsync(string input);

    [Internal]
    Task<string> UngrantedCapabilityAsync(string input);
}

[DigitalBrain.Neuron("sandbox.vault-guard", State = "vault-state")]
public sealed class SandboxVaultGuardNeuron : DigitalBrain.Neuron<SandboxVaultGuardState>, ISandboxVaultGuardNeuron
{
    public async Task<string> GetSecretLabelAsync(string secretName)
    {
        var secrets = Context.Services.GetRequiredService<ISecrets>();
        var secretRef = await secrets.Named(secretName).ConfigureAwait(false);
        return secretRef.Label;
    }

    public async Task<string> UnsafeExposeSecretPlaintextAsync(string secretName)
    {
        var secrets = Context.Services.GetRequiredService<ISecrets>();
        var secretRef = await secrets.Named(secretName).ConfigureAwait(false);
        return await secrets.Resolve(secretRef).ConfigureAwait(false);
    }
}

[GenerateSerializer]
public sealed class SandboxVaultGuardState
{
    [Id(0)]
    public string LastAccessedSecret { get; set; } = string.Empty;
}

[DigitalBrain.Neuron("sandbox.gated", State = "gated-state")]
public sealed class SandboxGatedNeuron : DigitalBrain.Neuron<SandboxGatedState>, ISandboxGatedNeuron
{
    public Task<string> AllowedCapabilityAsync(string input)
    {
        State.Counter++;
        return Task.FromResult($"Allowed: {input} (call #{State.Counter})");
    }

    public Task<string> UngrantedCapabilityAsync(string input)
    {
        return Task.FromResult($"Ungranted: {input}");
    }
}

[GenerateSerializer]
public sealed class SandboxGatedState
{
    [Id(0)]
    public int Counter { get; set; }
}

[Module(ModuleId)]
public sealed class SandboxConformanceModule : Module
{
    public const string ModuleId = "sandbox-conformance-module";

    public static ModuleManifest Manifest { get; } = new(
        Provides: [],
        Requires: [],
        Needs:
        [
            Need.State("vault-state"),
            Need.State("gated-state"),
        ],
        Settings: [],
        Secrets: [Secret.Named("db-token")]);
}

public sealed class SandboxSiloInvocationDispatcher(IServiceProvider siloServices) : IRuntimeInvocationDispatcher
{
    public async Task<JsonElement?> DispatchAsync(string brainId, RuntimeInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentNullException.ThrowIfNull(invocation);

        var contract = PlainNeuronCallFilter.ContractOf(invocation.Contract)
            ?? throw new NotSupportedException($"Contract '{invocation.Contract}' could not be resolved.");

        var fullKey = $"{brainId}/{invocation.Key}";
        var neuron = SiloNeurons.Get(siloServices, contract, fullKey);

        var binder = new NeuronBinder();
        var bound = binder.Bind(contract, invocation);

        using var stamp = RuntimeCallerContext.Stamp(CapabilityCallers.Of(brainId, brainId, brainId, CallerKind.App, appId: SandboxConformanceModule.ModuleId));
        return await binder.InvokeAsync(neuron, bound, invocation.Arguments, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SandboxConformanceFacts(AzuriteFixture azurite)
{
    private static readonly byte[] EdgeSecretKey = "sandbox-conformance-edge-key-32b"u8.ToArray();

    public static TheoryData<string> TopologyTargets => new(LocalOrleansTarget.TargetName, AzuriteOrleansTarget.TargetName);

    private sealed class SandboxedTopologyEnvironment : IAsyncDisposable
    {
        private readonly string _target;
        private readonly AzuriteFixture _azurite;
        private readonly string _home;
        private readonly string _clusterId;
        private readonly InMemorySecretVault _secretVault = new();

        private PortLease? _siloLease;
        private IHost? _siloHost;
        private WebApplication? _httpEdgeApp;

        public Uri EdgeBaseUri { get; private set; } = null!;
        public InMemorySecretVault SecretVault => _secretVault;
        public IServiceProvider SiloServices => (_siloHost ?? throw new ObjectDisposedException(nameof(SandboxedTopologyEnvironment))).Services;

        public SandboxedTopologyEnvironment(string target, AzuriteFixture azurite)
        {
            _target = target;
            _azurite = azurite;
            _home = Path.Combine(Path.GetTempPath(), "sandbox-conformance-" + Guid.NewGuid().ToString("N"));
            _clusterId = "sbx" + Guid.NewGuid().ToString("N")[..10];
            _secretVault.Provide("db-token", "vault-custody-super-secret-passphrase");
        }

        public async Task StartAsync(CancellationToken ct)
        {
            _siloLease = PortReservation.ReservePair();
            var siloPort = _siloLease.SiloPort;
            var gatewayPort = _siloLease.GatewayPort;

            var modules = new[] { typeof(SandboxConformanceModule) };
            var moduleAssemblies = new[] { typeof(SandboxConformanceModule).Assembly, typeof(Signal).Assembly }.Distinct().ToArray();

            if (_target == AzuriteOrleansTarget.TargetName)
            {
                var storage = _azurite.Require();
                await new BlobServiceClient(storage).GetBlobContainerClient(CloudProfile.LeaseContainer).CreateIfNotExistsAsync(cancellationToken: ct);

                var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
                var values = new Dictionary<string, string?>
                {
                    [RuntimeOptions.Default.TopologyKey] = "cloud",
                    [RuntimeOptions.Default.HomeKey] = _home,
                    [RuntimeOptions.Default.CloudStorageKey] = storage,
                    [TopologyContext.SiloPortKey] = siloPort.ToString(CultureInfo.InvariantCulture),
                    [TopologyContext.GatewayPortKey] = gatewayPort.ToString(CultureInfo.InvariantCulture),
                    [ModuleConfiguration.ModuleSection(SandboxConformanceModule.ModuleId) + ":Secrets:db-token"] = "vault-custody-super-secret-passphrase",
                };
                builder.Configuration.AddInMemoryCollection(values);
                builder.UseClusterStore(new BrainStorageRegistration(moduleAssemblies));
                builder.UseModuleTopology(modules, [], new CloudProfile());
                builder.UseOrleans(silo =>
                {
                    silo.ConfigureTopologySilo();
                    silo.Configure<Orleans.Configuration.ClusterOptions>(o => { o.ClusterId = _clusterId; o.ServiceId = _clusterId; });
                    silo.Configure<Orleans.Configuration.EndpointOptions>(o => o.AdvertisedIPAddress = IPAddress.Loopback);
                    silo.Services.TryAddSingleton<ICallFilter, CallFilter>();
                    silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IIncomingGrainCallFilter, PlainNeuronCallFilter>());
                    silo.Services.AddSingleton<ISecretVaultPort>(_secretVault);
                    silo.Services.AddSingleton(_secretVault);
                    silo.Services.AddSecretsCapability<InMemorySecretVault>();
                });
                _siloHost = builder.Build();
            }
            else
            {
                var factory = new LocalSiloHostFactory(
                    new BrainStorageRegistration(moduleAssemblies),
                    services =>
                    {
                        services.TryAddSingleton<ICallFilter, CallFilter>();
                        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIncomingGrainCallFilter, PlainNeuronCallFilter>());
                        services.AddSingleton<ISecretVaultPort>(_secretVault);
                        services.AddSingleton(_secretVault);
                        services.AddSecretsCapability<InMemorySecretVault>();
                    });

                var inProcessOptions = new InProcessHostOptions
                {
                    Home = _home,
                    Modules = modules,
                    Configuration = new Dictionary<string, string?>
                    {
                        [ModuleConfiguration.ModuleSection(SandboxConformanceModule.ModuleId) + ":Secrets:db-token"] = "vault-custody-super-secret-passphrase",
                    },
                };
                _siloHost = factory.Build(inProcessOptions, _home, _siloLease);
            }

            await _siloHost.StartAsync(ct).ConfigureAwait(false);

            // Start HTTP Edge
            var edgeBuilder = WebApplication.CreateSlimBuilder();
            edgeBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            edgeBuilder.Logging.ClearProviders();

            var dispatcher = new SandboxSiloInvocationDispatcher(_siloHost.Services);
            edgeBuilder.Services.AddSingleton<IRuntimeInvocationDispatcher>(dispatcher);
            edgeBuilder.Services.AddRuntimeEdge(options =>
            {
                options.SecretKey = EdgeSecretKey;
                options.TokenLifetime = TimeSpan.FromMinutes(10);
            });

            _httpEdgeApp = edgeBuilder.Build();
            _httpEdgeApp.MapRuntimeEdge();
            await _httpEdgeApp.StartAsync(ct).ConfigureAwait(false);

            var address = _httpEdgeApp.Urls.First();
            EdgeBaseUri = new Uri(address);
        }

        public async Task RestartSiloAsync(CancellationToken ct)
        {
            if (_siloHost is not null)
            {
                await _siloHost.StopAsync(ct).ConfigureAwait(false);
                if (_siloHost is IAsyncDisposable asyncSilo) { await asyncSilo.DisposeAsync().ConfigureAwait(false); }
                else { _siloHost.Dispose(); }
                _siloHost = null;
            }

            if (_httpEdgeApp is not null)
            {
                await _httpEdgeApp.StopAsync(ct).ConfigureAwait(false);
                await _httpEdgeApp.DisposeAsync().ConfigureAwait(false);
                _httpEdgeApp = null;
            }

            _siloLease?.Dispose();
            _siloLease = null;

            await StartAsync(ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_httpEdgeApp is not null)
            {
                try { await _httpEdgeApp.StopAsync(); } catch { }
                await _httpEdgeApp.DisposeAsync();
                _httpEdgeApp = null;
            }

            if (_siloHost is not null)
            {
                try { await _siloHost.StopAsync(); } catch { }
                if (_siloHost is IAsyncDisposable asyncHost) { await asyncHost.DisposeAsync(); }
                else { _siloHost.Dispose(); }
                _siloHost = null;
            }

            _siloLease?.Dispose();
            _siloLease = null;

            if (Directory.Exists(_home))
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try { Directory.Delete(_home, recursive: true); break; }
                    catch { await Task.Delay(100); }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(TopologyTargets))]
    public async Task AllowedNeuronCapabilityCallSucceedsThroughSessionConnection(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brainId = "sandbox-allowed-" + Guid.NewGuid().ToString("N")[..8];
        await using var env = new SandboxedTopologyEnvironment(target, azurite);
        await env.StartAsync(ct);

        var token = SessionTokens.Issue(EdgeSecretKey, brainId, "session-allowed", TimeSpan.FromMinutes(10));
        var brainUri = new Uri(env.EdgeBaseUri, $"/brains/{brainId}/");

        await using var connection = await DigitalBrainConnection.OpenAsync(
            brainUri,
            token,
            behavior: brainId,
            handler: null,
            cancellationToken: ct);

        var gated = connection.Neuron<ISandboxGatedNeuron>("default");
        var result = await gated.AllowedCapabilityAsync("hello-sandbox");

        Assert.Equal("Allowed: hello-sandbox (call #1)", result);
    }

    [Theory]
    [MemberData(nameof(TopologyTargets))]
    public async Task CallWithoutPermissionOrWithTamperedTokenIsRejected(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brainId = "sandbox-rejected-" + Guid.NewGuid().ToString("N")[..8];
        await using var env = new SandboxedTopologyEnvironment(target, azurite);
        await env.StartAsync(ct);

        var brainUri = new Uri(env.EdgeBaseUri, $"/brains/{brainId}/");

        // 1. Tampered token rejected at edge with 401 Unauthorized
        var validToken = SessionTokens.Issue(EdgeSecretKey, brainId, "session-valid", TimeSpan.FromMinutes(10));
        var tamperedToken = validToken[..^4] + "zzzz";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await using var badConnection = await DigitalBrainConnection.OpenAsync(
                brainUri,
                tamperedToken,
                behavior: brainId,
                handler: null,
                cancellationToken: ct);
        });

        // 2. Token speaking for a different brain is rejected
        var foreignToken = SessionTokens.Issue(EdgeSecretKey, "foreign-brain", "session-foreign", TimeSpan.FromMinutes(10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await using var badConnection = await DigitalBrainConnection.OpenAsync(
                brainUri,
                foreignToken,
                behavior: brainId,
                handler: null,
                cancellationToken: ct);
        });

        // 3. Method without grant / marked [Internal] is refused by PlainNeuronCallFilter
        await using var connection = await DigitalBrainConnection.OpenAsync(
            brainUri,
            validToken,
            behavior: brainId,
            handler: null,
            cancellationToken: ct);

        var gated = connection.Neuron<ISandboxGatedNeuron>("default");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await gated.UngrantedCapabilityAsync("hack-attempt");
        });
    }

    [Theory]
    [MemberData(nameof(TopologyTargets))]
    public async Task VaultSecretCustodyInvariantHoldsInsideSiloAcrossRestart(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brainId = "sandbox-vault-" + Guid.NewGuid().ToString("N")[..8];
        await using var env = new SandboxedTopologyEnvironment(target, azurite);
        await env.StartAsync(ct);

        var token = SessionTokens.Issue(EdgeSecretKey, brainId, "session-vault", TimeSpan.FromMinutes(10));
        var brainUri = new Uri(env.EdgeBaseUri, $"/brains/{brainId}/");

        await using (var connection = await DigitalBrainConnection.OpenAsync(
            brainUri,
            token,
            behavior: brainId,
            handler: null,
            cancellationToken: ct))
        {
            var guard = connection.Neuron<ISandboxVaultGuardNeuron>("default");

            // Client can read the metadata / label without the plaintext secret being returned
            var label = await guard.GetSecretLabelAsync("db-token");
            Assert.Equal("db-token", label);

            // In-silo resolution obtains the secret cleanly
            var plaintext = await guard.UnsafeExposeSecretPlaintextAsync("db-token");
            Assert.Equal("vault-custody-super-secret-passphrase", plaintext);
        }

        // Verify secret custody invariant: vault port callers were in-silo CapabilityCallers, and
        // port logs show named resolution executed inside the silo boundary
        Assert.NotEmpty(env.SecretVault.Callers);
        Assert.All(env.SecretVault.Callers, caller => Assert.Equal(brainId, caller.BrainId));

        // Restart silo and verify state and access control recover cleanly
        await env.RestartSiloAsync(ct);

        var refreshedToken = SessionTokens.Issue(EdgeSecretKey, brainId, "session-vault-post-restart", TimeSpan.FromMinutes(10));
        var refreshedBrainUri = new Uri(env.EdgeBaseUri, $"/brains/{brainId}/");

        await using (var connectionAfterRestart = await DigitalBrainConnection.OpenAsync(
            refreshedBrainUri,
            refreshedToken,
            behavior: brainId,
            handler: null,
            cancellationToken: ct))
        {
            var guard = connectionAfterRestart.Neuron<ISandboxVaultGuardNeuron>("default");
            var label = await guard.GetSecretLabelAsync("db-token");
            Assert.Equal("db-token", label);

            var plaintext = await guard.UnsafeExposeSecretPlaintextAsync("db-token");
            Assert.Equal("vault-custody-super-secret-passphrase", plaintext);
        }
    }
}
