using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Chat;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal sealed class BrainGraphProjection(IBrainGraphSource source)
{
    internal const int MaxNodes = 16;
    internal const int MaxActivity = 64;
    internal const string SnapshotScope = "Current conversation, its known runtime participants, and reachable source-owned synapses. Bounded recent activity; direct runtime calls are not invented as synapses.";

    public async Task<BrainGraphSnapshot> ReadAsync(
        string chatName, ActorContext actor, CancellationToken cancellationToken)
    {
        var chat = NeuronId.For<IChat>(source.Owner, PrincipalScoped.InstanceName(actor.PrincipalId, chatName));
        var ownerRoot = IBrainNeuron.ForOwner(source.Owner);
        var activeExecution = await source.ReadActiveExecutionAsync(chat, cancellationToken).ConfigureAwait(false);
        var participants = new HashSet<NeuronId>
        {
            chat,
            new("chat-turn-worker", source.Owner, chat.Name),
            new("assistant", source.Owner, "assistant"),
            ownerRoot,
        };
        if (activeExecution is { } execution && execution.Owner == source.Owner)
        {
            participants.Add(execution);
        }

        var known = new HashSet<NeuronId>(participants);
        var pending = new Queue<NeuronId>(participants);
        var reads = new Dictionary<NeuronId, BrainGraphNeuronRead>();
        var truncated = false;
        while (pending.TryDequeue(out var neuron))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(neuron, cancellationToken).ConfigureAwait(false);
            reads.Add(neuron, read);
            var privateNeuron = IsPrivate(neuron, actor.PrincipalId, activeExecution);
            foreach (var edge in read.Synapses)
            {
                // A shared participant (assistant, owner root) can be used by multiple
                // principal partitions. Never walk its unrelated outgoing graph.
                if (edge.Source == neuron
                    && (privateNeuron || IsPrivate(edge.Target, actor.PrincipalId, activeExecution)))
                {
                    Discover(edge.Target);
                }
            }

            foreach (var delivery in read.Incoming.Delta.Concat(read.Outgoing.Delta))
            {
                if (!VisibleDelivery(delivery, privateNeuron, actor.PrincipalId))
                {
                    continue;
                }

                Discover(delivery.Caller);
                // A removed subscription must remain restorable while its actual
                // subscription event is retained; no separate edge tombstone store.
                if (privateNeuron)
                {
                    if (delivery.Signal is Subscribe subscribed)
                    {
                        Discover(subscribed.Source);
                    }
                    if (delivery.Signal is Unsubscribe unsubscribed)
                    {
                        Discover(unsubscribed.Source);
                    }
                }
            }
        }

        var activity = new List<BrainGraphActivity>();
        var nodes = new List<BrainGraphNode>();
        var synapses = new List<BrainGraphSynapse>();
        foreach (var (neuron, read) in reads)
        {
            var privateNeuron = IsPrivate(neuron, actor.PrincipalId, activeExecution);
            var neuronActivity = ProjectActivity(neuron, read.Incoming, JournalKind.Incoming, privateNeuron, actor.PrincipalId)
                .Concat(ProjectActivity(neuron, read.Outgoing, JournalKind.Outgoing, privateNeuron, actor.PrincipalId))
                .OrderBy(item => item.Timestamp).ToArray();
            activity.AddRange(neuronActivity);
            var metadata = BrainGraphMetadata.For(neuron.Type);
            var localName = PrincipalPartition.TryParse(neuron.Name, out _, out var local) ? local : neuron.Name;
            var lastStatus = read.Outgoing.Delta
                .Where(delivery => VisibleDelivery(delivery, privateNeuron, actor.PrincipalId))
                .Select(delivery => delivery.Signal).OfType<TurnLifecycle>().LastOrDefault()?.Status.ToString();
            nodes.Add(new(InstanceId(neuron), neuron.Type, localName, metadata.Label, metadata.Module,
                participants.Contains(neuron) ? "participant" : "observed",
                lastStatus ?? "Idle", metadata.HandledSignals,
                read.Incoming.ResumeSequence, read.Outgoing.ResumeSequence,
                neuronActivity.LastOrDefault()?.Timestamp));

            foreach (var edge in read.Synapses)
            {
                if (edge.Source != neuron || !reads.ContainsKey(edge.Target)
                    || !CanSee(edge.Target, actor.PrincipalId)
                    || (!privateNeuron && !IsPrivate(edge.Target, actor.PrincipalId, activeExecution)))
                {
                    continue;
                }

                synapses.Add(new(SynapseId(edge), InstanceId(edge.Source), InstanceId(edge.Target), edge.SignalType,
                    edge.Kind.ToString(), edge.Weight, edge.FireCount, edge.LastFiredAt, edge.IsBlocking,
                    edge.Kind == SynapseKind.Bound
                        && PrincipalPartition.OwnsInstance(actor.PrincipalId, edge.Target.Name)
                        && BrainGraphMetadata.IsSubscriptionSignal(edge.SignalType)));
            }
        }

        truncated |= activity.Count > MaxActivity || reads.Values.Any(read =>
            read.Incoming.ResetSnapshot is not null || read.Outgoing.ResetSnapshot is not null);
        return new(InstanceId(chat), DateTimeOffset.UtcNow, truncated, SnapshotScope,
            nodes, synapses, [.. activity.OrderByDescending(item => item.Timestamp).Take(MaxActivity)]);

        void Discover(NeuronId candidate)
        {
            if (!CanSee(candidate, actor.PrincipalId) || known.Contains(candidate))
            {
                return;
            }
            if (known.Count >= MaxNodes)
            {
                truncated = true;
                return;
            }

            known.Add(candidate);
            pending.Enqueue(candidate);
        }
    }

    public async Task<BrainGraphSubscriptionResult> SetSubscriptionAsync(
        string chatName, ActorContext actor, BrainGraphSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!NeuronId.TryParseInstance(request.SourceId, source.Owner, out var from)
            || !NeuronId.TryParseInstance(request.TargetId, source.Owner, out var to)
            || from == to
            || !CanSee(from, actor.PrincipalId)
            || !PrincipalPartition.OwnsInstance(actor.PrincipalId, to.Name)
            || !BrainGraphMetadata.IsSubscriptionSignal(request.SignalType))
        {
            throw new NeuronAuthorizationException("This subscription is outside the current conversation's graph scope.");
        }

        // Rebuild scope immediately before mutation. Never trust a client's stale graph,
        // its claimed edge kind, or an arbitrary owner/instance encoded in the request.
        var snapshot = await ReadAsync(chatName, actor, cancellationToken).ConfigureAwait(false);
        var target = snapshot.Nodes.FirstOrDefault(node => node.Id == InstanceId(to));
        if (!snapshot.Nodes.Any(node => node.Id == InstanceId(from))
            || target is null
            || !target.HandledSignals.Contains(request.SignalType, StringComparer.Ordinal))
        {
            throw new NeuronAuthorizationException("The target does not handle this signal in the current graph scope.");
        }

        var existing = snapshot.Synapses.FirstOrDefault(edge => edge.SourceId == InstanceId(from)
            && edge.TargetId == InstanceId(to) && edge.SignalType == request.SignalType);
        if (existing?.Kind == nameof(SynapseKind.Innate))
        {
            throw new NeuronAuthorizationException("Innate connections cannot be changed from the graph.");
        }

        if (!request.Subscribed && existing is not null && !existing.CanUnsubscribe)
        {
            throw new NeuronAuthorizationException("Only explicit Bound subscriptions can be removed here.");
        }

        if (request.Subscribed || existing is not null)
        {
            using var principal = VerifiedActor.Enter(actor);
            Signal signal = request.Subscribed
                ? new Subscribe(from, request.SignalType)
                : new Unsubscribe(from, request.SignalType);
            var outcome = await source.SendAsync(to, signal, cancellationToken).ConfigureAwait(false);
            if (outcome != DeliveryOutcome.Handled)
            {
                throw new NeuronAuthorizationException("The neuron did not accept the subscription change.");
            }
        }

        return new(InstanceId(from), InstanceId(to), request.SignalType, request.Subscribed);
    }

    internal static string InstanceId(NeuronId neuron) => $"{neuron.Type}:{neuron.Name}";

    private bool CanSee(NeuronId neuron, PrincipalId principal)
        => neuron.Owner == source.Owner
            && (!PrincipalPartition.TryParse(neuron.Name, out var other, out _) || other == principal);

    private static bool IsPrivate(NeuronId neuron, PrincipalId principal, NeuronId? execution)
        => PrincipalPartition.OwnsInstance(principal, neuron.Name) || neuron == execution;

    private static bool VisibleDelivery(SignalDelivery delivery, bool privateNeuron, PrincipalId principal)
        => delivery.Principal == principal || (privateNeuron && delivery.Principal is null);

    private bool VisibleCaller(NeuronId caller, PrincipalId principal) => CanSee(caller, principal);

    private IEnumerable<BrainGraphActivity> ProjectActivity(
        NeuronId neuron, JournalRead journal, JournalKind direction, bool privateNeuron, PrincipalId principal)
    {
        for (var index = 0; index < journal.Delta.Count; index++)
        {
            var delivery = journal.Delta[index];
            if (!VisibleDelivery(delivery, privateNeuron, principal))
            {
                continue;
            }
            var sequence = journal.ResumeSequence - journal.Delta.Count + index + 1;
            var type = delivery.Signal.GetType().Name;
            var (summary, preview) = Summarize(delivery.Signal);
            yield return new($"{InstanceId(neuron)}:{direction}:{sequence}", InstanceId(neuron), direction.ToString(),
                sequence, type, delivery.Timestamp,
                VisibleCaller(delivery.Caller, principal) ? InstanceId(delivery.Caller) : "",
                delivery.CorrelationId.ToString(), summary, preview);
        }
    }

    // Explicit allowlist: never serialize arbitrary Signal objects, tool credentials,
    // prompt text, document contents, OAuth URLs, or exception details into the graph.
    internal static (string Summary, IReadOnlyDictionary<string, string>? Preview) Summarize(Signal signal)
        => signal switch
        {
            TurnLifecycle turn => ($"Turn {turn.Status.ToString().ToLowerInvariant()}",
                new Dictionary<string, string> { ["status"] = turn.Status.ToString(), ["turnId"] = turn.TurnId.ToString() }),
            UserMessaged message => ("Message received", new Dictionary<string, string>
                { ["characters"] = message.Text.Length.ToString(CultureInfo.InvariantCulture) }),
            Responded response => ("Assistant response recorded", new Dictionary<string, string>
                { ["characters"] = response.Text.Length.ToString(CultureInfo.InvariantCulture) }),
            Subscribe subscription => ("Subscription bound", new Dictionary<string, string>
                { ["signalType"] = subscription.SignalType }),
            Unsubscribe subscription => ("Subscription removed", new Dictionary<string, string>
                { ["signalType"] = subscription.SignalType }),
            _ => ($"{signal.GetType().Name} observed · payload omitted", null),
        };

    private static string SynapseId(Synapse edge)
        => $"{InstanceId(edge.Source)}|{edge.SignalType}|{InstanceId(edge.Target)}";
}
