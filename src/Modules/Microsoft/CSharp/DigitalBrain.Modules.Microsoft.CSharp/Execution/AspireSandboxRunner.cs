using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.Aspire;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

// Development: every owner shares the csharp-sandbox resource. It starts on demand through IAspire
// and its URL comes from the resource state the AppHost reports, so nothing is wired until needed.
internal sealed class AspireSandboxRunner(HttpClient http, IGrainFactory grains, IDigitalBrain brain, IOptions<CSharpOptions> options) : ICSharpRunner
{
    internal static readonly TimeSpan SandboxStartTimeout = TimeSpan.FromMinutes(4);
    internal static readonly TimeSpan InspectProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly HashSet<string> StoppedStates = new(["NotStarted", "Exited", "Finished", "FailedToStart", "Unknown"], StringComparer.Ordinal);

    private readonly SandboxRunsApi _api = new(http, static _ => ValueTask.FromResult<string?>(null));

    private IAspire Aspire => grains.GetGrain<IAspire>(options.Value.AspireApplication);

    public async Task StartAsync(CSharpRun run, CancellationToken cancellationToken)
        => await _api.StartAsync(await EnsureSandboxAsync(cancellationToken).ConfigureAwait(false), "", run, cancellationToken).ConfigureAwait(false);

    public async Task StopAsync(string owner, string runId, CancellationToken cancellationToken)
    {
        if (runId.Length == 0 || await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is not { } sandbox) { return; }
        await _api.StopAsync(sandbox, "", runId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CSharpRunState> InspectAsync(string owner, string runId, CancellationToken cancellationToken)
    {
        if (runId.Length == 0 || await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is not { } sandbox)
        { return CSharpRunState.Stopped; }
        // A stopped container does not surface as a state change on every runtime, and the
        // endpoint proxy can keep accepting connections for a backend that is gone, so an
        // unreachable or unresponsive sandbox counts as the run being lost, within a bounded probe.
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(InspectProbeTimeout);
        try { return await _api.InspectAsync(sandbox, "", runId, probe.Token).ConfigureAwait(false); }
        catch (HttpRequestException) { return CSharpRunState.Stopped; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return CSharpRunState.Stopped; }
    }

    public async Task<string> LogsAsync(string owner, string runId, int tail, CancellationToken cancellationToken)
        => runId.Length > 0 && await FindSandboxAsync(cancellationToken).ConfigureAwait(false) is { } sandbox
            ? await _api.LogsAsync(sandbox, "", runId, tail, cancellationToken).ConfigureAwait(false)
            : "";

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
}
