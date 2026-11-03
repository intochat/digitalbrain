using DigitalBrain.Access;
using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Connections;

// One per brain: the keys of its connection neurons, nothing else.
// A plain neuron (06-08), the brain's singleton "{brain}/main" (R4-19). [Internal]: module code reaches it, no edge caller does,
// and the script contract catalog leaves it out, as it did when it was no neuron.
[NeuronAlias("connection.index"), DefaultNeuronType(TypeName)]
[Internal]
public interface IConnectionIndex
{
    const string TypeName = "connection.index";
    static string Key(string brainId) => $"{brainId}/main";
    static NeuronId Id(string brainId) => new(new BrainId(brainId), TypeName, "main");

    Task<string[]> List();
    Task Track(string connectionKey);
    Task Untrack(string connectionKey);
}

// Internal notification port on the public catalog neuron. One-way avoids a cycle when a
// catalog read is already awaiting the connection that has just changed.
// A plain contract of the brain's connection requests neuron "{brain}/main" (06-08).
[NeuronAlias("integrations.catalog-updates"), DefaultNeuronType(TypeName)]
[Internal]
public interface IConnectionCatalogUpdates
{
    const string TypeName = "integrations.accounts";
    static NeuronId Id(string brainId) => new(new BrainId(brainId), TypeName, "main");

    [OneWayCall] Task Changed(string connectionKey);
}

// A seam for facts that fault a connection's persistence (06-08): a plain neuron's state store is the runtime's BrainLog, which
// facts cannot wrap as they wrapped the grain store, so ConnectionNeuron reports each write here. No production silo registers it.
public interface IConnectionWriteFaults
{
    // The state the connection activated with (its last committed state).
    void Loaded(string connectionKey, object state);

    // Before a write: next is the state to save, or null for a publish without a save. Throwing fails the write.
    Task BeforeWrite(string connectionKey, object? next, IReadOnlyList<Signal> signals);

    // After the write committed. Throwing models a lost acknowledgement.
    Task AfterWrite(string connectionKey, object? next, IReadOnlyList<Signal> signals);
}
