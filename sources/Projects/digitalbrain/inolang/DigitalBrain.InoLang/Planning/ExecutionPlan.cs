using DigitalBrain.InoLang.Ast;

namespace DigitalBrain.InoLang.Planning;

// v5 C2: the trigger taxonomy reflects the unified Synapse model.
//   Port       — handler fires when a local `using` port receives a synapse.
//   Broadcast  — handler subscribes to a contract FQN globally (no local port).
//   Lifecycle  — activated/deactivated/created.
//   Failure    — speculative branch rollback.
public enum TriggerCategory { Port, Broadcast, Lifecycle, Failure }

public readonly record struct TriggerKey(TriggerCategory Category, string Key)
{
    public static TriggerKey Port(string port) => new(TriggerCategory.Port, port);
    public static TriggerKey Broadcast(string fqn) => new(TriggerCategory.Broadcast, fqn);
    public static TriggerKey Lifecycle(string name) => new(TriggerCategory.Lifecycle, name);
    public static TriggerKey Failure(string branch) => new(TriggerCategory.Failure, branch);
}

public sealed record PlannedHandler(
    TriggerKey Key,
    Predicate? Where,
    IReadOnlyList<Stmt> Body);

// The runtime-side projection of a `using $port = neuron(Target)` /
// `using ~port = neuron(Target["key"])` declaration. TargetFqn selects the
// grain *implementation*; Key (when present) selects the keyed instance.
// Sigil is the dispatch discriminator: ProductionNeuronHost routes Call (`$`)
// to ICallNeuronTarget, Stream to IStreamNeuronTarget, Resource (`~`) to
// IResourceNeuronTarget, and Predicate (kernel-wired via host config, not by
// an InoLang `using` declaration) to IPredicateNeuronTarget — a mismatch
// between the binding's Sigil and the host method's contract throws so a
// silently-misrouted dispatch cannot mask a plan/source-of-truth defect.
public sealed record NeuronBinding(PortSigil Sigil, string TargetFqn, string? Key);

public sealed class ExecutionPlan
{
    readonly Dictionary<TriggerKey, List<PlannedHandler>> _byTrigger;

    public ExecutionPlan(
        string fqn,
        IReadOnlyList<PlannedHandler> handlers,
        IReadOnlyList<ScenarioDecl> scenarios,
        IReadOnlyList<string> counters,
        IReadOnlyDictionary<string, NeuronBinding> neurons,
        IReadOnlyDictionary<string, string>? synapsePorts,
        UiDecl? ui)
    {
        Fqn = fqn;
        Scenarios = scenarios;
        Counters = counters;
        Neurons = neurons;
        SynapsePorts = synapsePorts ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Ui = ui;
        _byTrigger = handlers
             .GroupBy(h => h.Key)
             .ToDictionary(g => g.Key, g => g.ToList());
    }

    public string Fqn { get; }
    public IReadOnlyList<ScenarioDecl> Scenarios { get; }
    public IReadOnlyList<string> Counters { get; }
    public IReadOnlyDictionary<string, NeuronBinding> Neurons { get; }
    public IReadOnlyDictionary<string, string> SynapsePorts { get; }
    public UiDecl? Ui { get; }
    public System.Collections.Generic.IReadOnlyCollection<PlannedHandler> AllHandlers => _byTrigger.Values.SelectMany(h => h).ToList();

    public IReadOnlyList<PlannedHandler> HandlersFor(TriggerKey key)
        => _byTrigger.TryGetValue(key, out var list) ? list : [];
}
