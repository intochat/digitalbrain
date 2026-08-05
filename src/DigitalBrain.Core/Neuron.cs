using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain;

// The base grain: the receive side of the turn pipeline (§4 steps 1-9) and the module
// verb surface (§5). Nothing leaves a neuron before its turn commits; no durable
// structure is touched before staging (post-handler); ONE WriteStateAsync commits the
// whole batch; any commit failure poisons the activation — reload of committed truth on
// the next activation is the only resynchronization. Dispatch (the drain, wakeup and
// timers) lives in Neuron.Dispatch.cs; reserved-kind interception in
// Neuron.Connections.cs / Neuron.Schedule.cs; ask expiry in Neuron.Asks.cs.
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
        // Keyed, never the ambient TimeProvider: Orleans' own runtime (the activation
        // collector above all) resolves the unkeyed one, and a controllable test clock
        // handed to it breaks its internal scheduling invariants.
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

    // ── the escape hatches, sealed away from modules ─────────────────────────────────────

    [Obsolete("Core commits the one-batch turn write (§4 step 8); a module-visible WriteStateAsync is an unenlisted durable write — the atomicity hole itself.", error: true)]
    protected new ValueTask WriteStateAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Core owns the turn commit.");

    [Obsolete("Neurons speak in facts; GrainFactory is a second wire that bypasses the turn pipeline and deadlocks on self-calls.", error: true)]
    protected new IGrainFactory GrainFactory
        => throw new NotSupportedException("Core owns the wire.");

    [Obsolete("Activation lifetime is Core's; poison-and-reload after a failed commit is the only deactivation path.", error: true)]
    protected new void DeactivateOnIdle()
        => throw new NotSupportedException("Core owns activation lifetime.");

    // ── the verbs (§5): in-memory staging only, eager encoding at the call ───────────────

    protected void Emit(Synapse fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var active = RequireTurn();
        RefuseContinuationEmission(fact.GetType());
        active.Emissions.Add(StagedFor(fact));
    }

    private static void RefuseContinuationEmission(Type factType)
    {
        if (factType.IsGenericType && factType.GetGenericTypeDefinition() == typeof(Answer<,>))
        {
            throw new InvalidOperationException(
                "Answer<,> exists only at dispatch — the reply fact is the journal record; "
                + "emit facts, never the continuation view.");
        }
    }

    protected void Ask<TReply>(Synapse<TReply> question)
        where TReply : Synapse
    {
        ArgumentNullException.ThrowIfNull(question);
        var active = RequireTurn();
        var questionType = question.GetType();
        if (!catalog.HasContinuation(Id.Kind, questionType))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} asks {questionType.Name} without declaring "
                + $"INeuron<Answer<{questionType.Name}, {Catalog.ReplyTypeOf(questionType).Name}>>; "
                + "declare the continuation, or Emit the question to announce it.");
        }

        var staged = StagedFor(question);
        active.Emissions.Add(catalog.TryGetAnswererKind(questionType, out var answererKind)
            ? staged with { AskAnswererKind = answererKind }
            : staged with { AskLacksAnswerer = true });
    }

    protected void Schedule(Synapse fact, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        var active = RequireTurn();
        var factType = fact.GetType();
        if (!catalog.ListenerKindsOf(factType).Contains(Id.Kind))
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

    // ── the turn runner ──────────────────────────────────────────────────────────────────

    private async Task DeliverCoreAsync<TFact>(TFact fact, SynapseMetadata metadata, CancellationToken cancellationToken)
        where TFact : Synapse
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(metadata);
        RefusePoisoned();

        if (metadata.Sequence <= journal.WatermarkOf(metadata.Source))
        {
            return;   // duplicate: the silent success ack — a throw would mint false terminal records
        }

        // Reserved kinds are handled by Core on the receiving emitter (§3) BEFORE module
        // dispatch — the catalog already refuses module declarations for them, so the
        // interception is never ambiguous with a listener.
        switch (fact)
        {
            case Connect connect:
                await ReceiveConnectAsync(connect, metadata);
                return;
            case Disconnect disconnect:
                await ReceiveDisconnectAsync(disconnect, metadata);
                return;
            case Schedule remoteSchedule:
                await ReceiveScheduleAsync(remoteSchedule, metadata);
                return;
            case Unschedule remoteUnschedule:
                await ReceiveUnscheduleAsync(remoteUnschedule, metadata);
                return;
            default:
                break;
        }

        if (metadata.Answers is { } askRef && askRef.Source == Id)
        {
            await ReceiveReplyAsync(fact, metadata, askRef, cancellationToken);
            return;
        }

        if (!catalog.ListenerKindsOf(typeof(TFact)).Contains(Id.Kind))
        {
            await JournalUnhandledAsync(fact, metadata);
            return;
        }

        OpenTurn(fact, metadata);
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
        TQuestion question, SynapseMetadata metadata, CancellationToken cancellationToken)
        where TQuestion : Synapse<TReply>
        where TReply : Synapse
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(metadata);
        RefusePoisoned();

        if (metadata.Sequence <= journal.WatermarkOf(metadata.Source))
        {
            return;
        }

        if (!catalog.IsAnswerer(Id.Kind, typeof(TQuestion)))
        {
            await JournalUnhandledAsync(question, metadata);
            return;
        }

        var questionKind = catalog.KindOfFact(typeof(TQuestion));
        if (journal.OpenAskOf(questionKind) is not null)
        {
            throw new InvalidOperationException(
                $"{Id} already holds an open ask of '{questionKind}'; at most one per question kind — "
                + "the refused delivery stays in the sender's outbox (backpressure).");
        }

        OpenTurn(question, metadata);
        TReply? reply;
        try
        {
            reply = await ((INeuron<TQuestion, TReply>)this).HandleAsync(question, cancellationToken);
        }
        catch
        {
            ClearTurn();
            throw;
        }

        await CommitTurnAsync(reply, openAskKind: reply is null ? questionKind : null);
    }

    // The asker receiving a fact whose Answers points into its own journal (§5): the
    // guarded predicate decides between continuation dispatch, a loud terminal record, and
    // a plain journaled reception — never a fabricated Answer<Q,R>.
    private async Task ReceiveReplyAsync<TFact>(
        TFact reply, SynapseMetadata metadata, SynapseRef askRef, CancellationToken cancellationToken)
        where TFact : Synapse
    {
        if (!journal.HasAskPin(askRef.Sequence))
        {
            // Late or already-settled: an ordinary reception, dispatches nothing — falling
            // off the window was loud exactly once (AskExpired), never twice.
            await ReceiveWithoutDispatchAsync(reply, metadata, terminalOutcome: null, releasePin: null);
            return;
        }

        var askEntry = journal.EntryAt(askRef.Sequence);
        var questionType = askEntry is { Entry: JournalEntry.Said }
            && catalog.TryGetFactType(askEntry.Kind, out var asked)
                ? asked
                : null;

        if (questionType is null
            || askEntry is null
            || Catalog.ReplyTypeOrNull(questionType) != typeof(TFact))
        {
            await ReceiveWithoutDispatchAsync(
                reply, metadata, new AskExpired(askRef, askEntry?.Kind ?? "unknown"), askRef.Sequence);
            return;
        }

        if (!catalog.IsAnswerer(metadata.Source.Kind, questionType))
        {
            // Reply-type impersonation: no continuation; the pin stays for the true answer.
            await ReceiveWithoutDispatchAsync(reply, metadata, terminalOutcome: null, releasePin: null);
            return;
        }

        if (!ContinuesAsks)
        {
            // The edge session (§5): the journaled reception — Answers stamped — IS the
            // round trip's closing record; the edge poll matches it, nothing dispatches.
            await ReceiveWithoutDispatchAsync(reply, metadata, terminalOutcome: null, releasePin: askRef.Sequence);
            return;
        }

        if (!catalog.HasContinuation(Id.Kind, questionType)
            || !string.Equals(
                catalog.ShapeFingerprintOf(questionType),
                ShapeFingerprintOfBody(askEntry.Body),
                StringComparison.Ordinal)
            || codec.Decode(askEntry.Body, questionType) is not Synapse question)
        {
            // Question-shape drift: a silently-defaulted Answer.Question would be fabrication.
            await ReceiveWithoutDispatchAsync(
                reply, metadata, new AskExpired(askRef, askEntry.Kind), askRef.Sequence);
            return;
        }

        OpenTurn(reply, metadata);
        try
        {
            await ContinuationInvokerFor(questionType, typeof(TFact))(this, question, reply, cancellationToken);
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
        Synapse fact, SynapseMetadata metadata, Synapse? terminalOutcome, long? releasePin)
    {
        OpenTurn(fact, metadata);
        var active = turn!;
        if (terminalOutcome is not null)
        {
            active.Emissions.Add(StagedFor(terminalOutcome));
        }

        active.UnpinPosition = releasePin;
        await CommitTurnAsync(reply: null, openAskKind: null);
    }

    // The delivery-time backstop (§3): no exact declared handler settles terminally on the
    // first attempt — the reception journals here (the receiver's truth), the refusal
    // signal crosses back for the sender's DeliveryFailed (its truth).
    private async Task JournalUnhandledAsync(Synapse fact, SynapseMetadata metadata)
    {
        var factKind = catalog.KindOfFact(fact.GetType());
        try
        {
            journal.AppendHeard(
                factKind,
                metadata.Timestamp,
                SynapseRefEntry.From(new SynapseRef(metadata.Source, metadata.Sequence)),
                metadata.Cause is { } cause ? SynapseRefEntry.From(cause) : null,
                metadata.Answers is { } answers ? SynapseRefEntry.From(answers) : null,
                codec.Encode(fact));
            journal.SetWatermark(metadata.Source, metadata.Sequence, clock.GetUtcNow());
        }
        catch
        {
            Poison();
            throw;
        }

        await CommitCoreBatchAsync(deliverable: false);
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

            // The shared Core commit arms the wakeup BEFORE the write (§4 step 7), poisons
            // on any failure, and indexes/schedules dispatch of the newly committed batch.
            await CommitCoreBatchAsync(deliverable);
            SyncScheduleTimers();
        }
        finally
        {
            ClearTurn();
        }
    }

    // Step 6: stage the whole batch in order — heard entry, said entries with the receiver
    // snapshot resolved NOW, state slot if touched, watermark, pins, schedule mutations,
    // the answer last. In-memory mutations of the durable structures; the caller commits.
    private bool StageBatch(Turn active, Synapse? reply, string? openAskKind)
    {
        var metadata = active.Metadata;
        var now = clock.GetUtcNow();
        var heardFrom = SynapseRefEntry.From(new SynapseRef(metadata.Source, metadata.Sequence));
        var heardCause = metadata.Cause is { } cause ? SynapseRefEntry.From(cause) : null;
        var heardAnswers = metadata.Answers is { } answered ? SynapseRefEntry.From(answered) : null;

        journal.AppendHeard(
            catalog.KindOfFact(active.Fact.GetType()),
            metadata.Timestamp,
            heardFrom,
            heardCause,
            heardAnswers,
            codec.Encode(active.Fact));

        var openAsks = journal.OpenAsksSnapshot();
        var deliverable = false;

        foreach (var staged in active.Emissions)
        {
            deliverable |= StageSaid(staged, heardFrom, now, replyTo: null, openAsks);
        }

        if (StateSlotIfTouched() is { } touchedState)
        {
            journal.State = touchedState;
        }

        journal.SetWatermark(metadata.Source, metadata.Sequence, now);

        if (active.UnpinPosition is { } answeredAsk)
        {
            journal.UnpinAsk(answeredAsk);
        }

        foreach (var change in active.ScheduleChanges)
        {
            // Every schedule mutation journals a zero-receiver said entry; ticks carry its
            // position as their Cause — the schedule's journaled ref (§6).
            var recordPosition = journal.AppendSaid(
                change.RecordKind, now, heardFrom, answers: null, to: [], change.Record);
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
            deliverable |= StageSaid(StagedFor(reply), heardFrom, now, replyTo: heardFrom, openAsks);
        }

        return deliverable;
    }

    private bool StageSaid(
        StagedEmission staged,
        SynapseRefEntry? cause,   // null = edge-born: the session's own utterances (§5 edge)
        DateTimeOffset now,
        SynapseRefEntry? replyTo,
        List<KeyValuePair<string, SynapseRefEntry>> openAsks)
    {
        var receivers = new List<NeuronIdEntry>();
        var routed = new HashSet<NeuronId>();
        var connected = journal.ConnectionsOf(staged.Kind);
        var redirectedKinds = new HashSet<string>(connected.Select(target => target.Kind), StringComparer.Ordinal);

        foreach (var listenerKind in catalog.ListenerKindsOf(staged.FactType))
        {
            if (redirectedKinds.Contains(listenerKind))
            {
                continue;   // the ghost rule: a connection redirects the kind away from this context
            }

            var listener = new NeuronId(listenerKind, Id.Name);
            if (listener == Id)
            {
                continue;   // an emitter never fans out to itself (flow 7's Pulse would tick forever)
            }

            if (routed.Add(listener))
            {
                receivers.Add(NeuronIdEntry.From(listener, NeuronIdEntry.Declared));
            }
        }

        foreach (var target in connected)
        {
            if (routed.Add(target))
            {
                receivers.Add(NeuronIdEntry.From(target, NeuronIdEntry.Connected));
            }
        }

        if (staged.DirectedTo is { } directed)
        {
            // Core-directed outcome (ConnectionRefused, refused remote Schedule): the
            // requester rides the snapshot beside ordinary routing, via the directed role.
            AddAskReceiver(receivers, routed, directed);
        }

        SynapseRefEntry? answers = null;

        if (replyTo is { } questionRef)
        {
            answers = questionRef;
            AddAskReceiver(receivers, routed, new NeuronId(questionRef.Kind, questionRef.Name));
        }
        else if (staged.AskAnswererKind is { } answererKind)
        {
            AddAskReceiver(receivers, routed, new NeuronId(answererKind, Id.Name));
        }
        else
        {
            // The closure rule (§5): while an ask is open, the FIRST staged emission typed
            // as its reply answers it — stamped and additionally delivered to the asker.
            for (var index = 0; index < openAsks.Count; index++)
            {
                var (questionKind, askedBy) = openAsks[index];
                if (!catalog.TryGetFactType(questionKind, out var questionType)
                    || Catalog.ReplyTypeOrNull(questionType) != staged.FactType)
                {
                    continue;
                }

                answers = askedBy;
                AddAskReceiver(receivers, routed, new NeuronId(askedBy.Kind, askedBy.Name));
                journal.RemoveOpenAsk(questionKind);
                openAsks.RemoveAt(index);
                break;
            }
        }

        var position = journal.AppendSaid(staged.Kind, now, cause, answers, [.. receivers], staged.Body);

        if (staged.AskAnswererKind is not null)
        {
            journal.PinAsk(position, now);
        }

        var deliverable = receivers.Count > 0;

        if (staged.AskLacksAnswerer)
        {
            // A zero-answerer ask settles terminally at once (§4 step 4) — no retry burn.
            var failure = new DeliveryFailed(
                new SynapseRef(Id, position),
                new NeuronId(string.Empty, string.Empty),
                "no-answerer",
                Attempts: 0);
            deliverable |= StageSaid(StagedFor(failure), cause, now, replyTo: null, openAsks);
        }

        return deliverable;
    }

    private static void AddAskReceiver(List<NeuronIdEntry> receivers, HashSet<NeuronId> routed, NeuronId receiver)
    {
        if (routed.Add(receiver))
        {
            receivers.Add(NeuronIdEntry.From(receiver, NeuronIdEntry.Ask));
        }
    }

    private StagedEmission StagedFor(Synapse fact)
        => new(fact.GetType(), catalog.KindOfFact(fact.GetType()), codec.Encode(fact));

    private static string ShapeFingerprintOfBody(JsonElement body)
        => body.ValueKind is JsonValueKind.Object
            ? Catalog.FingerprintOfShape(body.EnumerateObject().Select(property => property.Name))
            : string.Empty;

    // Declared continuations dispatch on the asker (§5); the edge session opts out — its
    // asks close by the journaled reception alone, matched by the edge poll on Answers.
    private protected virtual bool ContinuesAsks => true;

    // ── turn ambience and poisoning ──────────────────────────────────────────────────────

    private Turn RequireTurn()
        => turn ?? throw new InvalidOperationException(
            "No turn is open; verbs and State ride the turn a delivery runs — there is no out-of-turn emission.");

    private void OpenTurn(Synapse fact, SynapseMetadata metadata) => turn = new Turn(fact, metadata);

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

    // ── the Neuron<TState> seam ──────────────────────────────────────────────────────────

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

    // ── the dispatch seam (implemented in Neuron.Dispatch.cs) ────────────────────────────

    private partial Task ResumeDispatchAsync();

    private partial ValueTask ArmWakeupAsync();

    private partial void ScheduleDrain();

    private sealed class Turn(Synapse fact, SynapseMetadata metadata)
    {
        internal Synapse Fact { get; } = fact;

        internal SynapseMetadata Metadata { get; } = metadata;

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
