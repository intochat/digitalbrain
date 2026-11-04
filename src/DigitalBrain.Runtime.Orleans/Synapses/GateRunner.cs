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
/// The index serves only installed gates (<see cref="InstalledGateIndex"/>): before the first gated signal of a brain the
/// <see cref="InstalledGates"/> cache reads the brain's installs. Reaction edges are not this path's concern: the install wires them
/// (<see cref="OrleansInstallHooks"/>).
/// </para>
/// <para>
/// Gates never run on <see cref="StateChanged"/>: a gate's <c>Committed</c> saves <see cref="StateChanged"/> while the original publisher
/// is still in its turn awaiting <see cref="Commit"/>, so a gate on it that resolved to that publisher would deadlock. Nor on
/// <see cref="Rejected"/>: the refusing gate journals it inside its own gate turn, so a gate on it (say, one on base
/// <see cref="Signal"/>) would run nested in that turn. Nor on <see cref="DigitalBrain.Time.Tick"/>, <see cref="Installed"/>,
/// <see cref="Retired"/>, <see cref="InstallRequested"/>, <see cref="InstallDenied"/>, <see cref="Deployed"/>, <see cref="Staged"/>, <see cref="Undeployed"/> and the time registrations: their publishers (the time, installs and sources neurons) serve the gate path itself.
/// </para>
/// <para>
/// No call of the gate path can stall a publisher: <see cref="Commit"/> is bounded by <see cref="RuntimeOptions.GateTimeout"/> (logged
/// and skipped on timeout). The publishers along the current call chain blocked in <see cref="Run"/> or <see cref="Commit"/> are
/// tracked in <see cref="RequestContext"/> under <c>HostNeuronContext.GateChainKey</c>.
/// </para>
/// </summary>
internal static partial class GateRunner
{
    private const string NoAnswer = "the gate did not answer";
    private const string NoGates = "the installed gates did not load";

    /// <summary>Runs the installed gates of every durable signal in order.</summary>
    /// <param name="services">The silo services.</param>
    /// <param name="publisher">The publishing neuron.</param>
    /// <param name="signals">The signals about to be appended; ephemeral ones pass no gate.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>Each allowing gate with the signal, its proposed next state and the ticket of its provisional entry.</returns>
    /// <exception cref="RuleViolationException">A gate refused, did not answer or failed, or the installed gates did not load within <c>GateTimeout</c>.</exception>
    public static async Task<IReadOnlyList<(INeuronHost Gate, Signal Signal, JsonElement Next, Guid Ticket)>> Run(IServiceProvider services, NeuronId publisher,
        IReadOnlyList<Signal> signals, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signals);
        var gated = signals.Where(signal => !signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false) && !Ungated(signal)).ToArray();
        if (gated.Length == 0 || services.GetService<IGateIndex>() is not { } index) { return []; }
        var timeout = RuntimeOptions.From(services).GateTimeout;
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(GateRunner)) ?? NullLogger.Instance;
        // The installs neuron's own publishes neither load its brain's installs nor restore: it serves both.
        var installsNeuron = publisher.Type == OrleansInstallHooks.InstallsType;
        if (!installsNeuron && services.GetService<InstalledGates>() is { } installed)
        {
            // The installs neuron that serves the index may be slow or wedged: the publish waits no longer than for a gate, and fails closed.
            try { await installed.Load(publisher.Brain).WaitAsync(timeout, ct).ConfigureAwait(true); }
            catch (TimeoutException)
            {
                LogInstalledGatesTimedOut(log, publisher.ToString(), timeout);
                throw new RuleViolationException(new Rejected(OrleansInstallHooks.InstallsType, [OrleansInstallHooks.InstallsType], [NoGates]));
            }
        }
        if (!installsNeuron) { await RestoreInstalled(services, index, publisher.Brain, timeout, log, ct).ConfigureAwait(true); }
        var payloads = services.GetRequiredService<JournalPayloads>();
        var grains = services.GetRequiredService<IGrainFactory>();
        var types = services.GetRequiredService<NeuronTypeRegistry>();
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
                    // One attempt id per gate call of this publish: the gate's entry and its ticket carry it.
                    var attempt = Guid.NewGuid();
                    var verdict = await Ask(host, alias, json, attempt, descriptor, timeout, log, ct).ConfigureAwait(true);
                    if (!verdict.Allowed) { throw new RuleViolationException(new Rejected(descriptor.Name, verdict.Rules, verdict.Messages)); }
                    // An allowance without a next state cannot be committed: it fails closed like a gate that did not answer.
                    if (verdict.Next is not { } next)
                    {
                        allowed.Add((host, signal, default, attempt));
                        throw new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
                    }
                    allowed.Add((host, signal, next, attempt));
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

    // An installed alias missing from the live catalog (as after a restart) is restored before its gates are looked up, bounded by
    // GateTimeout; a restore that fails or does not finish fails the publish closed. With every installed alias live this awaits nothing.
    private static async Task RestoreInstalled(IServiceProvider services, IGateIndex index, BrainId brain, TimeSpan timeout, ILogger log, CancellationToken ct)
    {
        if (index is not InstalledGateIndex gates || !gates.TryGetInstalled(brain, out var aliases)
            || services.GetService<ISynapseCatalogSource>() is not { } catalog || services.GetService<ISynapseRestorer>() is not { } restorer
            || aliases.All(catalog.Current.States.ContainsKey))
        {
            return;
        }
        services.GetService<InstalledGates>()?.CountGateRestore();
        try { await InstallRestore.EnsureInstalledAliases(catalog, aliases, restorer, brain, ct).WaitAsync(timeout, ct).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException && !(error is OperationCanceledException && ct.IsCancellationRequested))
        {
            LogInstalledRestoreFailed(log, error, brain.ToString());
            throw new RuleViolationException(new Rejected(OrleansInstallHooks.InstallsType, [OrleansInstallHooks.InstallsType], [NoAnswer]));
        }
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
        var hook = services.GetService<PublishFaults>()?.BeforeGateCommit;
        var chain = EnterChain(publisher);
        try
        {
            foreach (var (gate, signal, next, ticket) in allowed)
            {
                try
                {
                    // Test-only (RuntimeTestHooks.BeforeGateCommit): a delay that forces gate commits out of order.
                    if (hook is not null) { await hook(NeuronId.Parse(gate.GetPrimaryKeyString()), signal).ConfigureAwait(true); }
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

    // The signals no gate runs on (see the type summary).
    private static bool Ungated(Signal signal)
        => signal is StateChanged or Rejected or DigitalBrain.Time.Tick or Installed or Retired or InstallRequested or InstallDenied or Deployed or Staged or Undeployed or DigitalBrain.Time.Registered or DigitalBrain.Time.Unregistered or PolicyProposed or PolicyAdopted or PolicyRefused or PolicyRetired;

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

    // A gate that does not answer within the timeout, or fails, refuses; its call is cancelled, and an Aborted with the attempt id is
    // sent behind it, not awaited, so a gate that ignores the cancellation and allows late leaves no entry (one that never added an entry is untouched).
    private static async Task<GateVerdict> Ask(INeuronHost host, string alias, JsonElement json, Guid attempt, SynapseDescriptor descriptor, TimeSpan timeout, ILogger log, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var call = host.Gate(alias, json, attempt, cancel.Token);
        try { return await call.WaitAsync(timeout, ct).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await cancel.CancelAsync().ConfigureAwait(true);
            _ = call.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            var refused = new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
            var gate = host.GetPrimaryKeyString();
            _ = host.Aborted(alias, json, attempt, CancellationToken.None).ContinueWith(t => LogTimedOutGateAbortFailed(log, t.Exception!, gate, alias),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw refused;
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

    [LoggerMessage(EventId = 4126, EventName = "InstalledGatesTimedOut", Level = LogLevel.Warning,
        Message = "The installed gates of the brain of {Publisher} did not load within {Timeout}; the publish is refused.")]
    private static partial void LogInstalledGatesTimedOut(ILogger logger, string publisher, TimeSpan timeout);

    [LoggerMessage(EventId = 4128, EventName = "InstalledRestoreFailed", Level = LogLevel.Warning,
        Message = "An installed synapse source of brain {Brain} could not be restored; the publish is refused.")]
    private static partial void LogInstalledRestoreFailed(ILogger logger, Exception error, string brain);

    [LoggerMessage(EventId = 4127, EventName = "TimedOutGateAbortFailed", Level = LogLevel.Debug,
        Message = "The gate {Gate} that did not answer could not drop a late provisional state for {Signal}; its lease discards it.")]
    private static partial void LogTimedOutGateAbortFailed(ILogger logger, Exception error, string gate, string signal);
}
