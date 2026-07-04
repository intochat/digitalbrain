using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

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

    protected override bool ShouldSubscribeToTimeline => true;

    public override async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        EnsureProjections();
        await TryExecuteMatchingAsync(item);
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        EnsureProjections();

        // Handle registrations first (they update our live view)
        switch (synapse)
        {
            case RegisterScript rs:
                _scripts[rs.Id] = rs.Code;
                await FireAsync(new Signal("ScriptRegistered", new Dictionary<string, object?> { ["id"] = rs.Id }));
                return;
            case RegisterReaction rr:
                _reactions.Add(rr);
                await FireAsync(new Signal("ReactionRegistered", new Dictionary<string, object?> { ["id"] = rr.Id, ["when"] = rr.When }));
                return;
            case AutomationApp app:
                foreach (var s in app.Scripts ?? Array.Empty<RegisterScript>())
                    _scripts[s.Id] = s.Code;
                foreach (var r in app.Reactions ?? Array.Empty<RegisterReaction>())
                    _reactions.Add(r);
                await FireAsync(new Signal("AutomationAppRegistered", new Dictionary<string, object?> { ["appId"] = app.AppId }));
                return;
            case CreateAutomationApp create:
                if (create.Scripts is not null)
                    foreach (var s in create.Scripts) _scripts[s.Id] = s.Code;
                if (create.Reactions is not null)
                    foreach (var r in create.Reactions) _reactions.Add(r);
                await FireAsync(new Signal("AutomationAppRegistered", new Dictionary<string, object?> { ["appId"] = create.AppId }));
                return;
        }

        await TryExecuteMatchingAsync(synapse);
    }

    private async Task TryExecuteMatchingAsync(Synapse synapse)
    {
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

            var outputs = await Foundry.ScriptRunner.ExecuteAsync(
                code,
                synapse,
                Self,
                s => FireAsync(StampCurrent(s)).AsTask());

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
                await FireAsync(StampCurrent(output));
            }
        }
    }

    private static bool IsMatch(RegisterReaction r, Synapse s)
    {
        if (string.IsNullOrEmpty(r.When) || r.When == "*")
            return TargetMatches(r.Target, s);

        if (r.When.Equals("NeuronActivated", StringComparison.OrdinalIgnoreCase) && s is NeuronActivated na)
            return TargetMatches(r.Target, na);

        if (r.When.StartsWith("Signal:", StringComparison.OrdinalIgnoreCase) && s is Signal sig)
        {
            var wanted = r.When["Signal:".Length..];
            return sig.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) && TargetMatches(r.Target, s);
        }

        if (r.When.Equals(s.Type, StringComparison.OrdinalIgnoreCase))
            return TargetMatches(r.Target, s);

        return false;
    }

    private static bool TargetMatches(string? target, Synapse s)
    {
        if (string.IsNullOrWhiteSpace(target)) return true;
        if (s is NeuronActivated na)
            return na.Neuron.Value.Contains(target.Trim('*'), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void EnsureProjections()
    {
        if (_scripts.Count > 0 || _reactions.Count > 0) return;

        // Replay from journals (exact pattern used by MarketplaceNeuron and GeneratedNeuron)
        foreach (var s in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterScript>())
            _scripts[s.Id] = s.Code;

        foreach (var r in OutgoingJournal.Concat(IncomingJournal).OfType<RegisterReaction>())
            _reactions.Add(r);

        foreach (var a in OutgoingJournal.Concat(IncomingJournal).OfType<AutomationApp>())
        {
            if (a.Scripts != null) foreach (var s in a.Scripts) _scripts[s.Id] = s.Code;
            if (a.Reactions != null) _reactions.AddRange(a.Reactions);
        }
    }

    public Task<IReadOnlyList<string>> ListActiveScriptsAsync()
    {
        EnsureProjections();
        return Task.FromResult((IReadOnlyList<string>)_scripts.Keys.ToList());
    }

    public Task<IReadOnlyList<string>> ListActiveReactionsAsync()
    {
        EnsureProjections();
        return Task.FromResult((IReadOnlyList<string>)_reactions.Select(r => r.Id).ToList());
    }

    public async Task DefineReactionAsync(string id, string when, string? target, string scriptCode, IReadOnlyList<string>? declaredEmits = null)
    {
        var scriptId = id + "-script";
        await FireAsync(new RegisterScript(scriptId, scriptCode, "defined-via-DefineReaction"));
        await FireAsync(new RegisterReaction(id, when, scriptId, target ?? string.Empty, declaredEmits ?? Array.Empty<string>()));
    }
}