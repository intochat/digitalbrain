using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ino.Aspire.Hosting;

/// <summary>
/// Cloudflared quick-tunnel integration. Adds an Aspire-managed
/// <c>cloudflared</c> executable that exposes a local HTTP port as a public
/// <c>https://*.trycloudflare.com</c> URL. A health check parses the tunnel
/// URL out of the cloudflared log file and surfaces it in the dashboard
/// (Healthy → description carries the resolved public URL); the same URL
/// is injected as an env var on the dependent project so webhook + mini-app
/// callbacks resolve to the public origin without manual ngrok plumbing.
///
/// <para>Mirrors TripRadar's
/// <c>tripradar/src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs</c>
/// — kept behaviour-identical so the dashboard UX matches.</para>
///
/// <para>Requires the <c>cloudflared</c> binary on PATH; tunnels last only
/// for the lifetime of the AppHost run (quick-tunnel mode, no auth).</para>
/// </summary>
public static partial class CloudflaredExtensions
{
    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com")]
    private static partial Regex TunnelUrlPattern();

    /// <summary>
    /// Adds a cloudflared executable that tunnels <paramref name="localPort"/>,
    /// makes the project <see cref="ResourceBuilderExtensions.WaitFor"/> the
    /// tunnel, and once the tunnel URL is resolved, copies it into each env
    /// var name in <paramref name="environmentVariableNames"/> on the project.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithCloudflaredTunnel(
        this IResourceBuilder<ProjectResource> project,
        string tunnelResourceName,
        int localPort,
        params string[] environmentVariableNames)
    {
        var tunnel = project.ApplicationBuilder.AddCloudflaredTunnel(tunnelResourceName, localPort);
        var logFile = GetLogFilePath(tunnelResourceName);

        return project
            .WaitFor(tunnel)
            .WithEnvironment(context =>
            {
                var tunnelUrl = ExtractTunnelUrl(logFile);
                if (tunnelUrl is null) return;
                foreach (var envVarName in environmentVariableNames)
                    context.EnvironmentVariables[envVarName] = tunnelUrl;
            });
    }

    /// <summary>
    /// Adds the cloudflared tunnel as a top-level executable resource.
    /// The associated health check publishes the resolved tunnel URL as
    /// its description, so the Aspire dashboard surfaces it once the
    /// tunnel finishes provisioning.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddCloudflaredTunnel(
        this IDistributedApplicationBuilder builder,
        string name,
        int localPort)
    {
        var logFilePath = GetLogFilePath(name);
        var logDirectory = Path.GetDirectoryName(logFilePath)!;

        if (!Directory.Exists(logDirectory))
            Directory.CreateDirectory(logDirectory);

        TryCleanLogFile(logFilePath);

        var healthCheckName = $"{name}-tunnel-health";

        builder.Services
            .AddHealthChecks()
            .AddCheck(healthCheckName, new CloudflaredTunnelHealthCheck(logFilePath));

        return builder
            .AddExecutable(
                name,
                "cloudflared",
                workingDirectory: ".",
                "tunnel", "--url", $"http://localhost:{localPort}", "--logfile", logFilePath)
            .WithHealthCheck(healthCheckName);
    }

    static void TryCleanLogFile(string path)
    {
        if (!File.Exists(path)) return;

        try { File.Delete(path); }
        catch (IOException)
        {
            // File is locked by a previous run — truncate in place so the
            // URL extractor sees only the current run's output.
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Truncate, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException) { }
        }
    }

    internal static string GetLogFilePath(string tunnelName)
        => Path.Combine(Path.GetTempPath(), "aspire-cloudflared", $"{tunnelName}.log");

    internal static string? ExtractTunnelUrl(string logFilePath)
    {
        if (!File.Exists(logFilePath)) return null;

        try
        {
            using var stream = new FileStream(
                logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var logContent = reader.ReadToEnd();
            var match = TunnelUrlPattern().Match(logContent);
            return match.Success ? match.Value : null;
        }
        catch (IOException) { return null; }
    }
}

internal sealed class CloudflaredTunnelHealthCheck(string logFilePath) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var tunnelUrl = CloudflaredExtensions.ExtractTunnelUrl(logFilePath);

        var result = tunnelUrl is not null
            ? HealthCheckResult.Healthy(tunnelUrl)
            : HealthCheckResult.Unhealthy("Tunnel URL not yet available");

        return Task.FromResult(result);
    }
}
