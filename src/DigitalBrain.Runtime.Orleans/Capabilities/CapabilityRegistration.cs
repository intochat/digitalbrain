using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// Registers the runtime's single-connection capabilities over a host port (R4-02, D-09): keyed singletons under the capability
/// id, which <see cref="ICapabilityBroker.Resolve{TCapability}"/> reads. Registering a capability twice fails silo startup
/// (the single-registration check of <see cref="CapabilityHosting"/>).
/// </summary>
public static class CapabilityRegistration
{
    /// <summary>
    /// secrets@1 (<see cref="SecretsCapability"/>) over <typeparamref name="TPort"/>; named secrets are checked against the
    /// manifests of the modules the host composed.
    /// </summary>
    public static IServiceCollection AddSecretsCapability<TPort>(this IServiceCollection services) where TPort : class, ISecretVaultPort
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TPort>();
        services.AddKeyedSingleton<ISecrets>(Capability.Secrets.V1Id, (sp, _) => new SecretsCapability(sp.GetRequiredService<TPort>(), sp.GetService<ILogger<SecretsCapability>>()) { Declarations = TopologyHosting.ComposedDeclarations(sp) });
        return services;
    }

    /// <summary>identity@1 (<see cref="IdentityCapability"/> and <see cref="GrantsCapability"/>) over <typeparamref name="TPort"/>.</summary>
    public static IServiceCollection AddIdentityCapability<TPort>(this IServiceCollection services) where TPort : class, IIdentityDirectoryPort
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TPort>();
        services.AddKeyedSingleton<IIdentity>(Capability.Identity.V1Id, (sp, _) => new IdentityCapability(sp.GetRequiredService<TPort>()));
        services.AddKeyedSingleton<IGrants>(Capability.Identity.V1Id, (sp, _) => new GrantsCapability(sp.GetRequiredService<TPort>()));
        return services;
    }

    /// <summary>
    /// identity@1 sessions and description (<see cref="SessionsCapability"/>: <see cref="ISessions"/> and
    /// <see cref="IIdentityProvider"/>) over <typeparamref name="TPort"/>. The privileged grants come from the manifests of the
    /// modules the host composed (<see cref="TopologyHosting.UseModuleTopology"/>); with none, every session call is refused.
    /// </summary>
    public static IServiceCollection AddIdentitySessions<TPort>(this IServiceCollection services) where TPort : class, IIdentitySessionPort
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TPort>();
        services.TryAddSingleton(TopologyHosting.ComposedDeclarations);
        services.AddKeyedSingleton<SessionsCapability>(Capability.Identity.V1Id, (sp, _) => new SessionsCapability(sp.GetRequiredService<TPort>(), sp.GetRequiredService<ModuleDeclarations>()));
        services.AddKeyedSingleton<ISessions>(Capability.Identity.V1Id, (sp, key) => sp.GetRequiredKeyedService<SessionsCapability>(key));
        services.AddKeyedSingleton<IIdentityProvider>(Capability.Identity.V1Id, (sp, key) => sp.GetRequiredKeyedService<SessionsCapability>(key));
        return services;
    }

    /// <summary>identity@1 sessions and description over <typeparamref name="TPort"/>, on the host builder.</summary>
    public static IHostApplicationBuilder AddIdentitySessions<TPort>(this IHostApplicationBuilder builder) where TPort : class, IIdentitySessionPort
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddIdentitySessions<TPort>();
        return builder;
    }

    /// <summary>secrets@1 over <typeparamref name="TPort"/>, on the host builder.</summary>
    public static IHostApplicationBuilder AddSecretsCapability<TPort>(this IHostApplicationBuilder builder) where TPort : class, ISecretVaultPort
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSecretsCapability<TPort>();
        return builder;
    }

    /// <summary>identity@1 over <typeparamref name="TPort"/>, on the host builder.</summary>
    public static IHostApplicationBuilder AddIdentityCapability<TPort>(this IHostApplicationBuilder builder) where TPort : class, IIdentityDirectoryPort
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddIdentityCapability<TPort>();
        return builder;
    }
}
