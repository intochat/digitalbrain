using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

using DigitalBrain.Ui.Contracts;

/// The reactive host for lightweight automations (reactions + scripts).
/// Always subscribes to the global timeline so it can react to NeuronActivated,
/// Signals, and other synapses without requiring static IHandle<> declarations.
/// Definitions are stored purely in the durable journals (source of truth).
/// "Apps" and reactions are hot: register -> immediately active for future matches.
[GrainType("digitalbrain.automation.v1")]
public class AutomationNeuron(ILogger<AutomationNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IAutomationNeuron
{
    private Dictionary<string, string> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private List<RegisterReaction> _reactions = new();
    private Dictionary<string, int> _execCounts = new(StringComparer.OrdinalIgnoreCase);

    protected override bool ShouldSubscribeToTimeline => true;

    public override async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        EnsureProjections();
        await TryExecuteMatchingAsync(item);
    }

    protected override async Task DispatchSynapse(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProjections();

        // Handle registrations first (they update our live view)
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
                    _scripts[s.Id] = s.Code;
                foreach (var r in app.Reactions ?? Array.Empty<RegisterReaction>())
                    _reactions.Add(r);
                await FireAsync(new Signal("AutomationAppRegistered", new Dictionary<string, object?> { ["appId"] = app.AppId }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case CreateAutomationApp create:
                if (create.Scripts is not null)
                    foreach (var s in create.Scripts) _scripts[s.Id] = s.Code;
                if (create.Reactions is not null)
                    foreach (var r in create.Reactions) _reactions.Add(r);
                await FireAsync(new Signal("AutomationAppRegistered", new Dictionary<string, object?> { ["appId"] = create.AppId }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case RemoveReaction rm:
                _reactions.RemoveAll(r => r.Id == rm.Id);
                await FireAsync(new Signal("ReactionRemoved", new Dictionary<string, object?> { ["id"] = rm.Id }), cancellationToken);
                await EmitAutomationsSurfaceAsync(cancellationToken);
                return;
            case PromoteAutomationToPack promo:
                await HandlePromoteAsync(promo, cancellationToken);
                return;
        }

        await TryExecuteMatchingAsync(synapse, cancellationToken);
    }

    private async Task TryExecuteMatchingAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matches = _reactions.Where(r => IsMatch(r, synapse)).ToList();
        if (matches.Count == 0) return;

        foreach (var reaction in matches)
        {
            if (!_scripts.TryGetValue(reaction.ScriptRef, out var code) &&
                !reaction.ScriptRef.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("Script ref '{Ref}' not found for reaction {ReactionId}", reaction.ScriptRef, reaction.Id);
                continue;
            }

            code ??= reaction.ScriptRef;

            Logger.LogInformation("Automation executing reaction {ReactionId} (when={When}) with script {ScriptRef}", reaction.Id, reaction.When, reaction.ScriptRef);

            _execCounts[reaction.Id] = _execCounts.GetValueOrDefault(reaction.Id) + 1;

            var caps = ServiceProvider.GetService<ICapabilityBroker>();
            var outputs = await Foundry.ScriptRunner.ExecuteAsync(
                code,
                synapse,
                Self,
                s => FireAsync(StampCurrent(s), cancellationToken),
                caps);

            // Light declared-emits enforcement (plan Task 9)
            if (reaction.DeclaredEmits != null && reaction.DeclaredEmits.Count > 0)
            {
                foreach (var o in outputs)
                {
                    if (!reaction.DeclaredEmits.Any(d => d == o.Type || d == "*"))
                    {
                        Logger.LogWarning("Reaction {Id} emitted undeclared type {Type} (declared: {Declared})", reaction.Id, o.Type, string.Join(",", reaction.DeclaredEmits));
                    }
                }
            }

            foreach (var output in outputs)
            {
                await FireAsync(StampCurrent(output), cancellationToken);
            }

            // Minimal run ledger entry (P1). Persisted via journal.
            await FireAsync(StampCurrent(new AutomationRun(reaction.Id, null, _execCounts[reaction.Id], "completed", DateTimeOffset.UtcNow)), cancellationToken);

            await EmitAutomationsSurfaceAsync(cancellationToken);
        }
    }

    private static bool IsMatch(RegisterReaction r, Synapse s)
    {
        if (string.IsNullOrEmpty(r.When) || r.When == "*")
            return TargetMatches(r.Target, s) && ScopeMatches(r.Scope, s);

        if (r.When.Equals("NeuronActivated", StringComparison.OrdinalIgnoreCase) && s is NeuronActivated na)
            return TargetMatches(r.Target, na) && ScopeMatches(r.Scope, na);

        if (r.When.StartsWith("Signal:", StringComparison.OrdinalIgnoreCase) && s is Signal sig)
        {
            var wanted = r.When["Signal:".Length..];
            return sig.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) && TargetMatches(r.Target, s) && ScopeMatches(r.Scope, s);
        }

        if (r.When.Equals(s.Type, StringComparison.OrdinalIgnoreCase))
            return TargetMatches(r.Target, s) && ScopeMatches(r.Scope, s);

        return false;
    }

    private static bool TargetMatches(string? target, Synapse s)
    {
        if (string.IsNullOrWhiteSpace(target)) return true;
        if (s is NeuronActivated na)
            return na.Neuron.Value.Contains(target.Trim('*'), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool ScopeMatches(string scope, Synapse s)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope == "default") return true;
        // Coordinate with NeuronScope: for activation, match user prefix of neuron id; for signals look for user in Props
        if (s is NeuronActivated na && NeuronScope.TryParse(na.Neuron.Value, out var sc))
            return string.Equals(sc.UserId.Value, scope, StringComparison.OrdinalIgnoreCase) || sc.UserId.Value == "default";
        if (s is Signal sig && sig.Props != null && sig.Props.TryGetValue("userId", out var u) && u is string us)
            return string.Equals(us, scope, StringComparison.OrdinalIgnoreCase);
        return true; // default loose for compat
    }

    private void EnsureProjections()
    {
        if (_scripts.Count > 0 || _reactions.Count > 0) return;

        // Replay from journals (exact pattern used by MarketplaceNeuron and GeneratedNeuron)
        foreach (var s in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterScript>())
            _scripts[s.Id] = s.Code;

        var removes = new HashSet<string>();
        foreach (var rm in OutgoingJournal.Concat(IncomingJournal).OfType<RemoveReaction>())
            removes.Add(rm.Id);

        foreach (var r in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterReaction>())
        {
            if (!removes.Contains(r.Id))
                _reactions.Add(r);
        }

        foreach (var a in OutgoingJournal.Concat(IncomingJournal).OfType<AutomationApp>())
        {
            if (a.Scripts != null) foreach (var s in a.Scripts) _scripts[s.Id] = s.Code;
            if (a.Reactions != null)
            {
                foreach (var r in a.Reactions)
                    if (!removes.Contains(r.Id)) _reactions.Add(r);
            }
        }

        // counts stay zero on replay; execution increments live
    }

    public async Task<IReadOnlyList<string>> ListActiveScriptsAsync()
    {
        EnsureProjections();
        await EmitAutomationsSurfaceAsync(); // refresh surface on query
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
        // Low-level / internal only. Public entry points (MCP define_reaction, Ino chat-to-automation, etc.)
        // must stage a SelfEvolutionProposal first (see AutomationDefinitionApplyHandler and MCP tools).
        // Direct calls bypass the approval rail and are only for trusted bootstrap or internal apply handlers.
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

    public async Task PromoteToPackAsync(string packName, string version, IReadOnlyList<string> reactionIds, string? ownerId = null)
    {
        await FireAsync(new PromoteAutomationToPack(packName, version, reactionIds, ownerId));
    }

    private async Task HandlePromoteAsync(PromoteAutomationToPack promo, CancellationToken cancellationToken)
    {
        EnsureProjections();
        var selected = _reactions.Where(r => promo.ReactionIds.Contains(r.Id)).ToList();
        // Thin manifest/source stub consumable by pack pipeline / seeds (real pack would synthesize full IPackBehavior + publish).
        // Flow example: MCP promote_automations_to_pack -> this emits AutomationPromoted + Signal("AutomationCrystallized") with stub -> CodeFoundry or marketplace seeds can consume.
        var summary = $"Promoted {selected.Count} reactions to {promo.PackName}@{promo.Version}. Reactions: {string.Join(",", selected.Select(r => r.Id))}";
        var stubCode = $"// Auto-crystallized from automations\n// Reactions: {string.Join(", ", selected.Select(r => r.Id))}\npublic sealed class {promo.PackName}Pack : DigitalBrain.Core.Distribution.IPackBehavior {{ /* TODO: implement from scripts */ public string Respond(string i) => i; }}";
        await FireAsync(new AutomationPromoted(promo.PackName, promo.Version, summary), cancellationToken);
        // Fire a signal carrying the stub so CodeFoundry or marketplace can pick it up
        await FireAsync(new Signal("AutomationCrystallized", new Dictionary<string, object?> { ["pack"] = promo.PackName, ["code"] = stubCode }), cancellationToken);
    }

    public Task<IReadOnlyList<ScriptLibraryEntry>> ListScriptLibraryAsync()
    {
        EnsureProjections();
        var entries = new List<ScriptLibraryEntry>();
        var usage = _reactions.GroupBy(r => r.ScriptRef).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in _scripts)
        {
            // Note: full RegisterScript metadata not stored separately; use defaults + usage
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
            .Select(r => new ReactionView(r.Id, r.When, r.ScriptRef, r.Target, _execCounts.GetValueOrDefault(r.Id)))
            .ToList();

        var scriptViews = _scripts
            .Select(kv =>
            {
                var used = _reactions.Count(rr => rr.ScriptRef == kv.Key);
                return new ScriptView(kv.Key, "library script", kv.Value.Length > 60 ? kv.Value[..60] + "..." : kv.Value, used);
            })
            .ToList();

        await FireAsync(new AutomationSurface(reactionViews, scriptViews, now), cancellationToken);

        // Visual graph foundation (data-only; future UI consumes + emits Register* back)
        await EmitAutomationGraphSurfaceAsync(reactionViews, scriptViews, now, cancellationToken);

        // Also keep lightweight list for timeline consumers that expect ListSurface
        var reactionItems = _reactions.Any()
            ? _reactions.Select(r => $"{r.Id}: when {r.When} -> {r.ScriptRef}").ToList()
            : new List<string> { "No active reactions. Define via MCP or synapses." };
        await FireAsync(new ListSurface("Active Reactions", reactionItems), cancellationToken);
    }

    private async Task EmitAutomationGraphSurfaceAsync(IReadOnlyList<ReactionView> reactions, IReadOnlyList<ScriptView> scripts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var nodes = new List<AutomationGraphNode>();
        var edges = new List<AutomationGraphEdge>();
        foreach (var s in scripts)
            nodes.Add(new AutomationGraphNode(s.Id, "script", s.Id, new Dictionary<string, object?> { ["preview"] = s.CodePreview }));
        foreach (var r in reactions)
        {
            nodes.Add(new AutomationGraphNode(r.Id, "reaction", $"{r.Id} ({r.When})", new Dictionary<string, object?> { ["when"] = r.When, ["target"] = r.Target }));
            if (!string.IsNullOrEmpty(r.ScriptRef))
                edges.Add(new AutomationGraphEdge(r.Id, r.ScriptRef, "uses-script"));
            edges.Add(new AutomationGraphEdge("timeline", r.Id, $"when:{r.When}"));
        }
        await FireAsync(new AutomationGraphSurface("Automations Graph", nodes, edges, now), cancellationToken);
    }
}
