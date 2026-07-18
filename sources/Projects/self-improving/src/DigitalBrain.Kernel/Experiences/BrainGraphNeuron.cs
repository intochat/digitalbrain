using DigitalBrain.Protocol;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;

namespace DigitalBrain.Kernel.Experiences;

public interface IBrainGraphNeuron : INeuron,
    IHandle<BundleInstalled>,
    IHandle<UiSurface>
{
    Task PingAsync(CancellationToken cancellationToken = default);
}

[GrainType("brain-graph")]
public sealed class BrainGraphNeuron : Neuron, IBrainGraphNeuron
{
    private readonly List<GraphNode> _nodes = new();
    private readonly List<GraphEdge> _edges = new();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        SeedCoreGraph();
        await EmitGraphSurfaceAsync(cancellationToken);
    }

    public async Task HandleAsync(BundleInstalled bundleInstalled, CancellationToken cancellationToken)
    {
        var id = bundleInstalled.BundleId.Value;
        if (_nodes.All(n => n.Id != id))
        {
            _nodes.Add(new GraphNode(id, id, "bundle"));
            _edges.Add(new GraphEdge("shell", id, "hosts"));
            await EmitGraphSurfaceAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(UiSurface surface, CancellationToken cancellationToken)
    {
        // live update: add emitters as nodes so the 3D reflects current brain activity (neurons that emit UI)
        var id = surface.Emitter.ToString();
        if (_nodes.All(n => n.Id != id))
        {
            _nodes.Add(new GraphNode(id, id.Split('/').LastOrDefault() ?? id, "neuron"));
            if (!_edges.Any(e => e.SourceId == "shell" && e.TargetId == id))
                _edges.Add(new GraphEdge("shell", id, "emits-ui"));
            await EmitGraphSurfaceAsync(cancellationToken);
        }
    }

    public Task PingAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private void SeedCoreGraph()
    {
        _nodes.Clear();
        _edges.Clear();

        _nodes.Add(new GraphNode("shell", "ShellNeuron", "core"));
        _nodes.Add(new GraphNode("kerneltasks", "KernelTaskSupervisor", "tasks"));
        _nodes.Add(new GraphNode("marketplace", "MarketplaceNeuron", "market"));
        _nodes.Add(new GraphNode("creator", "CreatorNeuron", "creation"));
        _nodes.Add(new GraphNode("llm-agent", "LlmAgentNeuron", "agent"));
        _nodes.Add(new GraphNode("memory", "MemoryNeuron", "memory"));
        _nodes.Add(new GraphNode("weather-watcher", "WeatherWatcher", "env"));
        _nodes.Add(new GraphNode("brain-graph", "BrainGraphNeuron", "viz"));
        _nodes.Add(new GraphNode("google-auth", "GoogleAuthNeuron", "auth"));

        _edges.Add(new GraphEdge("shell", "kerneltasks", "handles"));
        _edges.Add(new GraphEdge("shell", "marketplace", "composes"));
        _edges.Add(new GraphEdge("marketplace", "creator", "installs"));
        _edges.Add(new GraphEdge("shell", "llm-agent", "routes"));
        _edges.Add(new GraphEdge("llm-agent", "memory", "recalls"));
        _edges.Add(new GraphEdge("shell", "brain-graph", "emits"));
    }

    private async Task EmitGraphSurfaceAsync(CancellationToken cancellationToken)
    {
        var graph = new Graph3D(_nodes.ToArray(), _edges.ToArray());
        var surf = new UiSurface("brain-graph-3d", Self, graph);
        await Emit(surf);
        SurfaceStreamService.Publish(SurfaceStreamService.ToMessage(surf));
    }
}