using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ino.Core;
using Ino.Core.Hosting;
using Orleans.Journaling;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Minimal neuron exercising the Neuron&lt;TEvent&gt; base class. Applies events via
/// RaiseAsync and exposes state-style projections computed on demand from the journal.
/// </summary>
public sealed class TestNeuron(
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<TestEvent>> journal)
    : Neuron<TestEvent>(journal), ITestNeuron
{
    public Task ApplyEventAsync(TestEvent @event, string correlationId)
    {
        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId(correlationId),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("test:fixture"))
        {
            FirePort = new NoOpFirePort(),
            Logger = NullLogger.Instance,
        };
        return RaiseAsync(@event, ctx);
    }

    /// <summary>
    /// Grain surface for the protected base-class <c>RaiseAsync(TEvent, NeuronContext)</c>.
    /// Builds a <see cref="NeuronContext"/> from primitives so the grain boundary stays
    /// serializable (<see cref="NeuronContext"/> itself carries <c>ILogger</c> and
    /// <c>Activity</c>), then invokes <c>RaiseAsync</c>. Lets integration tests assert
    /// that the four causation fields are copied into the stored envelope.
    /// </summary>
    public async Task<string?> RaiseViaContextAsync(
        TestEvent @event,
        string currentEventId,
        string correlationId,
        string sourceStream,
        string? traceParent)
    {
        Activity? activity = null;
        if (traceParent is not null)
        {
            // Stamp the activity with a parent id so Activity.Id becomes a derived
            // W3C id with the supplied traceparent as its root. For assertion, we
            // surface CurrentActivity.Id directly — that's what Neuron<T>.RaiseAsync
            // writes to EventEnvelope.TraceParent.
            activity = new Activity("raise-via-context");
            activity.SetParentId(traceParent);
            activity.Start();
        }

        try
        {
            var ctx = new NeuronContext(
                SynapseId: SynapseId.New(),
                CorrelationId: new CorrelationId(correlationId),
                Source: new Caller.Ambient(DomainId.From("kernel")),
                SourceStream: new StreamKey(sourceStream))
            {
                FirePort = new NoOpFirePort(),
                Logger = NullLogger.Instance,
                CurrentActivity = activity,
                CurrentEventId = new EventId(currentEventId),
            };

            await RaiseAsync(@event, ctx);
            return activity?.Id;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public Task<IReadOnlyList<TestEvent>> GetAllEventsAsync() =>
        GetHistoryAsync(int.MaxValue);

    public Task<IReadOnlyList<EventEnvelope<TestEvent>>> GetAllEnvelopesAsync() =>
        GetHistoryWithMetadataAsync(int.MaxValue);

    public Task<int> GetEventCountAsync() =>
        Task.FromResult(JournalCount);

    public async Task<int> GetTotalDeltaAsync()
    {
        var all = await GetHistoryAsync(int.MaxValue);
        return all.Sum(e => e.Delta);
    }

    public async Task<string?> GetLastTextAsync()
    {
        var all = await GetHistoryAsync(int.MaxValue);
        return all.Count == 0 ? null : all[^1].Text;
    }
}
