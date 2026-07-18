using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;

namespace DigitalBrain.Kernel;

[GenerateSerializer]
public sealed record GuideState
{
    [Id(0)]
    public string CurrentSection { get; set; } = "index";

    [Id(1)]
    public List<string> VisitedSections { get; set; } = new();
}

public interface IHexGuideNeuron : INeuron, IHandle<InstallBundle>, IHandle<GuideNavigate>, IHandle<GuideRequest> { }

[GrainType("hexguide")]
public sealed class HexGuideNeuron : Neuron, IHexGuideNeuron
{
    private readonly IPersistentState<GuideState> _guideState;

    public HexGuideNeuron(
        [PersistentState("hexguide", "Default")] IPersistentState<GuideState> guideState)
    {
        _guideState = guideState;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        var s = _guideState.State;
        if (string.IsNullOrWhiteSpace(s.CurrentSection))
        {
            s.CurrentSection = "index";
        }
    }

    public Task HandleAsync(InstallBundle synapse, CancellationToken cancellationToken)
    {
        if (string.Equals(synapse.BundleId, "hex1b-guide", StringComparison.OrdinalIgnoreCase))
        {
            EmitIndexSurface();
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    public async Task HandleAsync(GuideNavigate synapse, CancellationToken cancellationToken)
    {
        var sec = synapse.Section;
        var s = _guideState.State;
        s.CurrentSection = sec;
        if (!s.VisitedSections.Contains(sec))
        {
            s.VisitedSections.Add(sec);
        }
        await _guideState.WriteStateAsync(cancellationToken);
        EmitSectionSurface(sec);
    }

    public async Task HandleAsync(GuideRequest synapse, CancellationToken cancellationToken)
    {
        var sec = synapse.Section ?? "index";
        var state = _guideState.State;
        state.CurrentSection = sec;
        if (!state.VisitedSections.Contains(sec))
        {
            state.VisitedSections.Add(sec);
        }
        await _guideState.WriteStateAsync(cancellationToken);
        if (sec == "index")
        {
            EmitIndexSurface();
        }
        else
        {
            EmitSectionSurface(sec);
        }
    }

    private void EmitIndexSurface()
    {
        // Hex guide surfaces removed (direct); rule in os/hex-guide.ino on: HexGuideRequest produces show card.
        Emit(new NeuronTelemetry(Self, "HexGuideIndex", new Dictionary<string, string>()));
    }

    private void EmitSectionSurface(string sectionName)
    {
        // Section surfaces now from .ino rule (hex-guide.ino).
        Emit(new NeuronTelemetry(Self, "HexGuideSection", new Dictionary<string, string> { ["section"] = sectionName }));
    }
}