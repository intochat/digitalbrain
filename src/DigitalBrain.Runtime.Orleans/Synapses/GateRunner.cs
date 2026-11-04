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
/// on <see cref="StateChanged"/> are still ensured.
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
    /// <returns>Each allowing gate with the signal and its proposed next state.</returns>
    /// <exception cref="RuleViolationException">A gate refused, did not answer or failed.</exception>
    public static async Task<IReadOnlyList<(INeuronHost Gate, Signal Signal, JsonElement Next)>> Run(IServiceProvider services, NeuronId publisher,
        IReadOnlyList<Signal> signals, ISet<string> ensured, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(ensured);
        var durable = signals.Where(signal => !signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false)).ToArray();
        var gated = durable.Where(signal => signal is not StateChanged).ToArray();
        if (durable.Length == 0 || services.GetService<IGateIndex>() is not { } index || services.GetService<SynapseCatalog>() is not { } catalog) { return []; }
        var payloads = services.GetRequiredService<JournalPayloads>();
        var grains = services.GetRequiredService<IGrainFactory>();
        var types = services.GetRequiredService<NeuronTypeRegistry>();
        await EnsureReactions(catalog, payloads, grains, types, publisher, durable, ensured).ConfigureAwait(true);
        var timeout = RuntimeOptions.From(services).GateTimeout;
        var allowed = new List<(INeuronHost, Signal, JsonElement)>();
        foreach (var signal in gated)
        {
            foreach (var (gate, descriptor) in index.GatesFor(publisher.Brain, signal.GetType()))
            {
                if (gate == publisher) { continue; }
                var host = Host(grains, types, gate);
                var (alias, json) = Encode(payloads, signal);
                var verdict = await Ask(host, alias, json, descriptor, timeout, ct).ConfigureAwait(true);
                if (!verdict.Allowed) { throw new RuleViolationException(new Rejected(descriptor.Name, verdict.Rules, verdict.Messages)); }
                // An allowance without a next state cannot be committed: it fails closed like a gate that did not answer.
                if (verdict.Next is not { } next) { throw new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer])); }
                allowed.Add((host, signal, next));
            }
        }
        return allowed;
    }

    /// <summary>
    /// Once the publisher's append is durable, saves the next state of each allowing gate, each as a turn of its gate neuron. The
    /// publisher's entry and the gates' entries are not one transaction: a failing <c>Committed</c> is logged, never thrown to the publisher.
    /// </summary>
    /// <param name="services">The silo services.</param>
    /// <param name="allowed">What <see cref="Run"/> returned.</param>
    /// <returns>A task that completes when every gate was asked.</returns>
    public static async Task Commit(IServiceProvider services, IReadOnlyList<(INeuronHost Gate, Signal Signal, JsonElement Next)> allowed)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(allowed);
        if (allowed.Count == 0) { return; }
        var payloads = services.GetRequiredService<JournalPayloads>();
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(GateRunner)) ?? NullLogger.Instance;
        foreach (var (gate, signal, next) in allowed)
        {
            try
            {
                var (alias, json) = Encode(payloads, signal);
                await gate.Committed(alias, json, next).ConfigureAwait(true);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                LogGateCommitFailed(log, error, gate.GetPrimaryKeyString(), signal.GetType().Name);
            }
        }
    }

    // A gate that does not answer within the timeout, or fails, refuses; its call is cancelled and its state is not touched.
    private static async Task<GateVerdict> Ask(INeuronHost host, string alias, JsonElement json, SynapseDescriptor descriptor, TimeSpan timeout, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var call = host.Gate(alias, json, cancel.Token);
        try { return await call.WaitAsync(timeout, ct).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await cancel.CancelAsync().ConfigureAwait(true);
            _ = call.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
        }
    }

    // The interim reaction-edge rule (see the type summary): each (subscriber, signal) is ensured once per publisher activation.
    private static async Task EnsureReactions(SynapseCatalog catalog, JournalPayloads payloads, IGrainFactory grains, NeuronTypeRegistry types,
        NeuronId publisher, Signal[] signals, ISet<string> ensured)
    {
        foreach (var signal in signals)
        {
            if (!payloads.Types.TryAliasOf(signal.GetType(), out var alias)) { continue; }
            var subscribers = catalog.Synapses
                .Where(d => d.Kind == SynapseKind.Reaction && d.Signal!.IsInstanceOfType(signal))
                .Select(d => new NeuronId(publisher.Brain, d.StateAlias, d.Key))
                .Where(subscriber => subscriber != publisher)
                .Distinct();
            foreach (var subscriber in subscribers)
            {
                var edge = $"{subscriber}|{alias}";
                if (!ensured.Add(edge)) { continue; }
                var host = grains.GetGrain<ISynapseHost>(GrainId.Create(NeuronTypeRegistry.GrainTypeOf(types.Require(subscriber.Type).Policy), subscriber.ToString()));
                var previous = RequestContext.Get(HostNeuronContext.CallerKey);
                RequestContext.Set(HostNeuronContext.CallerKey, publisher.ToString());
                try { await host.EnsureFrom(publisher, [alias]).ConfigureAwait(true); }
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
}
