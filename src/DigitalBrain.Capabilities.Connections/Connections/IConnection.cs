using System.Text;
using System.Text.Json.Serialization;
using DigitalBrain.Access;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Connections;

// The shell reads the status by name.
[JsonConverter(typeof(JsonStringEnumConverter<ConnectionStatus>))]
public enum ConnectionStatus
{
    Connected = 0,
    Expired = 1,
    Failing = 2,
    Disconnected = 3,
}

// The connector's edge neuron is the connection: keyed {brain}/connection/{connector}/{id}, it owns
// its credential as sealed state. The shell, the assistant and module tools talk to this face.
// A plain contract (06-08) that every connector's neuron implements, so it names no neuron type: the connection key's connector
// does (ConnectionGrains resolves it, R4-21(2)).
[NeuronAlias("connection"), ConnectionContract]
public interface IConnection
{
    [ReadOnlyCall] Task<ConnectionView> Read();
    Task<ConnectionView> Connect(string authMethodId, string value);
    Task<ConnectionView> Probe();
    Task Disconnect();

    // R147: a circle brain's copy of a shared connection: its definition, no credential, not connected (Task 28 seals one).
    [Internal] Task<ConnectionView> Adopt(string authMethodId, string? account);

    // R130: re-seals this connection's credential under the target brain's core part key, so a machine holding that
    // part opens it. No plaintext crosses this call: the returned secrets are ciphertext only.
    [Internal] Task<Dictionary<string, SealedSecret>> ExportFor(string brainId);

    // R130/R147: fills a shared copy's credential from secrets already sealed under this brain's core part key.
    [Internal] Task<ConnectionView> Import(string authMethodId, string? account, Dictionary<string, SealedSecret> secrets, ConnectionStatus status, string? detail);

    // R146: the owner's connection records an app of another brain it is shared into; that record is what the call
    // gate admits the app's use operations by (IConnectionBindings).
    // Phase F: a connection is shared with principals. ShareInto(brain, app) is ShareWith(AppPrincipal.Id(app)) for
    // an app of that brain, kept as an alias.
    [Internal] Task ShareInto(string brainId, string app);

    [Internal, Interleaves] Task<bool> IsSharedInto(string brainId, string app);

    // R155: the sharing is withdrawn (its sharer left the brain it was shared into).
    [Internal] Task UnshareFrom(string brainId, string app);

    [Internal] Task ShareWith(string principalId);

    [Internal, Interleaves] Task<bool> IsSharedWith(string principalId);

    [Internal] Task UnshareWith(string principalId);
}

// Reached only by the Platform OAuth callback, which stamps the owner recorded at login start. A plain contract (06-08) of every
// connection neuron, addressed by the connection key's connector like IConnection.
[NeuronAlias("connection.inbound"), ConnectionContract]
public interface IConnectionInbound
{
    Task<ConnectionView> AcceptGrant(OAuthGrant grant);
}

public sealed record ConnectionView(
    string ConnectorId,
    string ConnectionId,
    string AuthMethodId,
    ConnectionStatus Status,
    string? Account,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastProbedAt,
    string? Detail);

// The token response, parsed by the Platform. Target is what the user asked to connect (a repository
// URL, an instance); Response is the raw token JSON for the module's own parsing.
public sealed record OAuthGrant(
    string AuthMethodId,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string Scopes,
    string? Target,
    string Response)
{
#pragma warning disable CA1822
    private bool PrintMembers(StringBuilder builder) => builder.Append("<redacted>") is not null;
#pragma warning restore CA1822
}

// Opened secret, alive for one connector call inside the grain turn.
public sealed record Credential(string Value, string? Refresh)
{
#pragma warning disable CA1822
    private bool PrintMembers(StringBuilder builder) => builder.Append("<redacted>") is not null;
#pragma warning restore CA1822
}

public enum ConnectionProbeOutcome { Connected = 0, Expired = 1, Failing = 2 }

public sealed record ConnectionProbeResult(ConnectionProbeOutcome Outcome, string Detail)
{
    public static ConnectionProbeResult Connected(string detail = "The connector probe succeeded.") => new(ConnectionProbeOutcome.Connected, detail);
    public static ConnectionProbeResult Expired(string detail) => new(ConnectionProbeOutcome.Expired, detail);
    public static ConnectionProbeResult Failing(string detail) => new(ConnectionProbeOutcome.Failing, detail);
}

// Host-owned dispatch for an authenticated external delivery or persisted scheduled work.
// The host resolves identity from its authority; modules supply only the target brain and causal work.
public interface IBrainOwnerDispatch
{
    Task<bool> RunAsync(string brainId, string originKey, Func<Task> work, object? trace = null,
        CancellationToken cancellationToken = default);
}
