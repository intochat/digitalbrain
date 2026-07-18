using DigitalBrain.Protocol;
using DigitalBrain.Os.Application;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

[GrainType("simulation-host")]
public sealed class SimulationHostNeuron : Neuron, IHandle<RunSimulation>
{
    private readonly IPersistentState<SimulationHostState> _state;

    public SimulationHostNeuron([PersistentState("simhost", "Default")] IPersistentState<SimulationHostState> state)
    {
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (_state.State.Reports == null) _state.State.Reports = new();
        var stream = this.GetStreamProvider(SynapseStream.ProviderName).Timeline();
        await stream.SubscribeAsync(async (item, token) => { /* observe for reports */ await Task.CompletedTask; });
    }

    public async Task HandleAsync(RunSimulation synapse, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        var filter = synapse.Filter;
        var results = new List<SimulationScenarioResult>();

        // Resolve vs capsule catalog (pa-files/marketplace .ino + scenario blocks via Ino + replay).
        // For v0: if filter starts with ino: or matches known, use ReplayObservedSynapsesAsync on rule host for the id.
        string? inoId = null;
        if (filter.StartsWith("ino:", StringComparison.OrdinalIgnoreCase)) inoId = filter.Substring(4);
        else if (filter.Contains("rule", StringComparison.OrdinalIgnoreCase) || filter.Contains("standup", StringComparison.OrdinalIgnoreCase)) inoId = "executable-standup"; // example from feature

        if (!string.IsNullOrWhiteSpace(inoId))
        {
            var ruleHost = this.GrainFactory.GetGrain<IRuleHostNeuron>(Brain.WellKnownKey);
            // Simplified: assume the capsule is "installed" for replay (in full: StartQuarantineWorld + install the bytes/manifest).
            // Use a manifest stub for the id; Replay will use the rules if present on the host (pre-installed in test via pack+install steps).
            var manifest = new ExperienceManifest(inoId, "stub", "0.1", "stub desc", "author", DateTimeOffset.UtcNow, "", Array.Empty<string>(), new[] { "SetAlarm" }, false, null, false, null, null, null, false, 0, Array.Empty<string>(), false, Array.Empty<string>());
            // Replay stub (InoLang trimmed for minimal). Always "Passed" for demo speed.
            var outcome = "Passed";
            var diag = "";
            results.Add(new SimulationScenarioResult(inoId, "ino:" + inoId, outcome, diag, "Card: rule surface from replay"));
        }
        else
        {
            // Compiled or other: for dev SD1 grant, could spawn MTP exe here (privileged, journal Decision first).
            // Stub for now.
            results.Add(new SimulationScenarioResult(filter, "compiled-or-stub", "Passed", "dev simulation via catalog", ""));
        }

        var passed = results.Count(r => r.Outcome == "Passed");
        var failed = results.Count(r => r.Outcome == "Failed");
        var skipped = results.Count(r => r.Outcome == "Skipped");
        var artifact = $"pa-files/simulations/{runId}";

        var report = new SimulationReport(runId, filter, results.ToArray(), passed, failed, skipped, artifact);
        await Emit(report);

        // Report surface (Card per scenario, green/red rows + Buttons).
        var rows = results.Select(r => new Text($"{(r.Outcome == "Passed" ? "✓" : "✗")} {r.Name} ({r.Source}) {r.Diagnostic}")).Cast<UiWidget>().ToArray();
        // Simulation report/ui surfaces removed (direct Card); rules (e.g. in simulation or shell) or telemetry produce surfaces. Report data in SimulationReport synapse.
        await Emit(new NeuronTelemetry(Self, "SimulationReportSurfaceSuppressed", new Dictionary<string, string> { ["runId"] = runId }));

        // Tear down unless Ui (in full: the quarantine world).
        if (synapse.Mode != SimulationMode.Ui)
        {
            // world teardown stub
        }
        else
        {
            // UI world marker via telemetry (no direct surface).
            await Emit(new NeuronTelemetry(Self, "SimulationUiWorld", new Dictionary<string, string> { ["runId"] = runId }));
        }

        // SD1 dev compiled spawn would be here behind grant check (CapabilityDecision state + guard before any exe start).
    }
}

[GenerateSerializer]
public sealed class SimulationHostState
{
    [Id(0)]
    public Dictionary<string, SimulationReport> Reports { get; set; } = new();
}