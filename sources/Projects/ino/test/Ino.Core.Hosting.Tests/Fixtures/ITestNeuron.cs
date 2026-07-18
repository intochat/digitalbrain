using Ino.Core;
using Ino.Core.Hosting;
using Orleans;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Grain interface for the test neuron. Exposes the minimum surface needed by Phase 1
/// integration tests: apply an event, list events, count them.
///
/// In Phase 2 this is replaced by the INeuron&lt;T&gt; canonical dispatch path — for
/// Phase 1 we need an explicit grain interface so the test cluster can resolve a grain
/// by key and invoke methods on it directly.
///
/// Extends <see cref="IJournaledNeuronQuery"/> so Phase 1 integration tests can exercise
/// the non-generic journal lookup (<see cref="IJournaledNeuronQuery.FindEventAsync"/>)
/// directly through the grain reference.
/// </summary>
public interface ITestNeuron : IGrainWithStringKey, IJournaledNeuronQuery
{
    Task ApplyEventAsync(TestEvent @event, string correlationId);

    /// <summary>
    /// Raise an event building a <see cref="NeuronContext"/> from the supplied causation
    /// fields inside the grain. Returns the W3C activity id that the context surfaced as
    /// <c>CurrentActivity.Id</c>, so the test can assert that the stored envelope's
    /// <c>TraceParent</c> matches. Lets Phase 1 integration tests verify that
    /// <c>Neuron{TEvent}.RaiseAsync</c> copies <c>CurrentEventId</c>, <c>SourceStream</c>,
    /// <c>CorrelationId</c>, and <c>CurrentActivity.Id</c> into the stored envelope.
    ///
    /// Accepts primitives (not a <c>NeuronContext</c> instance) because <see cref="NeuronContext"/>
    /// carries non-serializable fields (<c>ILogger</c>, <c>Activity</c>) that Orleans cannot
    /// marshal across grain boundaries.
    /// </summary>
    Task<string?> RaiseViaContextAsync(
        TestEvent @event,
        string currentEventId,
        string correlationId,
        string sourceStream,
        string? traceParent);

    Task<IReadOnlyList<TestEvent>> GetAllEventsAsync();

    /// <summary>
    /// Return the full journal history with envelope metadata. Thin pass-through to
    /// <see cref="Neuron{TEvent}.GetHistoryWithMetadataAsync(int)"/>.
    /// </summary>
    Task<IReadOnlyList<EventEnvelope<TestEvent>>> GetAllEnvelopesAsync();

    Task<int> GetEventCountAsync();

    Task<int> GetTotalDeltaAsync();

    Task<string?> GetLastTextAsync();
}
