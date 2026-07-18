using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.InoLang.Domain.Ino;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.State;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

[GrainType("rule-host")]
public sealed class RuleHostNeuron : Neuron, IRuleHostNeuron
{
    private readonly IPersistentState<RuleHostState> _state;

    private const int MaxConsecutiveFaults = 3;

    public RuleHostNeuron(
        [PersistentState("rulehost", "Default")] IPersistentState<RuleHostState> state)
    {
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        if (_state.State.Rules == null)
            _state.State.Rules = new();

        var stream = this.GetStreamProvider(SynapseStream.ProviderName).Timeline();
        await stream.SubscribeAsync(async (item, token) =>
        {
            TryExecuteRulesFor(item);
            await Task.CompletedTask;
        });

        _state.State.FaultCounts ??= new();
    }

    public async Task InstallRulesAsync(string bundleIdValue, RuleSet ruleSet, CancellationToken cancellationToken = default)
    {
        _state.State.Rules[bundleIdValue] = ruleSet;
        await _state.WriteStateAsync(cancellationToken);
        await Emit(new NeuronTelemetry(Self, "RuleBundleInstalled", new Dictionary<string, string> { ["bundle"] = bundleIdValue }));
    }

    public async Task RemoveRuleSetAsync(string bundleIdValue, CancellationToken cancellationToken = default)
    {
        if (_state.State.Rules != null && _state.State.Rules.Remove(bundleIdValue))
        {
            await _state.WriteStateAsync(cancellationToken);
            await Emit(new NeuronTelemetry(Self, "RuleBundleRemoved", new Dictionary<string, string> { ["bundle"] = bundleIdValue }));
        }
    }

    private void TryExecuteRulesFor(Synapse item)
    {
        if (_state.State.Rules == null) return;

        foreach (var kv in _state.State.Rules)
        {
            var bundleIdValue = kv.Key;
            var rs = kv.Value;
            if (rs == null || rs.Declarations == null) continue;

            foreach (var rule in rs.Declarations)
            {
                try
                {
                    var intents = RuleInterpreter.Execute(rule, item);
                    if (intents.Length == 0) continue;

                    Emit(new RuleMatched(bundleIdValue, Array.IndexOf(rs.Declarations, rule), rule.On, item.CorrelationId)).GetAwaiter().GetResult();

                    foreach (var intent in intents)
                    {
                        if (intent is RuleInterpreter.EmitIntent ei)
                        {
                            if (IsPrivileged(ei.SynapseType) && !BundleHasGrant(bundleIdValue, ei.SynapseType))
                            {
                                Emit(new GrantRequested(bundleIdValue, new[] { ei.SynapseType })).GetAwaiter().GetResult();
                                continue;
                            }
                            var syn = SynapseBinder.TryCreate(ei.SynapseType, ei.Args);
                            if (syn != null)
                            {
                                var stamped = syn.Stamp(Self, item);
                                Emit(stamped).GetAwaiter().GetResult();
                            }
                        }
                        else if (intent is RuleInterpreter.ShowCardIntent sci)
                        {
                            var title = Substitute(sci.Title ?? "", item);
                            var widgets = BuildWidgets(sci, item);
                            var surfaceId = $"ui-def-{bundleIdValue}";
                            if (bundleIdValue == "weather-watcher") surfaceId = "weather";
                            if (bundleIdValue == "marketplace") surfaceId = "marketplace";
                            if (bundleIdValue == "kernel-tasks") surfaceId = "kerneltasks";
                            UiWidget shellRoot = bundleIdValue == "shell"
                                ? (widgets.Length == 1 ? widgets[0] : new Column(widgets))
                                : new Card(title, new Column(widgets));
                            var surface = new UiSurface(surfaceId, Self, shellRoot);
                            var stamped = surface.Stamp(Self, item);
                            Emit(stamped).GetAwaiter().GetResult();
                        }
                    }

                    var key = FaultKey(bundleIdValue, Array.IndexOf(rs.Declarations, rule));
                    _state.State.FaultCounts[key] = 0;
                }
                catch (Exception ex)
                {
                    var idx = Array.IndexOf(rs.Declarations, rule);
                    var key = FaultKey(bundleIdValue, idx);
                    _state.State.FaultCounts[key] = _state.State.FaultCounts.TryGetValue(key, out var c) ? c + 1 : 1;
                    Emit(new RuleFault(bundleIdValue, idx, ex.Message, item.SynapseId)).GetAwaiter().GetResult();

                    if (_state.State.FaultCounts[key] >= MaxConsecutiveFaults)
                    {
                        Emit(new RuleSuspended(bundleIdValue, idx)).GetAwaiter().GetResult();
                    }
                }
            }
        }
    }

    private static string FaultKey(string id, int idx) => $"{id}:{idx}";

    private static string Substitute(string s, Synapse item)
    {
        if (string.IsNullOrEmpty(s) || item == null || !s.Contains('$')) return s;
        var props = item.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var p in props)
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var val = p.GetValue(item)?.ToString() ?? "";
            s = s.Replace("$" + p.Name, val, StringComparison.OrdinalIgnoreCase);
            s = s.Replace("$" + char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1), val, StringComparison.OrdinalIgnoreCase);
        }
        return s;
    }

    private static UiWidget? BuildWidget(CardItem it, Synapse? item = null)
    {
        if (it.Kind == "text")
        {
            var t = Substitute(it.Text, item);
            return new Text(t);
        }
        if (it.Kind == "button")
        {
            var label = Substitute(it.Text, item);
            Synapse? onTap = null;
            if (it.Action != null)
            {
                var args = new Dictionary<string, object>();
                foreach (var kv in it.Action.Args)
                {
                    args[kv.Key] = Substitute(kv.Value, item);
                }
                onTap = SynapseBinder.TryCreate(it.Action.SynapseType, args);
            }
            return new Button(label, onTap);
        }
        if (it.Kind == "divider")
        {
            return new Divider();
        }
        if (it.Kind == "icon")
        {
            var name = Substitute(it.Text, item);
            return new Icon(name);
        }
        if (it.Kind == "textfield")
        {
            var textSub = Substitute(it.Text, item);
            var parts = textSub.Split('|');
            var label = parts[0];
            var val = parts.Length > 1 ? parts[1] : "";
            Synapse? onChanged = null;
            if (it.Action != null)
            {
                var args = new Dictionary<string, object>();
                foreach (var kv in it.Action.Args)
                {
                    args[kv.Key] = Substitute(kv.Value, item);
                }
                onChanged = SynapseBinder.TryCreate(it.Action.SynapseType, args);
            }
            return new TextField(label, val, onChanged);
        }
        if (it.Kind == "progress")
        {
            var textSub = Substitute(it.Text, item);
            var parts = textSub.Split('|');
            var label = parts[0];
            var valStr = parts.Length > 1 ? parts[1] : "0";
            double.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val);
            return new Progress(val, string.IsNullOrEmpty(label) ? null : label);
        }
        if (it.Kind == "toggle")
        {
            var textSub = Substitute(it.Text, item);
            var parts = textSub.Split('|');
            var label = parts[0];
            var valStr = parts.Length > 1 ? parts[1] : "false";
            bool.TryParse(valStr, out var val);
            Synapse? onChanged = null;
            if (it.Action != null)
            {
                var args = new Dictionary<string, object>();
                foreach (var kv in it.Action.Args)
                {
                    args[kv.Key] = Substitute(kv.Value, item);
                }
                onChanged = SynapseBinder.TryCreate(it.Action.SynapseType, args);
            }
            return new Toggle(label, val, onChanged);
        }
        if (it.Kind == "image")
        {
            var url = Substitute(it.Text, item);
            return new ImageWidget(url);
        }
        if (it.Kind == "column")
        {
            var children = it.Children != null
                ? it.Children.Select(ch => BuildWidget(ch, item)).Where(ch => ch != null).Cast<UiWidget>().ToArray()
                : Array.Empty<UiWidget>();
            return new Column(children);
        }
        if (it.Kind == "row")
        {
            var children = it.Children != null
                ? it.Children.Select(ch => BuildWidget(ch, item)).Where(ch => ch != null).Cast<UiWidget>().ToArray()
                : Array.Empty<UiWidget>();
            return new Row(children);
        }
        if (it.Kind == "container")
        {
            var textSub = Substitute(it.Text, item);
            var parts = textSub.Split('|');
            var padStr = parts[0];
            var deco = parts.Length > 1 ? parts[1] : null;
            double.TryParse(padStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pad);
            var child = it.Children != null && it.Children.Length > 0 ? BuildWidget(it.Children[0], item) : null;
            if (child != null)
            {
                return new Container((UiWidget)child, pad, string.IsNullOrEmpty(deco) ? null : deco);
            }
            return null;
        }
        if (it.Kind == "windowframe")
        {
            var textSub = Substitute(it.Text, item);
            var parts = textSub.Split('|');
            var title = parts[0];
            var windowId = parts.Length > 1 ? parts[1] : "";
            var child = it.Children != null && it.Children.Length > 0 ? BuildWidget(it.Children[0], item) : null;
            if (child != null)
            {
                return new WindowFrame(title, (UiWidget)child, windowId, 0, 0, 0, 0, 0, "");
            }
            return null;
        }

        return null;
    }

    private static UiWidget[] BuildWidgets(RuleInterpreter.ShowCardIntent sci, Synapse? item = null)
    {
        if (sci == null || sci.Items == null) return Array.Empty<UiWidget>();
        var list = new List<UiWidget>();
        foreach (var it in sci.Items)
        {
            var w = BuildWidget(it, item);
            if (w != null)
            {
                list.Add((UiWidget)w);
            }
        }
        return list.ToArray();
    }

    public Task HandleAsync(BundleInstalled synapse, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<RuleReplayReport> ReplayObservedSynapsesAsync(ExperienceManifest manifest, CancellationToken cancellationToken = default)
    {
        var id = manifest.Id;
        if (!_state.State.Rules.TryGetValue(id, out var rs) || rs?.Declarations == null || rs.Declarations.Length == 0)
            return Task.FromResult(new RuleReplayReport(id, Array.Empty<RuleMatched>(), Array.Empty<string>(), Array.Empty<RuleFault>()));

        var observed = (manifest.ObservedSynapses != null && manifest.ObservedSynapses.Length > 0)
            ? manifest.ObservedSynapses
            : rs.Declarations.Select(r => r.On).Distinct().ToArray();

        var matched = new List<RuleMatched>();
        var faults = new List<RuleFault>();
        var producedTypes = new List<string>();

        foreach (var typeName in observed)
        {
            var sample = typeName.Equals("SetAlarm", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, object> { ["Minutes"] = 10, ["Label"] = "standup" }
                : new Dictionary<string, object>();
            var syn = SynapseBinder.TryCreate(typeName, sample);
            if (syn == null) continue;

            foreach (var rule in rs.Declarations)
            {
                try
                {
                    var intents = RuleInterpreter.Execute(rule, syn);
                    if (intents.Length == 0) continue;

                    var idx = Array.IndexOf(rs.Declarations, rule);
                    matched.Add(new RuleMatched(id, idx, rule.On, syn.CorrelationId));

                    foreach (var intent in intents)
                    {
                        if (intent is RuleInterpreter.EmitIntent ei)
                        {
                            producedTypes.Add(ei.SynapseType);
                        }
                        else if (intent is RuleInterpreter.ShowCardIntent)
                        {
                            producedTypes.Add("UiSurface");
                        }
                    }
                }
                catch (Exception ex)
                {
                    var idx = Array.IndexOf(rs.Declarations, rule);
                    faults.Add(new RuleFault(id, idx, ex.Message, syn.SynapseId));
                }
            }
        }

        if (rs.Emits != null && rs.Emits.Length > 0)
            producedTypes.AddRange(rs.Emits);

        return Task.FromResult(new RuleReplayReport(id, matched.ToArray(), producedTypes.Distinct().ToArray(), faults.ToArray()));
    }

    private static readonly HashSet<string> Privileged = new(StringComparer.OrdinalIgnoreCase) { "SaveFileRequest", "GoogleApi", "WriteFile" };

    private static bool IsPrivileged(string type) => Privileged.Contains(type);

    private bool BundleHasGrant(string bundleId, string cap)
    {
        return false; // deny-by-default (security blocker); privileged rule emits (SaveFileRequest etc) now require explicit GrantDecision flow before RuleHost will emit the synapse. Real per-bundle ledger (manifest.RequiresGrant + decisions) to be centralized in follow-up PR.
    }
}

[GenerateSerializer]
public sealed class RuleHostState
{
    [Id(0)]
    public Dictionary<string, RuleSet> Rules { get; set; } = new();

    [Id(1)]
    public Dictionary<string, int> FaultCounts { get; set; } = new();
}
