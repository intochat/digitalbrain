using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Runtime.CompilerServices;

namespace DigitalBrain.Flutter.Aspire.Hosting;

// Flutter serves cached web assets while compiling. HTTP 200 alone can therefore
// expose a previous build (including its old runtime endpoint) to browsers.
internal sealed class FlutterWebBuildHealthCheck(Func<CancellationToken, IAsyncEnumerable<string>> readLines) : IHealthCheck
{
    public FlutterWebBuildHealthCheck(ResourceLoggerService logs, IResource resource)
        : this(ct => ReadLines(logs, resource, ct)) { }

    private static async IAsyncEnumerable<string> ReadLines(ResourceLoggerService logs, IResource resource, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var batch in logs.GetAllAsync(resource).WithCancellation(ct))
        { foreach (var line in batch) { yield return line.Content; } }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var ready = false;
        await foreach (var line in readLines(cancellationToken).WithCancellation(cancellationToken))
        {
            if (line.Contains("Starting process...", StringComparison.Ordinal)
                || line.Contains("Launching lib", StringComparison.Ordinal))
            { ready = false; }
            else if (line.Contains(" is being served at http", StringComparison.Ordinal))
            { ready = true; }
        }
        return ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Waiting for the current Flutter web compilation.");
    }
}
