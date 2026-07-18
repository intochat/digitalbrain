using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using DigitalBrain.Kernel.Experiences;
// UiWidget for Column ctor in map emit + BrainGraphNeuron for real map surface emit on tap/activate

namespace DigitalBrain.Kernel.DigitalBrain;

// Minimal IDigitalBrain for self-improving fast prototype.
// Everything else deleted per 5 steps. Demo + synapse log + authoring stub + N+1.
public class DigitalBrain : Neuron, IDigitalBrain
{
    private readonly List<string> _installed = ["demo", "shell"];
    private readonly List<string> _published = ["demo", "shell", "marketplace", "creator"];

    public async Task SendAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        var payload = synapse?.ToString() ?? "";
        if (payload.Contains("Demo") || payload.Contains("\"Type\":\"Demo\""))
        {
            var now = DateTime.UtcNow.ToString("HH:mm:ss");
            await Emit(new UiSurface("synapse-log", Self, new Text($"[server-synapse] DemoAction at {now}")));
            var demoTitle = _installed.Contains("demo-authoring") ? "Demo Executed (authoring variant)" : "Demo Executed";
            await Emit(new UiSurface("demo-result", Self, new Card(demoTitle, new Text($"Synapse logged from kernel at {now}"))));
            await Emit(new UiSurface("brain-map", Self, new Column(new UiWidget[] { new Text("Map: shell | creator(LLM) | ollama(gemma4-26b) | kernel") } )));
        }
        if (payload.Contains("brain-map") || payload.Contains("Map"))
        {
            // Use BrainGraphNeuron participation + simple list/widget emit on map tap/activate (per polish req)
            await Emit(new UiSurface("brain-map", Self, new Column(new UiWidget[] {
                new Text("• shell"),
                new Text("• creator (LLM)"),
                new Text("• ollama (gemma4-26b)"),
                new Text("• kernel"),
                new Text("• marketplace (bundles)"),
                new Text("• brain-graph")
            })));
            // Trigger brain graph for real surface if activated
            try { var g = GrainFactory.GetGrain<IBrainGraphNeuron>("brain-graph"); await g.PingAsync(); } catch { }
        }
        await Emit(synapse);
    }

    public Task<IReadOnlyList<NeuronId>> ListSubscribersAsync(string synapseTypeName, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<NeuronId>>(Array.Empty<NeuronId>());

    public Task<IReadOnlyList<string>> ListActiveNeuronTypesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(new[] { "shell", "marketplace", "rulehost", "demo", "brain-graph" });

    public Task<IReadOnlyList<Synapse>> GetRecentHistoryAsync(int max = 10, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Synapse>>(new List<Synapse>());

    public Task<IReadOnlyList<Synapse>> GetFullJournalAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Synapse>>(new List<Synapse>());

    public async Task<BundleInstalled> InstallBundleAsync(InstallBundle command, CancellationToken cancellationToken = default)
    {
        await InstallBundleAsync(command.BundleId, cancellationToken);
        return new BundleInstalled(command.BundleId);
    }

    public Task PublishBundleAsync(string bundleId, string? description = null, CancellationToken cancellationToken = default)
    {
        if (!_published.Contains(bundleId)) _published.Add(bundleId);
        return Emit(new BundlePublished(bundleId));
    }

    public Task<IReadOnlyList<string>> ListPublishedBundlesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_published);

    public async Task InstallBundleAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        if (!_installed.Contains(bundleId)) _installed.Add(bundleId);
        await Emit(new BundleInstalled(bundleId));
    }

    public Task UninstallBundleAsync(string bundleId, CancellationToken cancellationToken = default)
    { _installed.Remove(bundleId); return Task.CompletedTask; }

    public Task<IReadOnlyList<string>> ListInstalledBundlesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_installed);

    public Task RunExperienceAsync(string experienceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Additional from full IDigitalBrain (stubs for prototype)
    public Task<WorldConnectionInfo> StartWorldAsync(string worldId, CancellationToken ct = default) => Task.FromResult(new WorldConnectionInfo("root", "localhost", "11111", "30000", null));
    public Task<WorldConnectionInfo?> GetWorldConnectionAsync(string worldId, CancellationToken ct = default) => Task.FromResult<WorldConnectionInfo?>(new WorldConnectionInfo("root", "localhost", "11111", "30000", null));
    public Task<BrainIdentity> GetIdentityAsync(CancellationToken ct = default) => Task.FromResult(new BrainIdentity("root", "self", DateTimeOffset.UtcNow));
    public Task<string> SignAsync(string payload, CancellationToken ct = default) => Task.FromResult("sig-stub");
    public Task<WorldConnectionInfo> ForkBrainAsync(string fromWorld, string newWorld, DateTimeOffset? at = null, CancellationToken ct = default) => Task.FromResult(new WorldConnectionInfo(newWorld, "localhost", "11111", "30000", null));

    private sealed class DefaultClient : IDigitalBrainClient, IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
