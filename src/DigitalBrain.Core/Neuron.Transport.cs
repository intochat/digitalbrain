using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Orleans.Concurrency;

namespace DigitalBrain;

public abstract partial class Neuron : Neuron.ITransport
{
    private static readonly ConcurrentDictionary<Type, Func<ITransport, Synapse, CancellationToken, Task>> WireDeliverers = new();
    private static readonly ConcurrentDictionary<(Type Question, Type Reply), Func<ITransport, Synapse, CancellationToken, Task>> WireQuestionDeliverers = new();
    private static readonly ConcurrentDictionary<Type, Func<Neuron, Synapse, DeliveryEnvelope, CancellationToken, Task>> SelfDeliverers = new();
    private static readonly ConcurrentDictionary<(Type Question, Type Reply), Func<Neuron, Synapse, DeliveryEnvelope, CancellationToken, Task>> SelfQuestionDeliverers = new();

    [Alias("db.transport")]
    internal interface ITransport : IGrainWithStringKey
    {
        [Alias("deliver")]
        Task DeliverAsync<TFact>(TFact fact, CancellationToken cancellationToken)
            where TFact : Synapse;

        [Alias("deliver-question")]
        Task DeliverQuestionAsync<TQuestion, TReply>(TQuestion question, CancellationToken cancellationToken)
            where TQuestion : Synapse
            where TReply : Synapse;

        [ReadOnly]
        [Alias("read")]
        Task<NeuronReading> ReadAsync(long afterPosition);

        [ReadOnly]
        [Alias("read-state")]
        Task<JsonElement> ReadStateAsync();
    }

    internal static GrainId AddressOf(NeuronId id) => GrainId.Create(id.Kind, id.Name);

    internal static bool IsDelivery(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return method.DeclaringType == typeof(ITransport)
            && method.Name is nameof(ITransport.DeliverAsync) or nameof(ITransport.DeliverQuestionAsync);
    }

    internal static Func<ITransport, Synapse, CancellationToken, Task> WireDelivererFor(Type factType)
        => WireDeliverers.GetOrAdd(factType, static closed
            => CloserFor<Func<ITransport, Synapse, CancellationToken, Task>>(nameof(SendFactAsync), closed));

    internal Func<ITransport, Synapse, CancellationToken, Task> WireQuestionDelivererFor(Type questionType)
    {
        var replyType = catalog.ReplyTypeOf(questionType);
        return WireQuestionDeliverers.GetOrAdd((questionType, replyType), static closed
            => CloserFor<Func<ITransport, Synapse, CancellationToken, Task>>(
                nameof(SendQuestionAsync), closed.Question, closed.Reply));
    }

    internal Task DeliverToSelfAsync(Synapse fact, DeliveryEnvelope envelope, bool asQuestion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (asQuestion)
        {
            var replyType = catalog.ReplyTypeOf(fact.GetType());
            var deliverer = SelfQuestionDeliverers.GetOrAdd((fact.GetType(), replyType), static closed
                => CloserFor<Func<Neuron, Synapse, DeliveryEnvelope, CancellationToken, Task>>(
                    nameof(ForwardQuestionAsync), closed.Question, closed.Reply));
            return deliverer(this, fact, envelope, cancellationToken);
        }

        var factDeliverer = SelfDeliverers.GetOrAdd(fact.GetType(), static closed
            => CloserFor<Func<Neuron, Synapse, DeliveryEnvelope, CancellationToken, Task>>(
                nameof(ForwardFactAsync), closed));
        return factDeliverer(this, fact, envelope, cancellationToken);
    }

    private DeliveryEnvelope? incomingEnvelope;
    private DeliveryEnvelope? outboundDelivery;

    internal void AcceptEnvelope(DeliveryEnvelope envelope) => incomingEnvelope = envelope;

    internal void StageOutboundDelivery(DeliveryEnvelope envelope) => outboundDelivery = envelope;

    internal DeliveryEnvelope TakeOutboundDelivery()
    {
        var staged = outboundDelivery ?? throw new InvalidOperationException(
            $"A transport delivery left {Id} without a staged envelope; the drain stages it "
            + "before every wire call — a kernel bug.");
        outboundDelivery = null;
        return staged;
    }

    private DeliveryEnvelope TakeEnvelope()
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
                entry.ToEnvelope(Id).Identity,
                entry.Cause?.ToSynapseRef(),
                entry.Answers?.ToSynapseRef(),
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
        where TQuestion : Synapse
        where TReply : Synapse
        => receiver.DeliverQuestionAsync<TQuestion, TReply>((TQuestion)question, cancellationToken);

    private static Task ForwardFactAsync<TFact>(Neuron receiver, Synapse fact, DeliveryEnvelope envelope, CancellationToken cancellationToken)
        where TFact : Synapse
        => receiver.DeliverCoreAsync((TFact)fact, envelope, cancellationToken);

    private static Task ForwardQuestionAsync<TQuestion, TReply>(Neuron receiver, Synapse question, DeliveryEnvelope envelope, CancellationToken cancellationToken)
        where TQuestion : Synapse
        where TReply : Synapse
        => receiver.DeliverQuestionCoreAsync<TQuestion, TReply>((TQuestion)question, envelope, cancellationToken);
}
