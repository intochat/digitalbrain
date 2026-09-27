using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.Aspire;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace DigitalBrain.Microsoft.CSharp;

// Runs scripts in the csharp-sandbox resource. The sandbox starts on demand through IAspire and its
// URL comes from the resource state the AppHost reports, so nothing is wired until it is needed.
internal sealed class SandboxCSharpRunner(
    HttpClient http,
    IGrainFactory grains,
    IDigitalBrain brain,
    IOptions<CSharpOptions> options,
    IOptions<EndpointOptions> endpoint,
    IOptions<ClusterOptions> cluster)
{
    internal static readonly TimeSpan SandboxStartTimeout = TimeSpan.FromMinutes(4);
    // Orleans addresses the silo by its advertised IP; a container reaches a loopback silo only through the client's relay.
    private const string DockerHost = "host.docker.internal";
    private static readonly HashSet<string> StoppedStates = new(["NotStarted", "Exited", "Finished", "FailedToStart", "Unknown"], StringComparer.Ordinal);

    private IAspire Aspire => grains.GetGrain<IAspire>(options.Value.AspireApplication);

    public async Task StartAsync(string runId, string source, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var sandbox = await EnsureSandboxAsync(cancellationToken).ConfigureAwait(false);
        using var response = await http.PostAsJsonAsync(new Uri(sandbox, "runs?identifier=" + Uri.EscapeDataString(runId)),
            new SandboxRunRequest(source, ScriptEnvironment(settings)), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "start", cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(string runId, CancellationToken cancellationToken)
    {
        if (runId.Length == 0 || await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is not { } sandbox) { return; }
        using var response = await http.PostAsync(new Uri(sandbox, $"runs/{runId}/stop"), content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) { await EnsureSuccessAsync(response, "stop", cancellationToken).ConfigureAwait(false); }
    }

    public async Task<CSharpRunState> InspectAsync(string runId, CancellationToken cancellationToken)
    {
        if (runId.Length == 0 || await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is not { } sandbox) { return CSharpRunState.Stopped; }
        using var response = await http.GetAsync(new Uri(sandbox, $"runs/{runId}"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) { return CSharpRunState.Stopped; }
        await EnsureSuccessAsync(response, "inspect", cancellationToken).ConfigureAwait(false);
        var run = await response.Content.ReadFromJsonAsync<SandboxRunStatus>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The sandbox returned an empty run status.");
        var status = run.Status == "Running" ? CSharpFileStatus.Running : CSharpFileStatus.Exited;
        return new(status, run.ExitCode, run.StartedAt);
    }

    public async Task<string> LogsAsync(string runId, int tail, CancellationToken cancellationToken)
    {
        if (runId.Length == 0 || await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is not { } sandbox) { return ""; }
        var lines = Math.Clamp(tail, 1, 5000).ToString(CultureInfo.InvariantCulture);
        using var response = await http.GetAsync(new Uri(sandbox, $"runs/{runId}/logs?tail={lines}"), cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "";
    }

    private async Task<Uri> EnsureSandboxAsync(CancellationToken cancellationToken)
    {
        if (await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is { } running) { return running; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SandboxStartTimeout);
        await using var changes = await brain.SubscribeAsync<ResourceStateChanged>(Aspire, deadline.Token).ConfigureAwait(false);
        // Subscribed first, then read again, so a start that lands in between is not missed.
        var resources = await Aspire.ListResources(deadline.Token).ConfigureAwait(false);
        if (SandboxUrl(resources) is { } started) { return started; }
        var sandbox = resources.FirstOrDefault(resource => resource.Name == CSharpSandbox.ResourceName);
        if (sandbox is null || StoppedStates.Contains(sandbox.State))
        {
            await Aspire.StartResource(CSharpSandbox.ResourceName, deadline.Token).ConfigureAwait(false);
        }
        await foreach (var change in changes.ReadAllAsync(deadline.Token).ConfigureAwait(false))
        {
            if (change.Resource != CSharpSandbox.ResourceName) { continue; }
            if (change.State == "FailedToStart") { throw new InvalidOperationException("The C# sandbox failed to start. Check the csharp-sandbox resource in the Aspire dashboard."); }
            if (IsReady(change.State, change.Health) && SandboxUrl(await Aspire.ListResources(deadline.Token).ConfigureAwait(false)) is { } ready)
            {
                return ready;
            }
        }
        throw new InvalidOperationException("The sandbox state subscription ended before the sandbox was ready.");
    }

    private async Task<Uri?> FindSandboxAsync(CancellationToken cancellationToken)
        => SandboxUrl(await Aspire.ListResources(cancellationToken).ConfigureAwait(false));

    private static Uri? SandboxUrl(IReadOnlyList<AspireResource> resources)
        => resources.FirstOrDefault(resource => resource.Name == CSharpSandbox.ResourceName) is { } sandbox
            && IsReady(sandbox.State, sandbox.Health)
            && sandbox.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) is { } url
                ? new Uri(url.TrimEnd('/') + "/")
                : null;

    private static bool IsReady(string state, string? health) => state == "Running" && health is null or "Healthy";

    private Dictionary<string, string> ScriptEnvironment(IReadOnlyDictionary<string, string> settings)
    {
        var advertised = endpoint.Value.AdvertisedIPAddress ?? IPAddress.Loopback;
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Gateways"] = new UriBuilder("gwy.tcp", advertised.ToString(), endpoint.Value.GatewayPort, "0").Uri.ToString(),
            ["ClusterId"] = cluster.Value.ClusterId,
            ["ServiceId"] = cluster.Value.ServiceId,
        };
        if (IPAddress.IsLoopback(advertised)) { environment["GatewayRelayHost"] = DockerHost; }
        foreach (var (name, value) in settings) { environment["CSharpFile__Settings__" + name] = value; }
        return environment;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) { return; }
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"The sandbox could not {action} the script ({(int)response.StatusCode}): {detail}");
    }
}
