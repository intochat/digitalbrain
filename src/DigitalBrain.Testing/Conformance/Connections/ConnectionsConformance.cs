using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities.Connections.Requests;
using DigitalBrain.Capabilities.Storage;
using Xunit;

namespace DigitalBrain.Testing.Conformance.Connections;

/// <summary>A connected connections@1 implementation under test.</summary>
/// <param name="Connections">The capability connections broker surface.</param>
/// <param name="Requests">The connection requests surface for the test brain.</param>
/// <param name="Callers">Switches the runtime caller the implementation sees.</param>
public sealed record ConnectionsSubject(
    ICapabilityConnections Connections,
    IConnectionRequests Requests,
    CallerScope Callers)
{
    /// <summary>Optional seam to create/connect a connection for testing; null skips connection-backed facts.</summary>
    public Func<string, Task>? AddConnection { get; init; }

    /// <summary>The brain under test.</summary>
    public BrainId Brain { get; init; } = new("kit-brain");

    /// <summary>A connector kind offered by the subject.</summary>
    public string ConnectorKind { get; init; } = "kit-connector";

    /// <summary>A capability provided by <see cref="ConnectorKind"/>.</summary>
    public CapabilityId Capability { get; init; } = DigitalBrain.Capabilities.Capability.Storage.V1;

    /// <summary>Optional hook returning <see cref="ConnectionRequested"/> signals emitted for the test brain.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<ConnectionRequested>>>? RequestedSignals { get; init; }
}

/// <summary>
/// The connections@1 conformance kit. A module implementing connections subclasses it in its <c>tests/</c> project as a sealed
/// class and implements <see cref="Connect"/>; every inherited fact runs against that subject.
/// </summary>
/// <typeparam name="TConnections">The implementation under test (names the subclass's subject; not otherwise used).</typeparam>
public abstract class ConnectionsConformance<TConnections> where TConnections : ICapabilityConnections
{
    /// <summary>Every fact's timeout in milliseconds.</summary>
    protected const int Timeout = 30_000;

    /// <summary>Connects to the implementation. Called once per fact.</summary>
    protected abstract Task<ConnectionsSubject> Connect(CancellationToken ct);

    /// <summary>ListInOrder is empty for a new brain with no tracked connections.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ListInOrderIsEmptyForANewBrain()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var listed = await s.Connections.ListInOrder(s.Brain, ct).ConfigureAwait(false);
        Assert.Empty(listed);
    }

    /// <summary>Requesting a connector kind adds a pending reference returned by ListPending.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RequestThenListPendingContainsIt()
    {
        var (s, _) = await Open().ConfigureAwait(false);
        var requested = await s.Requests.Request(s.ConnectorKind).ConfigureAwait(false);
        Assert.Equal(s.ConnectorKind, requested.Kind);
        Assert.False(string.IsNullOrWhiteSpace(requested.Id));
        var pending = await s.Requests.ListPending().ConfigureAwait(false);
        Assert.Contains(requested, pending);
    }

    /// <summary>Adding a connection causes ListInOrder to include it.</summary>
    [Fact(Timeout = Timeout)]
    public async Task TrackThenListInOrderContainsTheConnection()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        Assert.SkipWhen(s.AddConnection is null, "the subject exposes no connection creation seam");
        var key = ConnectionKeys.For(s.Brain.Value, s.ConnectorKind, Unique("conn"));
        await s.AddConnection!(key).ConfigureAwait(false);
        var listed = await s.Connections.ListInOrder(s.Brain, ct).ConfigureAwait(false);
        Assert.Contains(listed, connection => connection.Key == key);
    }

    /// <summary>Reordering tracked connection keys is respected by ListInOrder.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ReorderIsRespectedByListInOrder()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        Assert.SkipWhen(s.AddConnection is null, "the subject exposes no connection creation seam");
        var first = ConnectionKeys.For(s.Brain.Value, s.ConnectorKind, Unique("first"));
        var second = ConnectionKeys.For(s.Brain.Value, s.ConnectorKind, Unique("second"));
        await s.AddConnection!(first).ConfigureAwait(false);
        await s.AddConnection(second).ConfigureAwait(false);
        await s.Requests.Reorder([second, first]).ConfigureAwait(false);
        var listed = await s.Connections.ListInOrder(s.Brain, ct).ConfigureAwait(false);
        Assert.Equal([second, first], listed.Select(connection => connection.Key).ToArray());
    }

    /// <summary>Binding an unknown connection key throws <see cref="KeyNotFoundException"/>.</summary>
    [Fact(Timeout = Timeout)]
    public async Task BindUnknownKeyThrowsNotFound()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var missing = ConnectionKeys.For(s.Brain.Value, s.ConnectorKind, Unique("missing"));
        await Assert.ThrowsAnyAsync<KeyNotFoundException>(
            () => s.Connections.Bind<IStorage>(s.Brain, missing, "kit-app", ct)).ConfigureAwait(false);
    }

    /// <summary>RequestConnection records a pending request and emits <see cref="ConnectionRequested"/>.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RequestConnectionRaisesConnectionRequested()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        await s.Connections.RequestConnection(s.Brain, s.Capability, "kit-app", ct).ConfigureAwait(false);
        var pending = await s.Requests.ListPending().ConfigureAwait(false);
        Assert.Contains(pending, request => request.Kind == s.ConnectorKind);
        if (s.RequestedSignals is not null)
        {
            var signals = await s.RequestedSignals(ct).ConfigureAwait(false);
            Assert.Contains(signals, signal => signal.Kind == s.ConnectorKind);
        }
    }

    private async Task<(ConnectionsSubject Subject, CancellationToken Ct)> Open()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = await Connect(ct).ConfigureAwait(false);
        Assert.NotNull(subject);
        return (subject, ct);
    }

    private static string Unique(string prefix) => $"kit-{prefix}-{Guid.NewGuid():N}";
}
