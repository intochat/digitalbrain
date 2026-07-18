using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ino.Core;
using Ino.Core.Brain;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Kernel;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Ino.Gateway;

/// <summary>
/// Default implementation of <see cref="IInoGateway"/>. Routes every inbound
/// chat message through <see cref="AskAsync"/>, which resolves the per-(userId,
/// sessionId) <see cref="IInoNeuron"/> grain and delegates to its
/// <c>ICortexCapability</c>. The gateway stays neuron-agnostic — installing
/// a new neuron in Cortex's registry makes it routable without touching
/// this class.
/// </summary>
public sealed class InoGateway(
    IFirePort firePort,
    IInoEventBus events,
    ISynapseJournal journal,
    IReasoningProbe reasoningProbe,
    IGrainFactory grainFactory,
    ILogger<InoGateway> log) : IInoGateway
{
    readonly ConcurrentDictionary<string, string> _userSessions = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<SynapseJournalEntry>> GetJournalAsync(
        string? neuronId,
        int limit,
        CancellationToken ct = default)
    {
        var safeLimit = limit <= 0 ? 50 : Math.Min(limit, InMemorySynapseJournal.Capacity);
        return Task.FromResult(journal.Recent(neuronId, safeLimit));
    }

    public Task<NeuronMetricsSnapshot> GetMetricsAsync(
        string? neuronId,
        CancellationToken ct = default) => Task.FromResult(journal.Metrics(neuronId));

    public Task<NeuronReasoning> GetReasoningAsync(
        string neuronId,
        CancellationToken ct = default)
    {
        if (reasoningProbe.TryGet(neuronId, out var hit))
        {
            return Task.FromResult(new NeuronReasoning(
                NeuronId: neuronId,
                Source: hit.Source,
                ScenarioName: hit.ScenarioName,
                Text: $"mocked via BDD · {hit.FeatureTitle} — {hit.ScenarioName}\nprompt: {hit.Prompt}\nreply: {hit.Reply}"));
        }

        return Task.FromResult(new NeuronReasoning(
            NeuronId: neuronId,
            Source: "bdd-mock",
            ScenarioName: string.Empty,
            Text: $"No BDD scenario has matched a prompt for {neuronId} yet. Fire a chat that your neuron Features/*.feature file covers."));
    }

    public async IAsyncEnumerable<InoEvent> StreamEventsAsync(
        string userId,
        IReadOnlyList<string>? eventTypes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        log.LogInformation("stream-events: user={UserId} filter={Filter}",
            userId, eventTypes is null ? "<all>" : string.Join(",", eventTypes));

        var filter = eventTypes is { Count: > 0 }
            ? new HashSet<string>(eventTypes, StringComparer.Ordinal)
            : null;

        await foreach (var evt in events.SubscribeAsync(userId, ct))
        {
            if (filter is null || filter.Contains(evt.Type))
                yield return evt;
        }
    }

    static readonly ActivitySource ActivitySource = new("ino");

    public async IAsyncEnumerable<ChatResult> ChatAsync(
        string message,
        string userId,
        string? correlationId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var span = ActivitySource.StartActivity("ino.gateway.chat", ActivityKind.Internal);
        span?.SetTag("ino.user.id", userId);
        span?.SetTag("ino.chat.message_length", message.Length);

        // Every Chat() turn starts a *new* conversation thread — the client's
        // Send-message path is "begin a new ask", not "continue clarifying".
        // We mint a fresh correlation_id and overwrite the user's session
        // slot so any subsequent FireSynapse (chip tap) lands on this turn's
        // grain activation. If the client did echo a correlation_id back
        // (proper proto-aware client), we honour it for forward-compat.
        var corrId = string.IsNullOrWhiteSpace(correlationId)
            ? CorrelationId.New()
            : new CorrelationId(correlationId);
        _userSessions[userId] = corrId.Value;
        span?.SetTag("ino.correlation_id", corrId.Value);

        log.LogInformation(
            "gateway chat: user={UserId} correlation={CorrelationId} message={Message}",
            userId, corrId.Value, message);

        // Generic "Searching…" placeholder — a single gRPC-round-trip acknowledgement
        // so the client can paint a pending state while Cortex dispatches. Neurons that
        // stream their own progressive frames (slice 8's ItineraryComposer) will emit
        // richer skeleton payloads downstream of this.
        yield return ChatResult.Text(reply: "Searching…", neuronId: "cortex", correlationId: corrId.Value);

        NeuronResult result;
        Exception? handlerError = null;
        try
        {
            var ino = await AskAsync(message, userId, InoNeuronGrainKey.DefaultSessionId, corrId.Value, ct);
            result = ino.Success
                ? (ino.Rfw is { } inoRfw
                    ? NeuronResult.Ok(ino.Text).WithRfwPayload(inoRfw)
                    : NeuronResult.Ok(ino.Text))
                : NeuronResult.Fail(SynapseErrorCode.NoCanonicalHandler, ino.Text);
        }
        catch (OperationCanceledException oce)
        {
            handlerError = oce;
            result = NeuronResult.Fail(SynapseErrorCode.Cancelled, oce.Message);
            log.LogInformation("AskAsync cancelled for {Message}", message);
        }
        catch (Exception ex)
        {
            handlerError = ex;
            result = NeuronResult.Fail(SynapseErrorCode.NoCanonicalHandler, ex.Message);
            log.LogError(ex, "AskAsync threw on {Message}", message);
        }

        if (handlerError is not null)
        {
            span?.SetStatus(ActivityStatusCode.Error, handlerError.Message);
            yield return ChatResult.Text(
                reply: $"Routing error: {handlerError.GetType().Name}: {handlerError.Message}",
                neuronId: "cortex",
                correlationId: corrId.Value);
            yield break;
        }

        // Slice 4 path — structured RfwPayload on NeuronResult takes precedence.
        if (result.Success && result.Rfw is { } payload)
        {
            var contentType = $"rfw/{payload.LibraryName}";
            span?.SetTag("ino.chat.content_type", contentType);
            span?.SetStatus(ActivityStatusCode.Ok);
            yield return ChatResult.WithRfw(
                reply: result.Message ?? "Done",
                neuronId: "cortex",
                contentType: contentType,
                description: payload.DescriptionDsl,
                data: payload.DataPayload,
                correlationId: corrId.Value);
            yield break;
        }

        // Pre-Slice-4 path — RFW smuggled in via ResponsePayload+IHasRfwPayload.
        if (result.Success && result.ResponsePayload is IHasRfwPayload rfw)
        {
            span?.SetTag("ino.chat.content_type", rfw.ContentType);
            span?.SetStatus(ActivityStatusCode.Ok);
            yield return ChatResult.WithRfw(
                reply: result.Message ?? "Done",
                neuronId: "cortex",
                contentType: rfw.ContentType,
                description: rfw.RfwDescription,
                data: rfw.RfwData,
                correlationId: corrId.Value);
            yield break;
        }

        span?.SetStatus(
            result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            result.Error?.Message);
        yield return ChatResult.Text(
            reply: result.Message ?? "(no reply)",
            neuronId: "cortex",
            correlationId: corrId.Value);
    }

    public async Task<InoResponse> AskAsync(
        string prompt,
        string userId,
        string sessionId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        using var span = ActivitySource.StartActivity("ino.gateway.ask", ActivityKind.Internal);
        span?.SetTag("ino.user.id", userId);
        span?.SetTag("ino.session.id", sessionId);

        var corrId = string.IsNullOrWhiteSpace(correlationId) ? CorrelationId.New() : new CorrelationId(correlationId);
        span?.SetTag("ino.correlation_id", corrId.Value);

        RequestContext.Set(InoRequestContextKeys.UserId, userId);
        RequestContext.Set(InoRequestContextKeys.SessionId, sessionId);
        try
        {
            var grain = grainFactory.GetGrain<IInoNeuron>(InoNeuronGrainKey.Format(userId, sessionId));
            return await grain.AskAsync(prompt, corrId.Value, ct);
        }
        finally
        {
            RequestContext.Clear();
        }
    }

    public const string ProvideClarificationVerb = "ino.core.provide-clarification";

    public async Task<FireResult> FireSynapseAsync(
        string verb,
        IReadOnlyDictionary<string, string> args,
        string correlationId,
        string userId,
        CancellationToken ct = default)
    {
        // Codegen-stale clients can't echo correlation_id back yet — fall
        // through to the user-session table so a chip tap still pins the
        // fire to the conversation's grain activation.
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            if (!_userSessions.TryGetValue(userId, out var stored))
                throw new InvalidOperationException(
                    $"No active conversation for user '{userId}'. Send a Chat() turn first to start a conversation, " +
                    "or upgrade the client to echo correlation_id from the most recent ChatResponse on FireSynapse.");
            correlationId = stored;
        }

        using var span = ActivitySource.StartActivity("ino.gateway.fire", ActivityKind.Internal);
        span?.SetTag("ino.user.id", userId);
        span?.SetTag("ino.fire.verb", verb);
        span?.SetTag("ino.correlation_id", correlationId);

        var corrId = new CorrelationId(correlationId);
        var synapseId = SynapseId.New();
        var ctx = new NeuronContext(
            SynapseId: synapseId,
            CorrelationId: corrId,
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway>"),
            UserId: userId)
        {
            FirePort = firePort,
            Logger = log,
        };

        ISynapse synapse = verb switch
        {
            ProvideClarificationVerb => BuildProvideClarification(args),
            _ => throw new NotSupportedException(
                $"Verb '{verb}' is not understood by the v0.1 gateway. " +
                $"Supported verbs: {ProvideClarificationVerb}."),
        };

        log.LogInformation(
            "gateway fire: user={UserId} correlation={CorrelationId} verb={Verb} args={Args}",
            userId, correlationId, verb, string.Join(",", args.Select(kv => $"{kv.Key}={kv.Value}")));

        var result = await firePort.Fire(synapse, ctx, ct);

        if (result.Success && result.Rfw is { } payload)
        {
            var contentType = $"rfw/{payload.LibraryName}";
            span?.SetTag("ino.fire.content_type", contentType);
            span?.SetStatus(ActivityStatusCode.Ok);
            return new FireResult(
                Success: true,
                SynapseId: synapseId.Value,
                Reply: result.Message ?? "Done",
                ContentType: contentType,
                RfwDescription: payload.DescriptionDsl,
                RfwData: payload.DataPayload,
                CorrelationId: correlationId);
        }

        if (result.Success && result.ResponsePayload is IHasRfwPayload rfw)
        {
            span?.SetTag("ino.fire.content_type", rfw.ContentType);
            span?.SetStatus(ActivityStatusCode.Ok);
            return new FireResult(
                Success: true,
                SynapseId: synapseId.Value,
                Reply: result.Message ?? "Done",
                ContentType: rfw.ContentType,
                RfwDescription: rfw.RfwDescription,
                RfwData: rfw.RfwData,
                CorrelationId: correlationId);
        }

        span?.SetStatus(
            result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            result.Error?.Message);

        return new FireResult(
            Success: result.Success,
            SynapseId: synapseId.Value,
            Reply: result.Message ?? (result.Error?.Message ?? "(no reply)"),
            ContentType: "text",
            RfwDescription: ReadOnlyMemory<byte>.Empty,
            RfwData: ReadOnlyMemory<byte>.Empty,
            CorrelationId: correlationId);
    }

    static ProvideClarification BuildProvideClarification(IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("field", out var field) || string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("ino.core.provide-clarification requires arg 'field'.");
        if (!args.TryGetValue("value", out var value) || value is null)
            throw new ArgumentException("ino.core.provide-clarification requires arg 'value'.");
        return new ProvideClarification(field, value);
    }

    // ── Inspector E.3 — Slice 3B ──────────────────────────────────────────────

    public async Task<IReadOnlyList<ProposalEntry>> ListProposalsAsync(
        string userId, ProposalStatus? filter, int skip, int take, CancellationToken ct = default)
    {
        var grain = grainFactory.GetGrain<IProposalLog>("singleton");
        var entries = await grain.ListAsync(filter, skip, take);
        // Filter by userId for the per-user view (all-users view when userId is "admin" or system).
        return entries.Where(e => e.UserId == userId).ToArray();
    }

    public async Task DecideProposalAsync(
        string userId, string proposalId, ProposalStatus decision, CancellationToken ct = default)
    {
        if (decision == ProposalStatus.Pending)
            throw new ArgumentException("Pending is not a valid decision.", nameof(decision));

        var registryGrain = grainFactory.GetGrain<INeuronRegistry>(0);
        var ok = decision == ProposalStatus.Approved
            ? await registryGrain.ApproveAsync(proposalId, userId, ct)
            : await registryGrain.RejectAsync(proposalId, userId, ct);

        if (!ok) return;  // unknown proposal or already decided; no broadcast.

        // Broadcast ProposalDecided so ProposalLog updates its state.
        // Registry.ApproveAsync fires NeuronCreated for the auto-register path;
        // we fire ProposalDecided here for the Reject path and to keep ProposalLog
        // as a single consistent read-model for both outcomes.
        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway-inspector>"),
            UserId: userId)
        {
            FirePort = firePort,
            Logger = log,
        };
        await firePort.FireBroadcast(
            new ProposalDecided(proposalId, decision, userId, DateTimeOffset.UtcNow),
            ctx, ct);
    }

    public async Task<IReadOnlyList<RoutingDecision>> ListRoutingDecisionsAsync(
        string userId, int count, CancellationToken ct = default)
    {
        var grain = grainFactory.GetGrain<ICortexJournal>("singleton");
        return await grain.GetRecentAsync(userId, Math.Min(count, 20));
    }

    // ── Inspector debug fire — Slice C.4 ──────────────────────────────────────

    // Lazy reflective dispatch cache: maps synapse Type → MethodInfo for Fire<T>.
    static readonly MethodInfo FirePortFireOpenGeneric =
        typeof(IFirePort).GetMethod(nameof(IFirePort.Fire))!;
    static readonly ConcurrentDictionary<Type, MethodInfo> FireMethodCache = new();

    public async Task<FireResult> FireTestSynapseAsync(
        string synapseType,
        string payloadJson,
        string sourceNodeId,
        string userId,
        CancellationToken ct = default)
    {
        var resolvedType = ResolveISynapseType(synapseType);
        if (resolvedType is null)
            throw new ArgumentException(
                $"No ISynapse implementation named '{synapseType}' found in loaded assemblies.");

        ISynapse synapse;
        try
        {
            var deserialized = JsonSerializer.Deserialize(payloadJson, resolvedType);
            if (deserialized is not ISynapse typed)
                throw new ArgumentException(
                    $"payload_json did not deserialize as {resolvedType.Name}.");
            synapse = typed;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"payload_json is not valid JSON for {resolvedType.Name}: {ex.Message}", ex);
        }

        var corrId = CorrelationId.New();
        var synapseId = SynapseId.New();
        var ctx = new NeuronContext(
            SynapseId: synapseId,
            CorrelationId: corrId,
            Source: new Caller.Ambient(DomainId.From(
                string.IsNullOrWhiteSpace(sourceNodeId) ? "inspector" : sourceNodeId)),
            SourceStream: new StreamKey("<inspector>"),
            UserId: userId)
        {
            FirePort = firePort,
            Logger = log,
        };

        log.LogInformation(
            "gateway fire-test-synapse: user={UserId} correlation={CorrelationId} synapseType={SynapseType}",
            userId, corrId.Value, resolvedType.Name);

        var fireMethod = FireMethodCache.GetOrAdd(
            resolvedType,
            t => FirePortFireOpenGeneric.MakeGenericMethod(t));

        var task = (Task<NeuronResult>)fireMethod.Invoke(firePort, [synapse, ctx, ct])!;
        var result = await task;

        return new FireResult(
            Success: result.Success,
            SynapseId: synapseId.Value,
            Reply: result.Message ?? (result.Error?.Message ?? "(no reply)"),
            ContentType: "text",
            RfwDescription: ReadOnlyMemory<byte>.Empty,
            RfwData: ReadOnlyMemory<byte>.Empty,
            CorrelationId: corrId.Value);
    }

    static Type? ResolveISynapseType(string shortName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name == shortName && typeof(ISynapse).IsAssignableFrom(type) && !type.IsAbstract)
                    return type;
            }
        }
        return null;
    }

    public async Task<NeuronResult> HandleRfwEventAsync(
        string correlationId,
        string eventName,
        IReadOnlyDictionary<string, string> args,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(args);

        using var span = ActivitySource.StartActivity("ino.gateway.rfw_event", ActivityKind.Internal);
        span?.SetTag("ino.correlation_id", correlationId);
        span?.SetTag("ino.rfw.event_name", eventName);

        var registry = grainFactory.GetGrain<ICorrelationRegistry>("singleton");
        var entry = await registry.GetAsync(correlationId);
        if (entry is null)
        {
            log.LogWarning("rfw event: unknown correlation {CorrelationId} — trip may have expired", correlationId);
            span?.SetStatus(ActivityStatusCode.Error, "unknown correlation");
            return NeuronResult.Ok("Sorry — that trip has expired. Send a new message to start over.");
        }

        var planType = Type.GetType(entry.PlanInterfaceAqn);
        if (planType is null)
        {
            log.LogWarning(
                "rfw event: plan interface {Aqn} not loadable in this assembly — RfwEvent callbacks need the plan's contracts assembly to be reachable from the gateway",
                entry.PlanInterfaceAqn);
            return NeuronResult.Ok("Sorry — couldn't dispatch that event.");
        }

        // The plan's typed interface (e.g. ITripPlanner) extends
        // IRfwEventHandler, so the grain reference proxy generated for the
        // typed interface implements both directly — the cast is a normal
        // hierarchy downcast that Orleans's grain ref handles without
        // sibling-interface activator gymnastics.
        var typedRef = grainFactory.GetGrain(planType, entry.GrainKey);
        if (typedRef is not IRfwEventHandler handler)
        {
            log.LogWarning(
                "rfw event: plan {PlanType} does not implement IRfwEventHandler — cannot dispatch {EventName}",
                planType.FullName, eventName);
            return NeuronResult.Ok($"Sorry — the {eventName} event isn't supported here.");
        }
        try
        {
            return await handler.HandleRfwEventAsync(eventName, args, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "rfw event: handler {Plan} threw on {EventName}", planType.FullName, eventName);
            span?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return NeuronResult.Fail(SynapseErrorCode.NoCanonicalHandler,
                $"Couldn't process the {eventName} event: {ex.GetType().Name}");
        }
    }
}
