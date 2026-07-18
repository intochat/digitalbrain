namespace Ino.Core.Hosting;

/// <summary>
/// Resolves Orleans cluster ports + clusterId/serviceId. Fixed defaults for
/// `aspire run` (so the dashboard URLs stay stable session over session)
/// fall back to env vars when the test harness wants ephemeral ports.
///
/// Why ephemeral in tests: each `DistributedApplicationTestingBuilder`-driven
/// test fixture spawns child silo processes that bind localhost ports. With
/// hard-coded ports two back-to-back test assemblies (e.g.
/// `Ino.Domains.Travel.Tests` immediately after `Ino.E2E.Tests`) race the
/// previous run's TIME_WAIT sockets and intermittently fail to bind.
/// Randomizing per fixture makes the fixtures isolated.
///
/// `InoTestAppHost` sets the env vars BEFORE `BuildAsync`; child silo
/// processes inherit them.
///
/// Domain silos: each domain process reads <see cref="DomainSiloPortEnv"/> /
/// <see cref="DomainGatewayPortEnv"/>; the Aspire AppHost sets a different
/// value per domain via <c>WithEnvironment</c>, so the env-var name is the
/// same in every silo process but the resolved port is unique.
/// </summary>
public static class InoOrleansEndpoints
{
    // Default ports (used by `aspire run` / `aspire start` for the local dev
    // dashboard). Tests overwrite via env vars.
    public const int DefaultKernelSiloPort = 11111;
    public const int DefaultKernelGatewayPort = 30000;
    public const int DefaultIdentitySiloPort = 11112;
    public const int DefaultIdentityGatewayPort = 30001;
    public const string DefaultClusterId = "ino";
    public const string DefaultServiceId = "ino";

    public const string KernelSiloPortEnv = "INO_ORLEANS_KERNEL_SILO_PORT";
    public const string KernelGatewayPortEnv = "INO_ORLEANS_KERNEL_GATEWAY_PORT";
    public const string IdentitySiloPortEnv = "INO_ORLEANS_IDENTITY_SILO_PORT";
    public const string IdentityGatewayPortEnv = "INO_ORLEANS_IDENTITY_GATEWAY_PORT";
    public const string ClusterIdEnv = "INO_ORLEANS_CLUSTER_ID";
    public const string ServiceIdEnv = "INO_ORLEANS_SERVICE_ID";

    public static int KernelSiloPort => ReadPort(KernelSiloPortEnv, DefaultKernelSiloPort);
    public static int KernelGatewayPort => ReadPort(KernelGatewayPortEnv, DefaultKernelGatewayPort);
    public static int IdentitySiloPort => ReadPort(IdentitySiloPortEnv, DefaultIdentitySiloPort);
    public static int IdentityGatewayPort => ReadPort(IdentityGatewayPortEnv, DefaultIdentityGatewayPort);
    public static string ClusterId => Environment.GetEnvironmentVariable(ClusterIdEnv) ?? DefaultClusterId;
    public static string ServiceId => Environment.GetEnvironmentVariable(ServiceIdEnv) ?? DefaultServiceId;

    /// <summary>
    /// Per-domain silo port. Each domain silo reads
    /// <c>INO_ORLEANS_DOMAIN_&lt;ID&gt;_SILO_PORT</c>; tests / AppHost set
    /// distinct values per domain so multi-silo runs don't collide on a
    /// shared port name. <paramref name="domainId"/> is normalised
    /// (uppercase, dots → underscores).
    /// </summary>
    public static int DomainSiloPort(DomainId domainId, int defaultPort) =>
        ReadPort(DomainSiloPortEnvFor(domainId), defaultPort);

    public static int DomainGatewayPort(DomainId domainId, int defaultPort) =>
        ReadPort(DomainGatewayPortEnvFor(domainId), defaultPort);

    public static string DomainSiloPortEnvFor(DomainId domainId) =>
        $"INO_ORLEANS_DOMAIN_{NormaliseId(domainId)}_SILO_PORT";

    public static string DomainGatewayPortEnvFor(DomainId domainId) =>
        $"INO_ORLEANS_DOMAIN_{NormaliseId(domainId)}_GATEWAY_PORT";

    static string NormaliseId(DomainId domainId) =>
        domainId.Value.Replace('.', '_').Replace('-', '_').ToUpperInvariant();

    static int ReadPort(string envVar, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envVar);
        return int.TryParse(raw, out var port) && port > 0 ? port : fallback;
    }
}
