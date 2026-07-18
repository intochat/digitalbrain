using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting.Cloudflared;

internal static partial class CloudflaredExtensions
{
    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com")]
    private static partial Regex TunnelUrlPattern();

    extension(IResourceBuilder<ProjectResource> project)
    {
        public IResourceBuilder<ProjectResource> WithCloudflaredTunnel(string name, int localPort)
        {
            var tunnel = project.ApplicationBuilder.AddCloudflaredTunnel(name, localPort);
            var logFile = GetLogFilePath(name);

            return project
                .WaitFor(tunnel)
                .WithEnvironment(context =>
                {
                    var tunnelUrl = ExtractTunnelUrl(logFile);
                    if (tunnelUrl is not null)
                    {
                        context.EnvironmentVariables["Bot__WebhookUrl"] = tunnelUrl;
                        context.EnvironmentVariables["Bot__MiniAppUrl"] = tunnelUrl;
                    }
                });
        }
    }

    extension(IResourceBuilder<ExecutableResource> resource)
    {
        public IResourceBuilder<ExecutableResource> WithCloudflaredTunnel(
            string name, int localPort, bool useHttps = false, Action<IDictionary<string, object>, string>? configureEnvironment = null)
        {
            var tunnel = resource.ApplicationBuilder.AddCloudflaredTunnel(name, localPort, useHttps);
            var logFile = GetLogFilePath(name);

            var result = resource.WaitFor(tunnel);

            if (configureEnvironment is null)
                return result;

            return result.WithEnvironment(context =>
            {
                var tunnelUrl = ExtractTunnelUrl(logFile);
                if (tunnelUrl is not null)
                    configureEnvironment(context.EnvironmentVariables, tunnelUrl);
            });
        }
    }

    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<ExecutableResource> AddCloudflaredTunnel(string name, int localPort, bool useHttps = false)
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

            var scheme = useHttps ? "https" : "http";
            var args = new List<string> { "tunnel", "--url", $"{scheme}://localhost:{localPort}", "--logfile", logFilePath };

            if (useHttps)
                args.AddRange(["--no-tls-verify"]);

            return builder
                .AddExecutable(name, "cloudflared", ".", [.. args])
                .WithHealthCheck(healthCheckName);
        }
    }

    private static void TryCleanLogFile(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException) { }
        }
    }

    internal static string GetLogFilePath(string tunnelName)
        => Path.Combine(Path.GetTempPath(), "aspire-cloudflared", $"{tunnelName}.log");

    internal static string? ExtractTunnelUrl(string logFilePath)
    {
        if (!File.Exists(logFilePath))
            return null;

        try
        {
            using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var logContent = reader.ReadToEnd();
            var match = TunnelUrlPattern().Match(logContent);
            return match.Success ? match.Value : null;
        }
        catch (IOException)
        {
            return null;
        }
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
