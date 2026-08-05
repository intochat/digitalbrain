using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain;

public abstract partial class Neuron : DurableGrain, IGrainWithStringKey
{
    private readonly NeuronJournal journal;
    private readonly Catalog catalog;
    private readonly BodyCodec codec;
    private readonly TimeProvider clock;
    private Turn? turn;
    private bool poisoned;

    protected Neuron()
    {
        journal = ServiceProvider.GetRequiredService<NeuronJournal>();
        catalog = ServiceProvider.GetRequiredService<Catalog>();
        codec = ServiceProvider.GetRequiredService<BodyCodec>();
        clock = ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey) ?? TimeProvider.System;
    }

    public NeuronId Id => new(NeuronId.KindOf(GetType()), this.GetPrimaryKeyString());

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken);
        journal.MarkCommitted();
        await ResumeDispatchAsync();
    }

    public sealed override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        => base.OnDeactivateAsync(reason, cancellationToken);

    [Obsolete("Core commits the one-batch turn write; a module-visible WriteStateAsync is an unenlisted durable write.", error: true)]
    protected new ValueTask WriteStateAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Core owns the turn commit.");

    [Obsolete("Neurons speak in facts; GrainFactory is a second wire that bypasses the turn pipeline and deadlocks on self-calls.", error: true)]
    protected new IGrainFactory GrainFactory
        => throw new NotSupportedException("Core owns the wire.");

    [Obsolete("Activation lifetime is Core's; poison-and-reload after a failed commit is the only deactivation path.", error: true)]
    protected new void DeactivateOnIdle()
        => throw new NotSupportedException("Core owns activation lifetime.");

    protected void Emit(Synapse fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        RequireTurn().Emissions.Add(StagedFor(fact));
    }

    protected void Ask<TReply>(Synapse question)
        where TReply : Synapse
    {
        ArgumentNullException.ThrowIfNull(question);
        var active = RequireTurn();
        var questionType = question.GetType();
        if (!catalog.ListensTo(Id.Kind, typeof(TReply)))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} asks {questionType.Name} without declaring INeuron<{typeof(TReply).Name}>; "
                + "declare the reply listener, or Emit the question to announce it.");
        }

        if (catalog.TryGetReplyType(questionType, out var replyType) && replyType != typeof(TReply))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} asks {questionType.Name} as {typeof(TReply).Name} but the answerer replies with {replyType.Name}.");
        }

        var staged = StagedFor(question);
        active.Emissions.Add(catalog.TryGetAnswererKind(questionType, out var answererKind)
            ? staged with { AskAnswererKind = answererKind }
            : staged with { AskLacksAnswerer = true });
    }

    protected void Reply(Synapse fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var active = RequireTurn();
        active.Emissions.Add(StagedFor(fact) with { DirectedTo = active.Envelope.Source });
    }

    protected void Schedule(Synapse fact, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        var active = RequireTurn();
        var factType = fact.GetType();
        if (!catalog.ListensTo(Id.Kind, factType))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} schedules {factType.Name} without declaring INeuron<{factType.Name}>; "
                + "a tick nobody handles is a dead claim.");
        }

        active.ScheduleChanges.Add(new StagedScheduleChange(
            catalog.KindOfFact(factType),
            codec.Encode(fact),
            period,
            catalog.KindOfFact(typeof(Schedule)),
            codec.Encode(new Schedule(fact, period))));
    }

    protected void Unschedule<TFact>()
        where TFact : Synapse
    {
        var active = RequireTurn();
        var factKind = catalog.KindOfFact(typeof(TFact));
        active.ScheduleChanges.Add(new StagedScheduleChange(
            factKind,
            Fact: null,
            TimeSpan.Zero,
            catalog.KindOfFact(typeof(Unschedule)),
            codec.Encode(new Unschedule(factKind))));
    }

    private async Task DeliverCoreAsync<TFact>(TFact fact, DeliveryEnvelope envelope, CancellationToken cancellationToken)
        where TFact : Synapse
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(envelope);
        RefusePoisoned();

        if (envelope.Sequence <= journal.WatermarkOf(envelope.Source))
        {
            return;
        }

        switch (fact)
        {
            case Connect connect:
                await ReceiveConnectAsync(connect, envelope);
                return;
            case Disconnect disconnect:
                await ReceiveDisconnectAsync(disconnect, envelope);
                return;
            case Schedule remoteSchedule:
                await ReceiveScheduleAsync(remoteSchedule, envelope);
                return;
            case Unschedule remoteUnschedule:
                await ReceiveUnscheduleAsync(remoteUnschedule, envelope);
                return;
            default:
                break;
        }

        if (envelope.Answers is { } askRef && askRef.Source == Id)
        {
            await ReceiveReplyAsync(fact, envelope, askRef, cancellationToken);
            return;
        }

        if (!catalog.ListensTo(Id.Kind, typeof(TFact)))
        {
            await JournalUnhandledAsync(fact, envelope);
            return;
        }

        OpenTurn(fact, envelope);
        try
        {
            await ((INeuron<TFact>)this).HandleAsync(fact, cancellationToken);
        }
        catch
        {
            ClearTurn();
            throw;
        }

        await CommitTurnAsync(reply: null, openAskKind: null);
    }

    private async Task DeliverQuestionCoreAsync<TQuestion, TReply>(
        TQuestion question, DeliveryEnvelope envelope, CancellationToken cancellationToken)
        where TQuestion : Synapse
        where TReply : Synapse
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(envelope);
        RefusePoisoned();

        if (envelope.Sequence <= journal.WatermarkOf(envelope.Source))
        {
            return;
        }

        if (!catalog.IsAnswerer(Id.Kind, typeof(TQuestion)))
        {
            await JournalUnhandledAsync(question, envelope);
            return;
        }

        var questionKind = catalog.KindOfFact(typeof(TQuestion));
        if (journal.OpenAskOf(questionKind) is not null)
        {
            throw new InvalidOperationException(
                $"{Id} already holds an open ask of '{questionKind}'; at most one per question kind — "
                + "the refused delivery stays in the sender's outbox (backpressure).");
        }

        OpenTurn(question, envelope);
        TReply? reply;
        try
        {
            reply = await ((IAnswers<TQuestion, TReply>)this).HandleAsync(question, cancellationToken);
        }
        catch
        {
            ClearTurn();
            throw;
        }

        await CommitTurnAsync(reply, openAskKind: reply is null ? questionKind : null);
    }

    private async Task ReceiveReplyAsync<TFact>(
        TFact reply, DeliveryEnvelope envelope, SynapseRef askRef, CancellationToken cancellationToken)
        where TFact : Synapse
    {
        if (!journal.HasAskPin(askRef.Sequence))
        {
            await ReceiveWithoutDispatchAsync(reply, envelope, terminalOutcome: null, releasePin: null);
            return;
        }

        var askEntry = journal.EntryAt(askRef.Sequence);
        if (askEntry is not { Entry: JournalEntry.Said }
            || !catalog.TryGetFactType(askEntry.Kind, out var questionType)
            || !catalog.TryGetReplyType(questionType, out var replyType)
            || replyType != typeof(TFact))
        {
            await ReceiveWithoutDispatchAsync(
                reply, envelope, new AskExpired(askRef, askEntry?.Kind ?? "unknown"), askRef.Sequence);
            return;
        }

        if (!catalog.IsAnswerer(envelope.Source.Kind, questionType))
        {
            await ReceiveWithoutDispatchAsync(reply, envelope, terminalOutcome: null, releasePin: null);
            return;
        }

        if (!ContinuesAsks)
        {
            await ReceiveWithoutDispatchAsync(reply, envelope, terminalOutcome: null, releasePin: askRef.Sequence);
            return;
        }

        OpenTurn(reply, envelope);
        try
        {
            await ((INeuron<TFact>)this).HandleAsync(reply, cancellationToken);
        }
        catch
        {
            ClearTurn();
            throw;
        }

        turn!.UnpinPosition = askRef.Sequence;
        await CommitTurnAsync(reply: null, openAskKind: null);
    }

    private async Task ReceiveWithoutDispatchAsync(
        Synapse fact, DeliveryEnvelope envelope, Synapse? terminalOutcome, long? releasePin)
    {
        OpenTurn(fact, envelope);
        if (terminalOutcome is not null)
        {
            turn!.Emissions.Add(StagedFor(terminalOutcome));
        }

        turn!.UnpinPosition = releasePin;
        await CommitTurnAsync(reply: null, openAskKind: null);
    }

    private async Task JournalUnhandledAsync(Synapse fact, DeliveryEnvelope envelope)
    {
        var factKind = catalog.KindOfFact(fact.GetType());
        OpenTurn(fact, envelope);
        await CommitTurnAsync(reply: null, openAskKind: null);
        throw new UnhandledFactException(factKind, Id);
    }

    private async Task CommitTurnAsync(Synapse? reply, string? openAskKind)
    {
        var active = turn ?? throw new InvalidOperationException("No turn is open to commit.");
        try
        {
            bool deliverable;
            try
            {
                deliverable = StageBatch(active, reply, openAskKind);
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable);
            SyncScheduleTimers();
        }
        finally
        {
            ClearTurn();
        }
    }

    private bool StageBatch(Turn active, Synapse? reply, string? openAskKind)
    {
        var envelope = active.Envelope;
        var now = clock.GetUtcNow();
        var (heardFrom, _) = AppendHeardFromEnvelope(active.Fact, envelope);
        var openAsks = journal.OpenAsksSnapshot();
        var deliverable = false;

        var emissionDepth = active.Envelope.EmissionDepth;
        foreach (var staged in active.Emissions)
        {
            deliverable |= StageSaid(staged, heardFrom, now, replyTo: null, openAsks, emissionDepth);
        }

        if (StateSlotIfTouched() is { } touchedState)
        {
            journal.State = touchedState;
        }

        journal.SetWatermark(envelope.Source, envelope.Sequence, now);

        if (active.UnpinPosition is { } answeredAsk)
        {
            journal.UnpinAsk(answeredAsk);
        }

        foreach (var change in active.ScheduleChanges)
        {
            var recordPosition = journal.AppendSaid(
                change.RecordKind, now, heardFrom, answers: null, to: [], change.Record, emissionDepth);
            if (change.Fact is { } scheduledFact)
            {
                journal.SetSchedule(change.FactKind, new ScheduleEntry(
                    change.FactKind, scheduledFact, change.Period, now + change.Period,
                    ConsecutiveFailures: 0, Cause: recordPosition));
            }
            else
            {
                journal.RemoveSchedule(change.FactKind);
            }
        }

        if (openAskKind is not null)
        {
            journal.SetOpenAsk(openAskKind, heardFrom);
        }

        if (reply is not null)
        {
            deliverable |= StageSaid(
                StagedFor(reply), heardFrom, now, replyTo: heardFrom, openAsks, emissionDepth);
        }

        return deliverable;
    }

    private bool StageSaid(
        StagedEmission staged,
        SynapseRefEntry? cause,
        DateTimeOffset now,
        SynapseRefEntry? replyTo,
        List<KeyValuePair<string, SynapseRefEntry>> openAsks,
        int depth = 1)
    {
        var receivers = new List<NeuronIdEntry>();
        var routed = new HashSet<NeuronId>();
        var connected = journal.ConnectionsOf(staged.Kind);
        var redirectedKinds = new HashSet<string>(connected.Select(target => target.Kind), StringComparer.Ordinal);

        foreach (var listenerKind in catalog.ListenerKindsOf(staged.FactType))
        {
            if (redirectedKinds.Contains(listenerKind))
            {
                continue;
            }

            var listener = new NeuronId(listenerKind, Id.Name);
            if (listener != Id)
            {
                RouteTo(receivers, routed, listener, NeuronIdEntry.Declared);
            }
        }

        foreach (var target in connected)
        {
            RouteTo(receivers, routed, target, NeuronIdEntry.Connected);
        }

        if (staged.DirectedTo is { } directed)
        {
            RouteTo(receivers, routed, directed, NeuronIdEntry.Directed);
        }

        SynapseRefEntry? answers = null;

        if (replyTo is { } questionRef)
        {
            answers = questionRef;
            RouteTo(
                receivers,
                routed,
                new NeuronId(questionRef.Kind, questionRef.Name),
                NeuronIdEntry.Request);
        }
        else if (staged.AskAnswererKind is { } answererKind)
        {
            RouteTo(
                receivers,
                routed,
                new NeuronId(answererKind, Id.Name),
                NeuronIdEntry.Request,
                NeuronIdEntry.DeliverQuestion);
        }
        else
        {
            for (var index = 0; index < openAsks.Count; index++)
            {
                var (questionKind, askedBy) = openAsks[index];
                if (!catalog.TryGetFactType(questionKind, out var questionType)
                    || !catalog.TryGetReplyType(questionType, out var replyType)
                    || replyType != staged.FactType)
                {
                    continue;
                }

                answers = askedBy;
                RouteTo(
                    receivers,
                    routed,
                    new NeuronId(askedBy.Kind, askedBy.Name),
                    NeuronIdEntry.Request);
                journal.RemoveOpenAsk(questionKind);
                openAsks.RemoveAt(index);
                break;
            }
        }

        var speechDepth = Math.Max(1, depth);
        var overDepth = speechDepth > DeliveryPolicy.MaximumDepth;
        var position = journal.AppendSaid(
            staged.Kind,
            now,
            cause,
            answers,
            overDepth ? [] : [.. receivers],
            staged.Body,
            speechDepth);

        if (staged.AskAnswererKind is not null && !overDepth)
        {
            journal.PinAsk(position, now);
        }

        var deliverable = false;

        if (overDepth)
        {
            var failedRef = new SynapseRefEntry(Id.Kind, Id.Name, position);
            var reason =
                $"depth {speechDepth} exceeds maximum {DeliveryPolicy.MaximumDepth}";
            if (receivers.Count == 0)
            {
                deliverable |= StageSaid(
                    StagedFor(new DeliveryFailed(
                        new SynapseRef(Id, position),
                        Id,
                        reason,
                        Attempts: 1)),
                    cause,
                    now,
                    replyTo: null,
                    openAsks,
                    depth: 1);
            }
            else
            {
                foreach (var receiver in receivers)
                {
                    deliverable |= StageSaid(
                        StagedFor(new DeliveryFailed(
                            new SynapseRef(Id, position),
                            receiver.ToNeuronId(),
                            reason,
                            Attempts: 1)) with
                        {
                            DirectedTo = receiver.ToNeuronId(),
                        },
                        cause,
                        now,
                        replyTo: null,
                        openAsks,
                        depth: 1);
                }
            }

            return deliverable;
        }

        deliverable = receivers.Count > 0;

        if (staged.AskLacksAnswerer)
        {
            var failure = new DeliveryFailed(
                new SynapseRef(Id, position),
                new NeuronId(string.Empty, string.Empty),
                "no-answerer",
                Attempts: 0);
            deliverable |= StageSaid(StagedFor(failure), cause, now, replyTo: null, openAsks, speechDepth);
        }

        return deliverable;
    }

    private static void RouteTo(
        List<NeuronIdEntry> receivers,
        HashSet<NeuronId> routed,
        NeuronId receiver,
        string via,
        string deliver = NeuronIdEntry.DeliverFact)
    {
        if (routed.Add(receiver))
        {
            receivers.Add(NeuronIdEntry.From(receiver, via, deliver));
        }
    }

    private (SynapseRefEntry From, long Position) AppendHeardFromEnvelope(Synapse fact, DeliveryEnvelope envelope)
    {
        var from = SynapseRefEntry.From(new SynapseRef(envelope.Source, envelope.Sequence));
        var position = journal.AppendHeard(
            catalog.KindOfFact(fact.GetType()),
            envelope.Timestamp,
            from,
            envelope.Cause is { } cause ? SynapseRefEntry.From(cause) : null,
            envelope.Answers is { } answers ? SynapseRefEntry.From(answers) : null,
            codec.Encode(fact));
        return (from, position);
    }

    private StagedEmission StagedFor(Synapse fact)
    {
        var factType = fact.GetType();
        return new(factType, catalog.KindOfFact(factType), codec.Encode(fact));
    }

    private protected virtual bool ContinuesAsks => true;

    private Turn RequireTurn()
        => turn ?? throw new InvalidOperationException(
            "No turn is open; verbs and State ride the turn a delivery runs — there is no out-of-turn emission.");

    private void OpenTurn(Synapse fact, DeliveryEnvelope envelope) => turn = new Turn(fact, envelope);

    private void ClearTurn()
    {
        turn = null;
        ResetTurnState();
    }

    private void RefusePoisoned()
    {
        if (poisoned)
        {
            throw new InvalidOperationException(
                $"{Id} is poisoned by a failed commit and is deactivating; retry reaches a fresh activation.");
        }
    }

    private void Poison()
    {
        poisoned = true;
        base.DeactivateOnIdle();
    }

    private protected virtual JsonElement? StateSlotIfTouched() => null;

    private protected virtual void ResetTurnState()
    {
    }

    private protected TValue MaterializeState<TValue>()
        where TValue : class, new()
    {
        _ = RequireTurn();
        var committed = journal.CommittedState;
        return committed.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new TValue()
            : (TValue?)codec.Decode(committed, typeof(TValue)) ?? new TValue();
    }

    private protected JsonElement EncodeState(object state) => codec.Encode(state);

    private partial Task ResumeDispatchAsync();

    private partial ValueTask ArmWakeupAsync();

    private partial void ScheduleDrain();

    private sealed class Turn(Synapse fact, DeliveryEnvelope envelope)
    {
        internal Synapse Fact { get; } = fact;

        internal DeliveryEnvelope Envelope { get; } = envelope;

        internal List<StagedEmission> Emissions { get; } = [];

        internal List<StagedScheduleChange> ScheduleChanges { get; } = [];

        internal long? UnpinPosition { get; set; }
    }

    private sealed record StagedEmission(Type FactType, string Kind, JsonElement Body)
    {
        internal string? AskAnswererKind { get; init; }

        internal bool AskLacksAnswerer { get; init; }

        internal NeuronId? DirectedTo { get; init; }
    }

    private sealed record StagedScheduleChange(
        string FactKind, JsonElement? Fact, TimeSpan Period, string RecordKind, JsonElement Record);
}
