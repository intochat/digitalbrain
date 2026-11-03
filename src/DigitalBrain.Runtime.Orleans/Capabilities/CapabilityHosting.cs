using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>Host configuration of the capability registry, read when the silo is configured.</summary>
public static class CapabilityHosting
{
    internal static readonly Func<IServiceProvider, object?, IIdentity> DeniedIdentityFactory =
        static (_, _) => new IdentityCapability(DeniedIdentityDirectoryPort.Instance);

    internal static readonly Func<IServiceProvider, object?, IGrants> DeniedGrantsFactory =
        static (_, _) => new GrantsCapability(DeniedIdentityDirectoryPort.Instance);

    /// <summary>
    /// Configures the one <see cref="CapabilityRegistry"/> of this host (for example <c>AcceptBinding(storage@1, typeof(IStorage))</c>).
    /// Call before the silo is configured; module validation runs there.
    /// </summary>
    public static IHostApplicationBuilder ConfigureCapabilities(this IHostApplicationBuilder builder, Action<CapabilityRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(Registry(builder.Services));
        return builder;
    }

    /// <summary>The registry instance in <paramref name="services"/>, added when absent.</summary>
    internal static CapabilityRegistry Registry(IServiceCollection services)
    {
        if (services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(CapabilityRegistry) && !descriptor.IsKeyedService)?.ImplementationInstance
            is CapabilityRegistry existing)
        {
            return existing;
        }
        var registry = new CapabilityRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    internal static bool IsDeniedIdentityFallback(ServiceDescriptor descriptor)
        => descriptor.IsKeyedService
            && (ReferenceEquals(descriptor.KeyedImplementationFactory, DeniedIdentityFactory)
                || ReferenceEquals(descriptor.KeyedImplementationFactory, DeniedGrantsFactory));

    internal static void RemoveDeniedIdentityFallbacks(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (IsDeniedIdentityFallback(services[index]))
            {
                services.RemoveAt(index);
            }
        }
    }

    /// <summary>Registers the broker over the host's <see cref="ICapabilityConnections"/> and the single-registration startup check.</summary>
    internal static void AddCapabilityBroker(this IServiceCollection services)
    {
        var registry = Registry(services);
        services.TryAddSingleton<DigitalBrain.Runtime.Orleans.Capabilities.Storage.StorageAccessLog>();
        if (!services.Any(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(IIdentity) && Capability.Identity.V1Id.Equals(descriptor.ServiceKey as string, StringComparison.Ordinal)))
        {
            services.AddKeyedSingleton(Capability.Identity.V1Id, DeniedIdentityFactory);
        }
        if (!services.Any(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(IGrants) && Capability.Identity.V1Id.Equals(descriptor.ServiceKey as string, StringComparison.Ordinal)))
        {
            services.AddKeyedSingleton(Capability.Identity.V1Id, DeniedGrantsFactory);
        }
        services.TryAddSingleton<IIdentity>(sp => sp.GetRequiredKeyedService<IIdentity>(Capability.Identity.V1Id));
        services.TryAddSingleton<IGrants>(sp => sp.GetRequiredKeyedService<IGrants>(Capability.Identity.V1Id));
        services.TryAddSingleton(sp => new CapabilityBroker(
            registry,
            sp.GetService<ICapabilityConnections>() ?? DeniedCapabilityConnections.Instance,
            sp,
            sp.GetServices<ICapabilityBindingInterceptor>()));
        services.TryAddSingleton<ICapabilityBroker>(sp => sp.GetRequiredService<CapabilityBroker>());
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(new SingleCapabilityCheck(registry, services));
    }

    // Fails silo startup when a single-connection capability is registered twice; reads the collection at start, after host registrations.
    private sealed class SingleCapabilityCheck(CapabilityRegistry registry, IServiceCollection services) : ILifecycleParticipant<ISiloLifecycle>
    {
        public void Participate(ISiloLifecycle lifecycle)
            => lifecycle.Subscribe(nameof(SingleCapabilityCheck), ServiceLifecycleStage.First, _ =>
            {
                var issues = CapabilityBroker.SingleRegistrationIssues(registry, services);
                return issues.Count == 0 ? Task.CompletedTask : throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
            });
    }

    private sealed class DeniedCapabilityConnections : ICapabilityConnections
    {
        public static readonly DeniedCapabilityConnections Instance = new();

        public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BrainConnection>>([]);

        public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
            => throw new UnauthorizedAccessException("connections@1: no connections service is registered on this host.");

        public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class DeniedIdentityDirectoryPort : IIdentityDirectoryPort
    {
        public static readonly DeniedIdentityDirectoryPort Instance = new();

        private static UnauthorizedAccessException Denied()
            => new("identity@1: no identity directory port is registered on this host.");

        public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken) => throw Denied();
        public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken) => throw Denied();
        public Task EnsureAccount(Account account, CancellationToken cancellationToken) => throw Denied();
        public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken) => throw Denied();
        public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken) => throw Denied();
        public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken) => throw Denied();
        public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken) => throw Denied();
        public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken) => throw Denied();
        public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken) => throw Denied();
        public Task<IReadOnlyList<DigitalBrain.Capabilities.Identity.BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken) => throw Denied();
        public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken) => throw Denied();
        public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken) => throw Denied();
        public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken) => throw Denied();
        public Task<Member?> EnsureMember(CapabilityCaller caller, Member member, CancellationToken cancellationToken) => throw Denied();
        public Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId, CancellationToken cancellationToken) => throw Denied();
        public Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId, CancellationToken cancellationToken) => throw Denied();
        public Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request, CancellationToken cancellationToken)
            => Task.FromResult<CallDecision?>(CallDecision.Deny(CallDenial.NotAuthenticated, "identity@1: no identity directory port is registered on this host."));
        public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken) => throw Denied();
        public Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant, CancellationToken cancellationToken) => throw Denied();
        public Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken) => throw Denied();
    }
}
