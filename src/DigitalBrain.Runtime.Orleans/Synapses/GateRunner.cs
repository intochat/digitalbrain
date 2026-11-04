using System.Text.Json;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The gate path of a hosted write, as TestOS runs it: before a publisher's durable signals are appended, every gate neuron the
/// <see cref="IGateIndex"/> names (other than the publisher) runs its gate synapses as a turn of its host grain; the first refusal,
/// a gate that does not answer within <see cref="RuntimeOptions.GateTimeout"/>, or a failing gate stops the write with
/// <see cref="RuleViolationException"/>. After the append, <see cref="Commit"/> asks each allowing gate to save its next state.
/// <para>
/// Interim rule of this slice for reaction edges: the Orleans runtime cannot list the live neurons of a brain, so the publisher side
/// ensures them. Before the append, for each durable signal whose type a <c>[State]</c> reaction synapse of the catalog takes, the
/// synapse <c>publisher → {brain}/{StateAlias}/{Key}</c> is ensured on the subscriber through the ordinary admission path, once per
/// (publisher, subscriber, signal) per publisher activation. Install-time edges replace this rule in a later plan.
/// </para>
/// <para>
/// Gates never run on <see cref="StateChanged"/>: a gate's <c>Committed</c> saves <see cref="StateChanged"/> while the original publisher
/// is still in its turn awaiting <see cref="Commit"/>, so a gate on it that resolved to that publisher would deadlock. Reaction synapses
/// on <see cref="StateChanged"/> are still ensured. Nor on <see cref="Rejected"/>: the refusing gate journals it inside its own gate
/// turn, so a gate on it (say, one on base <see cref="Signal"/>) would run nested in that turn.
/// </para>
/// <para>
/// No call of the gate path can stall a publisher: <see cref="Commit"/> and each reaction-edge ensure are bounded by
/// <see cref="RuntimeOptions.GateTimeout"/> (logged and skipped on timeout), and no edge is ensured on a subscriber that is itself a
/// publisher awaiting its gates or <see cref="Commit"/> along the current call chain (tracked in <see cref="RequestContext"/> under
/// <c>HostNeuronContext.GateChainKey</c>): that subscriber's turn is blocked on this very chain, so the ensure would deadlock.
/// Such an edge is ensured on a later publish.
/// </para>
/// </summary>
internal static partial class GateRunner
{
    private const string NoAnswer = "the gate did not answer";

    /// <summary>Ensures the reaction edges, then runs the gates of every durable signal in order.</summary>
    /// <param name="services">The silo services.</param>
    /// <param name="publisher">The publishing neuron.</param>
    /// <param name="signals">The signals about to be appended; ephemeral ones pass no gate.</param>
    /// <param name="ensured">The publisher activation's cache of ensured reaction edges.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>Each allowing gate with the signal, its proposed next state and the ticket of its provisional entry.</returns>
    /// <exception cref="RuleViolationException">A gate refused, did not answer or failed.</exception>
    public static async Task<IReadOnlyList<(INeuronHost Gate, Signal Signal, JsonElement Next, Guid Ticket)>> Run(IServiceProvider services, NeuronId publisher,
        IReadOnlyList<Signal> signals, ISet<string> ensured, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(ensured);
        var durable = signals.Where(signal => !signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false)).ToArray();
        var gated = durable.Where(signal => signal is not StateChanged and not Rejected).ToArray();
        if (durable.Length == 0 || services.GetService<IGateIndex>() is not { } index || services.GetService<SynapseCatalog>() is not { } catalog) { return []; }
        var payloads = services.GetRequiredService<JournalPayloads>();
        var grains = services.GetRequiredService<IGrainFactory>();
        var types = services.GetRequiredService<NeuronTypeRegistry>();
        var timeout = RuntimeOptions.From(services).GateTimeout;
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(GateRunner)) ?? NullLogger.Instance;
        await EnsureReactions(catalog, payloads, grains, types, publisher, durable, ensured, timeout, log).ConfigureAwait(true);
        var allowed = new List<(INeuronHost Gate, Signal Signal, JsonElement Next, Guid Ticket)>();
        var chain = EnterChain(publisher);
        try
        {
            foreach (var signal in gated)
            {
                foreach (var (gate, descriptor) in index.GatesFor(publisher.Brain, signal.GetType()))
                {
                    if (gate == publisher) { continue; }
                    var host = Host(grains, types, gate);
                    var (alias, json) = Encode(payloads, signal);
                    var verdict = await Ask(host, alias, json, descriptor, timeout, log, ct).ConfigureAwait(true);
                    if (!verdict.Allowed) { throw new RuleViolationException(new Rejected(descriptor.Name, verdict.Rules, verdict.Messages)); }
                    // An allowance without a next state cannot be committed: it fails closed like a gate that did not answer.
                    if (verdict.Next is not { } next)
                    {
                        allowed.Add((host, signal, default, verdict.Ticket));
                        throw new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
                    }
                    allowed.Add((host, signal, next, verdict.Ticket));
                }
            }
        }
        catch
        {
            await Abort(services, allowed).ConfigureAwait(true);
            throw;
        }
        finally { LeaveChain(chain); }
        return allowed;
    }

    /// <summary>
    /// The publish did not happen (a later gate stopped it, or the append threw): each allowing gate drops its provisional state. A
    /// failing <c>Aborted</c> is logged, never thrown; the gate's <see cref="StateNeuron.ProvisionalLease"/> then discards the entry.
    /// </summary>
    /// <param name="services">The silo services.</param>
    /// <param name="allowed">What <see cref="Run"/> returned, or the gates that allowed before a stop.</param>
    /// <returns>A task that completes when every gate was told.</returns>
    public static async Task Abort(IServiceProvider services, IReadOnlyList<(INeuronHost Gate, Signal Signal, JsonElement Next, Guid Ticket)> allowed)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(allowed);
        if (allowed.Count == 0) { return; }
        var payloads = services.GetRequiredService<JournalPayloads>();
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(GateRunner)) ?? NullLogger.Instance;
        foreach (var (gate, signal, _, ticket) in allowed)
        {
            try
            {
                var (alias, json) = Encode(payloads, signal);
                await gate.Aborted(alias, json, ticket).ConfigureAwait(true);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                LogGateAbortFailed(log, error, gate.GetPrimaryKeyString(), signal.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Once the publisher's append is durable, saves the next state of each allowing gate, each as a turn of its gate neuron. The
    /// publisher's entry and the gates' entries are not one transaction: a failing <c>Committed</c> is logged, never thrown to the publisher.
    /// </summary>
    /// <param name="services">The silo services.</param>
    /// <param name="publisher">The publishing neuron, blocked on this call until every gate answered or timed out.</param>
    /// <param name="allowed">What <see cref="Run"/> returned.</param>
    /// <returns>A task that completes when every gate was asked.</returns>
    public static async Task Commit(IServiceProvider services, NeuronId publisher, IReadOnlyList<(INeuronHost Gate, Signal Signal, JsonElement Next, Guid Ticket)> allowed)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(allowed);
        if (allowed.Count == 0) { return; }
        var payloads = services.GetRequiredService<JournalPayloads>();
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(GateRunner)) ?? NullLogger.Instance;
        var timeout = RuntimeOptions.From(services).GateTimeout;
        var chain = EnterChain(publisher);
        try
        {
            foreach (var (gate, signal, next, ticket) in allowed)
            {
                try
                {
                    var (alias, json) = Encode(payloads, signal);
                    // Bounded: a gate whose save stalls must not hold the publisher's turn; a timeout is logged like a failure.
                    await gate.Committed(alias, json, next, ticket).WaitAsync(timeout).ConfigureAwait(true);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    LogGateCommitFailed(log, error, gate.GetPrimaryKeyString(), signal.GetType().Name);
                }
            }
        }
        finally { LeaveChain(chain); }
    }

    // The publishers along the current call chain that are blocked in Run or Commit.
    private static string[] Chain()
        => RequestContext.Get(HostNeuronContext.GateChainKey) is string chain ? chain.Split(',', StringSplitOptions.RemoveEmptyEntries) : [];

    // Adds the publisher to the chain for the calls it makes while blocked; returns the previous value to restore.
    private static object? EnterChain(NeuronId publisher)
    {
        var previous = RequestContext.Get(HostNeuronContext.GateChainKey);
        var id = publisher.ToString();
        var current = Chain();
        if (!current.Contains(id, StringComparer.Ordinal)) { RequestContext.Set(HostNeuronContext.GateChainKey, string.Join(',', [.. current, id])); }
        return previous;
    }

    private static void LeaveChain(object? previous)
    {
        if (previous is null) { RequestContext.Remove(HostNeuronContext.GateChainKey); }
        else { RequestContext.Set(HostNeuronContext.GateChainKey, previous); }
    }

    // A gate that does not answer within the timeout, or fails, refuses; its call is cancelled, and an Aborted without a ticket (the
    // verdict never arrived) is sent behind it, not awaited, so a gate that ignores the cancellation and allows late leaves no entry.
    private static async Task<GateVerdict> Ask(INeuronHost host, string alias, JsonElement json, SynapseDescriptor descriptor, TimeSpan timeout, ILogger log, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var call = host.Gate(alias, json, cancel.Token);
        try { return await call.WaitAsync(timeout, ct).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await cancel.CancelAsync().ConfigureAwait(true);
            _ = call.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            var refused = new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
            var gate = host.GetPrimaryKeyString();
            _ = host.Aborted(alias, json, Guid.Empty, CancellationToken.None).ContinueWith(t => LogTimedOutGateAbortFailed(log, t.Exception!, gate, alias),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw refused;
        }
    }

    // The interim reaction-edge rule (see the type summary): each (subscriber, signal) is ensured once per publisher activation.
    private static async Task EnsureReactions(SynapseCatalog catalog, JournalPayloads payloads, IGrainFactory grains, NeuronTypeRegistry types,
        NeuronId publisher, Signal[] signals, ISet<string> ensured, TimeSpan timeout, ILogger log)
    {
        // A subscriber blocked further up this call chain cannot take the ensure call; its edge waits for a later publish.
        var blocked = Chain();
        foreach (var signal in signals)
        {
            if (!payloads.Types.TryAliasOf(signal.GetType(), out var alias)) { continue; }
            var subscribers = catalog.Synapses
                .Where(d => d.Kind == SynapseKind.Reaction && d.Signal!.IsInstanceOfType(signal))
                .Select(d => new NeuronId(publisher.Brain, d.StateAlias, d.Key))
                .Where(subscriber => subscriber != publisher && !blocked.Contains(subscriber.ToString(), StringComparer.Ordinal))
                .Distinct();
            foreach (var subscriber in subscribers)
            {
                var edge = $"{subscriber}|{alias}";
                if (!ensured.Add(edge)) { continue; }
                var host = grains.GetGrain<ISynapseHost>(GrainId.Create(NeuronTypeRegistry.GrainTypeOf(types.Require(subscriber.Type).Policy), subscriber.ToString()));
                var previous = RequestContext.Get(HostNeuronContext.CallerKey);
                RequestContext.Set(HostNeuronContext.CallerKey, publisher.ToString());
                try { await host.EnsureFrom(publisher, [alias]).WaitAsync(timeout).ConfigureAwait(true); }
                catch (TimeoutException)
                {
                    // Bounded like a gate: log, forget the edge so a later publish ensures it again, and go on with the publish.
                    ensured.Remove(edge);
                    LogReactionEnsureTimedOut(log, subscriber.ToString(), alias);
                }
                catch
                {
                    ensured.Remove(edge);
                    throw;
                }
                finally
                {
                    if (previous is null) { RequestContext.Remove(HostNeuronContext.CallerKey); }
                    else { RequestContext.Set(HostNeuronContext.CallerKey, previous); }
                }
            }
        }
    }

    private static INeuronHost Host(IGrainFactory grains, NeuronTypeRegistry types, NeuronId id)
        => grains.GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.GrainTypeOf(types.Require(id.Type).Policy), id.ToString()));

    private static (string Alias, JsonElement Json) Encode(JournalPayloads payloads, Signal signal)
    {
        var payload = payloads.EncodeSignal(signal);
        return (payload.Type, JsonElement.Parse(payload.Json));
    }

    [LoggerMessage(EventId = 4124, EventName = "GateCommitFailed", Level = LogLevel.Warning,
        Message = "The gate {Gate} could not save its next state for {Signal} after the publisher's append; the gate lags one signal behind.")]
    private static partial void LogGateCommitFailed(ILogger logger, Exception error, string gate, string signal);

    [LoggerMessage(EventId = 4125, EventName = "GateAbortFailed", Level = LogLevel.Warning,
        Message = "The gate {Gate} could not drop its provisional state for {Signal}; its lease discards it.")]
    private static partial void LogGateAbortFailed(ILogger logger, Exception error, string gate, string signal);

    [LoggerMessage(EventId = 4127, EventName = "TimedOutGateAbortFailed", Level = LogLevel.Debug,
        Message = "The gate {Gate} that did not answer could not drop a late provisional state for {Signal}; its lease discards it.")]
    private static partial void LogTimedOutGateAbortFailed(ILogger logger, Exception error, string gate, string signal);

    [LoggerMessage(EventId = 4126, EventName = "ReactionEnsureTimedOut", Level = LogLevel.Warning,
        Message = "The reaction edge to {Subscriber} for {Signal} was not ensured within the gate timeout; a later publish ensures it.")]
    private static partial void LogReactionEnsureTimedOut(ILogger logger, string subscriber, string signal);
}
