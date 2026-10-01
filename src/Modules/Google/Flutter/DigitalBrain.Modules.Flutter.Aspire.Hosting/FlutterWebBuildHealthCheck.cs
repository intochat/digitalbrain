using System.Runtime.CompilerServices;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DigitalBrain.Flutter.Aspire.Hosting;

// Flutter serves cached web assets while compiling. HTTP 200 alone can therefore
// expose a previous build (including its old runtime endpoint) to browsers.
internal sealed class FlutterWebBuildHealthCheck(
    Func<CancellationToken, IAsyncEnumerable<string>> readLines,
    Func<CancellationToken, Task<bool>>? bundleServed = null) : IHealthCheck
{
    public FlutterWebBuildHealthCheck(ResourceLoggerService logs, IResource resource, EndpointReference endpoint)
        : this(ct => ReadLines(logs, resource, ct), ct => BundleServedAsync(endpoint, ct)) { }

    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static async IAsyncEnumerable<string> ReadLines(ResourceLoggerService logs, IResource resource, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var batch in logs.GetAllAsync(resource).WithCancellation(ct))
        { foreach (var line in batch) { yield return line.Content; } }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var ready = false;
        try
        {
            await foreach (var line in readLines(cancellationToken).WithCancellation(cancellationToken))
            {
                if (line.Contains("Starting process...", StringComparison.Ordinal)
                    || line.Contains("Launching lib", StringComparison.Ordinal))
                { ready = false; }
                else if (line.Contains(" is being served at http", StringComparison.Ordinal))
                { ready = true; }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A replaced executable instance loses its log stream; the bundle probe still decides.
        }
        if (ready) { return HealthCheckResult.Healthy(); }
        // This check guards the release web-server device, which serves main.dart.js only once
        // its single compilation landed. Some runtimes never surface the process's stdout, so
        // the compiled bundle itself is the readiness signal of last resort.
        if (bundleServed is not null && await bundleServed(cancellationToken).ConfigureAwait(false))
        { return HealthCheckResult.Healthy(); }
        return HealthCheckResult.Unhealthy("Waiting for the current Flutter web compilation.");
    }

    private static async Task<bool> BundleServedAsync(EndpointReference endpoint, CancellationToken ct)
    {
        try
        {
            using var response = await Probe.GetAsync(new Uri(new Uri(endpoint.Url), "main.dart.js"),
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return false; }
    }
}
