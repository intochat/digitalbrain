using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Orleans.Concurrency;

namespace DigitalBrain;

// The wire: the ONE grain interface into a neuron, plus the cached generic-method closers
// (the ForwarderFor pattern) that the drain uses to invoke it — DeliverAsync for the
// listener route, DeliverQuestionAsync for the answerer route. The envelope travels as
// RequestContext headers: the drain stages it (StageOutboundDelivery), the outgoing filter
// writes it just before the wire call, the incoming filter consumes it into the receiver
// (AcceptEnvelope), and the delivery methods here hand it to the turn entry. Reads are the
// sole interleaving surface and serve committed truth only.
public abstract partial class Neuron : Neuron.ITransport
{
    private static readonly ConcurrentDictionary<Type, Func<ITransport, Synapse, CancellationToken, Task>> WireDeliverers = new();
    private static readonly ConcurrentDictionary<Type, Func<ITransport, Synapse, CancellationToken, Task>> WireQuestionDeliverers = new();
    private static readonly ConcurrentDictionary<Type, Func<Neuron, Synapse, SynapseMetadata, CancellationToken, Task>> SelfDeliverers = new();
    private static readonly ConcurrentDictionary<Type, Func<Neuron, Synapse, SynapseMetadata, CancellationToken, Task>> SelfQuestionDeliverers = new();
    private static readonly ConcurrentDictionary<(Type Question, Type Reply), Func<Neuron, Synapse, Synapse, CancellationToken, Task>> ContinuationInvokers = new();

    [Alias("db.transport")]
    internal interface ITransport : IGrainWithStringKey
    {
        [Alias("deliver")]
        Task DeliverAsync<TFact>(TFact fact, CancellationToken cancellationToken)
            where TFact : Synapse;

        [Alias("deliver-question")]
        Task DeliverQuestionAsync<TQuestion, TReply>(TQuestion question, CancellationToken cancellationToken)
            where TQuestion : Synapse<TReply>
            where TReply : Synapse;

        [ReadOnly]
        [Alias("read")]
        Task<NeuronReading> ReadAsync(long afterPosition);

        [ReadOnly]
        [Alias("read-state")]
        Task<JsonElement> ReadStateAsync();
    }

    internal static GrainId AddressOf(NeuronId id) => GrainId.Create(id.Kind, id.Name);

    // The filters stage whitelists incoming calls; a delivery is the only turn-opening one.
    internal static bool IsDelivery(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return method.DeclaringType == typeof(ITransport)
            && method.Name is nameof(ITransport.DeliverAsync) or nameof(ITransport.DeliverQuestionAsync);
    }

    // Sender-side closers over the transport, one per fact type, cached forever: the drain
    // closes DeliverAsync<TFact> for listener-routed receivers and
    // DeliverQuestionAsync<TQ,TR> for the answerer route (TR extracted once per question).
    internal static Func<ITransport, Synapse, CancellationToken, Task> WireDelivererFor(Type factType)
        => WireDeliverers.GetOrAdd(factType, static closed
            => CloserFor<Func<ITransport, Synapse, CancellationToken, Task>>(nameof(SendFactAsync), closed));

    internal static Func<ITransport, Synapse, CancellationToken, Task> WireQuestionDelivererFor(Type questionType)
        => WireQuestionDeliverers.GetOrAdd(questionType, static closed
            => CloserFor<Func<ITransport, Synapse, CancellationToken, Task>>(
                nameof(SendQuestionAsync), closed, Catalog.ReplyTypeOf(closed)));

    // Self-delivery enters the same reception routine by direct method call — never the
    // proxy (the proven self-call deadlock).
    internal Task DeliverToSelfAsync(Synapse fact, SynapseMetadata metadata, bool asQuestion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var deliverer = asQuestion
            ? SelfQuestionDeliverers.GetOrAdd(fact.GetType(), static closed
                => CloserFor<Func<Neuron, Synapse, SynapseMetadata, CancellationToken, Task>>(
                    nameof(ForwardQuestionAsync), closed, Catalog.ReplyTypeOf(closed)))
            : SelfDeliverers.GetOrAdd(fact.GetType(), static closed
                => CloserFor<Func<Neuron, Synapse, SynapseMetadata, CancellationToken, Task>>(
                    nameof(ForwardFactAsync), closed));

        return deliverer(this, fact, metadata, cancellationToken);
    }

    // The filter/transport envelope hand-off. Turns are serialized and the filter runs
    // synchronously before its Invoke, so one slot per direction cannot be overwritten.
    private SynapseMetadata? incomingEnvelope;
    private SynapseMetadata? outboundDelivery;

    internal void AcceptEnvelope(SynapseMetadata metadata) => incomingEnvelope = metadata;

    internal void StageOutboundDelivery(SynapseMetadata metadata) => outboundDelivery = metadata;

    internal SynapseMetadata TakeOutboundDelivery()
    {
        var staged = outboundDelivery ?? throw new InvalidOperationException(
            $"A transport delivery left {Id} without a staged envelope; the drain stages it "
            + "before every wire call — a kernel bug.");
        outboundDelivery = null;
        return staged;
    }

    private SynapseMetadata TakeEnvelope()
    {
        var envelope = incomingEnvelope ?? throw new InvalidOperationException(
            $"A delivery reached {Id} without its envelope; Core writes the headers before "
            + "every wire call and the incoming filter consumes them — a delivery without "
            + "an envelope is a kernel bug.");
        incomingEnvelope = null;
        return envelope;
    }

    Task ITransport.DeliverAsync<TFact>(TFact fact, CancellationToken cancellationToken)
        => DeliverCoreAsync(fact, TakeEnvelope(), cancellationToken);

    Task ITransport.DeliverQuestionAsync<TQuestion, TReply>(TQuestion question, CancellationToken cancellationToken)
        => DeliverQuestionCoreAsync<TQuestion, TReply>(question, TakeEnvelope(), cancellationToken);

    Task<NeuronReading> ITransport.ReadAsync(long afterPosition)
    {
        RefusePoisoned();
        var read = journal.Read(afterPosition);
        var facts = new List<JournalFact>(read.Delta.Count);
        foreach (var entry in read.Delta)
        {
            facts.Add(new JournalFact(
                entry.Seq,
                entry.Entry,
                entry.Kind,
                entry.ToMetadata(Id),
                entry.To is { } to
                    ? [.. to.Select(receiver => new Delivery(receiver.ToNeuronId(), receiver.Via ?? string.Empty))]
                    : null,
                codec.DecodeFact(entry.Kind, entry.Body)));
        }

        return Task.FromResult(new NeuronReading(facts, read.Connections));
    }

    Task<JsonElement> ITransport.ReadStateAsync()
    {
        RefusePoisoned();
        return Task.FromResult(journal.CommittedState);
    }

    private static TDelegate CloserFor<TDelegate>(string methodName, params Type[] typeArguments)
        where TDelegate : Delegate
        => typeof(Neuron)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .CreateDelegate<TDelegate>();

    private static Task SendFactAsync<TFact>(ITransport receiver, Synapse fact, CancellationToken cancellationToken)
        where TFact : Synapse
        => receiver.DeliverAsync((TFact)fact, cancellationToken);

    private static Task SendQuestionAsync<TQuestion, TReply>(ITransport receiver, Synapse question, CancellationToken cancellationToken)
        where TQuestion : Synapse<TReply>
        where TReply : Synapse
        => receiver.DeliverQuestionAsync<TQuestion, TReply>((TQuestion)question, cancellationToken);

    private static Task ForwardFactAsync<TFact>(Neuron receiver, Synapse fact, SynapseMetadata metadata, CancellationToken cancellationToken)
        where TFact : Synapse
        => receiver.DeliverCoreAsync((TFact)fact, metadata, cancellationToken);

    private static Task ForwardQuestionAsync<TQuestion, TReply>(Neuron receiver, Synapse question, SynapseMetadata metadata, CancellationToken cancellationToken)
        where TQuestion : Synapse<TReply>
        where TReply : Synapse
        => receiver.DeliverQuestionCoreAsync<TQuestion, TReply>((TQuestion)question, metadata, cancellationToken);

    private static Func<Neuron, Synapse, Synapse, CancellationToken, Task> ContinuationInvokerFor(Type questionType, Type replyType)
        => ContinuationInvokers.GetOrAdd((questionType, replyType), static closed
            => CloserFor<Func<Neuron, Synapse, Synapse, CancellationToken, Task>>(
                nameof(ContinueCoreAsync), closed.Question, closed.Reply));

    private static Task ContinueCoreAsync<TQuestion, TReply>(Neuron receiver, Synapse question, Synapse reply, CancellationToken cancellationToken)
        where TQuestion : Synapse<TReply>
        where TReply : Synapse
        => ((INeuron<Answer<TQuestion, TReply>>)receiver).HandleAsync(
            new Answer<TQuestion, TReply>((TQuestion)question, (TReply)reply), cancellationToken);
}
