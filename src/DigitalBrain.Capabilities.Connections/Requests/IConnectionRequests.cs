using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Connections.Requests;

// Request an account of a kind. The secret stays in the platform ring; the script holds a ConnectionRef.
// A plain neuron (06-08), the brain's singleton "{brain}/main" (R4-19).
[NeuronAlias("integrations.accounts"), DefaultNeuronType("integrations.accounts")]
public interface IConnectionRequests
{
    static string Key(string brainId) => $"{brainId}/main";

    [ReadOnlyCall] Task<ConnectorKind[]> ListKinds();

    [ReadOnlyCall] Task<ConnectionRef[]> ListPending();

    // Keyed by the current brain. Describes existing connections without opening credentials, in the brain's Settings order
    // (ConnectionSummary.Order): the order the capability broker resolves storage connections in (05-07, R4-02).
    [ReadOnlyCall] Task<ConnectionSummary[]> ListConnections();

    // Persists the brain's connection order: the listed keys first, in that order; connections not listed keep their relative
    // order after them, and new connections are appended. Unknown keys are ignored.
    Task Reorder(string[] keys);

    Task<ConnectionRef> Request(string kind);

    // The apps gallery's connections: every declared OAuth connector, whether this host offers it, and
    // whether this brain holds a live connection of it.
    [ReadOnlyCall] Task<ConnectorOffer[]> ListOffers();
}

// An opaque account. Kind and id name the credential the platform holds; the value never crosses this type.
public sealed record ConnectionRef(string Kind, string Id);

public sealed record ConnectorOffer(string Id, string DisplayName, bool Available, bool Connected);

public sealed record ConnectorKind(string Id, string DisplayName);

public sealed record ConnectionSummary(
    string Kind,
    string Id,
    string Neuron,
    string Status,
    string? Account,
    DateTimeOffset? LastProbedAt)
{
    // Id(6) was the 0.6 string Capabilities (connector-declared kinds); retired by 05-07, never reused (SAFE-01 clean break).

    // The capability ids ("storage@1") the connection's module provides (its ModuleManifest.Provides) and its neuron implements.
    public string[] Provides { get; init; } = [];

    // The connection's position in the brain's Settings order, from 0; new connections are appended.
    public int Order { get; init; }
}

[Signal("integrations.account-requested")]
public sealed record ConnectionRequested(string Kind, string Id) : Signal;

// Invalidation only: consumers re-read the catalog; no credential or connector detail is carried.
[Signal("integrations.connections-changed")]
public sealed record ConnectionsChanged(long Revision) : Signal;
