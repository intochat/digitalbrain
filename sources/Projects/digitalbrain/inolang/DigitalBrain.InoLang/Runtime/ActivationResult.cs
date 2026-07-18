namespace DigitalBrain.InoLang.Runtime;

// v5 C2: Port is the *local* emit-port name (`!x` from `using !x = synapse(T)`),
// NOT the broadcast contract FQN. The Interpreter has no FQN at emit time
// without re-linking; the runtime resolves Port → FQN via
// ExecutionPlan.SynapsePorts at fan-out time. Keep this in mind when authoring
// a new EmittedSynapse consumer — assuming FQN here silently misroutes.
public sealed record EmittedSynapse(string Port, IReadOnlyDictionary<string, string> Args);

public sealed class ActivationResult
{
    public List<EmittedSynapse> EmittedSynapses { get; } = [];
    public Dictionary<string, string> SavedResources { get; } = [];
    public Dictionary<string, long> Counters { get; } = [];
    public List<string> Logs { get; } = [];
}
