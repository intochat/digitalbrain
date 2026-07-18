namespace Ino.Gateway;

/// <summary>
/// Runtime client configuration returned by <c>GET /config.json</c>. Flutter
/// (web, desktop, Telegram mini-app), the MCP transport, and any future
/// external client fetches this once at startup and configures its endpoints,
/// retry policy, and telemetry target from the result. The shape is
/// transport-neutral — gRPC / MCP / CLI all project the same record onto
/// their own surface.
///
/// Bound from configuration <c>InoClient</c> section; unset fields fall back
/// to the property-level defaults. Relative path defaults ("/", "/otlp",
/// "/health") are rewritten by the <c>/config.json</c> handler at request
/// time to absolute URLs anchored on the requesting client's origin so
/// clients see reachable endpoints without cross-origin surprises.
///
/// Served as a CDN-cacheable asset (ETag + Cache-Control) from the gateway's
/// HTTPS origin. Under high load this is one tiny file per client session —
/// the CDN absorbs the traffic. Breaking-change path is to bump
/// <see cref="Version"/>; clients revalidate on each load.
/// </summary>
public sealed record InoClientConfig
{
    public string GrpcEndpoint { get; init; } = "/";
    public string OtlpEndpoint { get; init; } = "/otlp";
    public string HealthEndpoint { get; init; } = "/health";
    public bool TransportSecure { get; init; } = true;
    public InoRetryPolicy RetryPolicy { get; init; } = new();
    public string Version { get; init; } = "0.1.0";
}

/// <summary>
/// Standard retry policy for client-initiated gRPC calls. Matches the
/// .NET-side <c>ConfigureHttpClientDefaults().AddStandardResilienceHandler()</c>
/// defaults so every transport (Flutter / MCP / CLI) behaves identically on
/// transient failures. Milliseconds.
/// </summary>
public sealed record InoRetryPolicy
{
    public int MaxRetries { get; init; } = 3;
    public int InitialBackoffMs { get; init; } = 200;
    public int MaxBackoffMs { get; init; } = 5_000;
    public int TimeoutMs { get; init; } = 30_000;
}
