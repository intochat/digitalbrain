using DigitalBrain.Apps;
using DigitalBrain.Broker;
using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Marketplace;

// Sandbox run of a manifest's declared scenarios. Remote scenarios travel through the broker (the
// only path a third-party call may take) against the deterministic transport; declarative scenarios
// run in-silo and are recorded as passing. Whatever the fake transport reports as used data classes
// and meters is recorded by the broker, which is what the declared-vs-used diff later consumes.
internal sealed class BrokerScenarioRunner(IRemoteAppGateway gateway) : IManifestScenarioRunner
{
    public async Task<IReadOnlyList<ScenarioResult>> RunAsync(AppManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var results = new List<ScenarioResult>();
        if (manifest.Scenarios.Count == 0) { return results; }
        if (manifest.Kind != AppKind.Remote)
        {
            return [.. manifest.Scenarios.Select(scenario => new ScenarioResult
            {
                Name = scenario.Name,
                Passed = true,
                Detail = "Declarative app runs in the silo.",
            })];
        }

        var caller = CertificationCaller();
        var host = EndpointHost(manifest.RemoteEndpoint);
        foreach (var scenario in manifest.Scenarios)
        {
            var operation = manifest.Operations.Count > 0 ? manifest.Operations[0].Name : "run";
            var result = await gateway.InvokeAsync(new RemoteCallRequest
            {
                Manifest = manifest,
                Caller = caller,
                Operation = operation,
                EgressHosts = host is null ? [] : [host],
                IntentId = "certification",
            }, cancellationToken).ConfigureAwait(false);
            results.Add(new ScenarioResult
            {
                Name = scenario.Name,
                Passed = result.Allowed,
                Detail = result.Allowed ? result.Output : result.Explanation,
            });
        }

        return results;
    }

    private static CallerContext CertificationCaller() => new()
    {
        PrincipalId = "certifier",
        AccountId = "platform",
        WorkspaceId = "platform",
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        IntentId = "certification",
    };

    private static string? EndpointHost(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : null;
}
