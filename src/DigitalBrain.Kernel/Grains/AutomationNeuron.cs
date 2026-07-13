using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

using DigitalBrain.Ui.Contracts;

[GrainType("digitalbrain.automation.v1")]
public class AutomationNeuron(ILogger<AutomationNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IAutomationNeuron
{
    private Dictionary<string, string> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private List<RegisterReaction> _reactions = [];

    protected override bool ShouldSubscribeToTimeline => true;

    public override async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        EnsureProjections();
    }

    protected override async Task DispatchSynapse(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProjections();

        switch (synapse)
        {
            case RegisterScript rs:
                _scripts[rs.Id] = rs.Code;
                await FireAsync(new Signal("ScriptRegistered", new Dictionary<string, object?> { ["id"] = rs.Id }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case RegisterReaction rr:
                _reactions.Add(rr);
                await FireAsync(new Signal("ReactionRegistered", new Dictionary<string, object?> { ["id"] = rr.Id, ["when"] = rr.When }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case AutomationApp app:
                foreach (var s in app.Scripts ?? Array.Empty<RegisterScript>())
                {
                    _scripts[s.Id] = s.Code;
                }

                foreach (var r in app.Reactions ?? Array.Empty<RegisterReaction>())
                {
                    _reactions.Add(r);
                }

                await FireAsync(new Signal("AutomationAppRegistered", new Dictionary<string, object?> { ["appId"] = app.AppId }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case CreateAutomationApp create:
                if (create.Scripts is not null)
                {
                    foreach (var s in create.Scripts)
                    {
                        _scripts[s.Id] = s.Code;
                    }
                }

                if (create.Reactions is not null)
                {
                    foreach (var r in create.Reactions)
                    {
                        _reactions.Add(r);
                    }
                }

                await FireAsync(new Signal("AutomationAppRegistered", new Dictionary<string, object?> { ["appId"] = create.AppId }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case RemoveReaction rm:
                _reactions.RemoveAll(r => r.Id == rm.Id);
                await FireAsync(new Signal("ReactionRemoved", new Dictionary<string, object?> { ["id"] = rm.Id }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
        }
    }

    private void EnsureProjections()
    {
        if (_scripts.Count > 0 || _reactions.Count > 0)
        {
            return;
        }

        foreach (var s in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterScript>())
        {
            _scripts[s.Id] = s.Code;
        }

        var removes = new HashSet<string>();
        foreach (var rm in OutgoingJournal.Concat(IncomingJournal).OfType<RemoveReaction>())
        {
            removes.Add(rm.Id);
        }

        foreach (var r in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterReaction>())
        {
            if (!removes.Contains(r.Id))
            {
                _reactions.Add(r);
            }
        }

        foreach (var a in OutgoingJournal.Concat(IncomingJournal).OfType<AutomationApp>())
        {
            if (a.Scripts != null)
            {
                foreach (var s in a.Scripts)
                {
                    _scripts[s.Id] = s.Code;
                }
            }

            if (a.Reactions != null)
            {
                foreach (var r in a.Reactions)
                {
                    if (!removes.Contains(r.Id))
                    {
                        _reactions.Add(r);
                    }
                }
            }
        }

    }

    public async Task<IReadOnlyList<string>> ListActiveScriptsAsync()
    {
        EnsureProjections();
        await EmitAutomationsSurfaceAsync();
        return _scripts.Keys.ToList();
    }

    public async Task<IReadOnlyList<string>> ListActiveReactionsAsync()
    {
        EnsureProjections();
        await EmitAutomationsSurfaceAsync();
        return _reactions.Select(r => r.Id).ToList();
    }

    public async Task DefineReactionAsync(string id, string when, string? target, string scriptCode, IReadOnlyList<string>? declaredEmits = null, CancellationToken cancellationToken = default)
    {
        var scriptId = id + "-script";
        await FireAsync(new RegisterScript(scriptId, scriptCode, "defined-via-DefineReaction", Array.Empty<string>(), "default"), cancellationToken);
        await FireAsync(new RegisterReaction(id, when, scriptId, target ?? string.Empty, declaredEmits ?? Array.Empty<string>(), "default", null), cancellationToken);
    }

    public Task<string?> GetScriptCodeAsync(string id)
    {
        EnsureProjections();
        _scripts.TryGetValue(id, out var code);
        return Task.FromResult(code);
    }

    public async Task RemoveReactionAsync(string id)
    {
        EnsureProjections();
        _reactions.RemoveAll(r => r.Id == id);
        await FireAsync(new RemoveReaction(id));
        await EmitAutomationsSurfaceAsync();
    }

    public Task<IReadOnlyList<ScriptLibraryEntry>> ListScriptLibraryAsync()
    {
        EnsureProjections();
        var entries = new List<ScriptLibraryEntry>();
        var usage = _reactions.GroupBy(r => r.ScriptRef).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in _scripts)
        {
            entries.Add(new ScriptLibraryEntry(kv.Key, kv.Value, "shared library script", Array.Empty<string>(), usage.GetValueOrDefault(kv.Key)));
        }
        return Task.FromResult<IReadOnlyList<ScriptLibraryEntry>>(entries);
    }

    private async Task EmitAutomationsSurfaceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProjections();
        var now = DateTimeOffset.UtcNow;

        var reactionViews = _reactions
            .Select(r => new ReactionView(r.Id, r.When, r.ScriptRef, r.Target, 0))
            .ToList();

        var scriptViews = _scripts
            .Select(kv =>
            {
                var used = _reactions.Count(rr => rr.ScriptRef == kv.Key);
                return new ScriptView(kv.Key, "library script", kv.Value.Length > 60 ? kv.Value[..60] + "..." : kv.Value, used);
            })
            .ToList();

        await FireAsync(new AutomationSurface(reactionViews, scriptViews, now), cancellationToken);

        await EmitAutomationGraphSurfaceAsync(reactionViews, scriptViews, now, cancellationToken);

        var reactionItems = _reactions.Any()
            ? _reactions.Select(r => $"{r.Id}: when {r.When} -> {r.ScriptRef}").ToList()
            : ["No active reactions. Define via MCP or synapses."];
        await FireAsync(new ListSurface("Active Reactions", reactionItems), cancellationToken);
    }

    private async Task EmitAutomationGraphSurfaceAsync(IReadOnlyList<ReactionView> reactions, IReadOnlyList<ScriptView> scripts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var nodes = new List<AutomationGraphNode>();
        var edges = new List<AutomationGraphEdge>();
        foreach (var s in scripts)
        {
            nodes.Add(new AutomationGraphNode(s.Id, "script", s.Id, new Dictionary<string, object?> { ["preview"] = s.CodePreview }));
        }

        foreach (var r in reactions)
        {
            nodes.Add(new AutomationGraphNode(r.Id, "reaction", $"{r.Id} ({r.When})", new Dictionary<string, object?> { ["when"] = r.When, ["target"] = r.Target }));
            if (!string.IsNullOrEmpty(r.ScriptRef))
            {
                edges.Add(new AutomationGraphEdge(r.Id, r.ScriptRef, "uses-script"));
            }

            edges.Add(new AutomationGraphEdge("timeline", r.Id, $"when:{r.When}"));
        }
        await FireAsync(new AutomationGraphSurface("Automations Graph", nodes, edges, now), cancellationToken);
    }
}
