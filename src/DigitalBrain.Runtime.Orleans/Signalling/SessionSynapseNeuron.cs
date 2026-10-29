using System.Text.Json;
using System.Security.Cryptography;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.Options;
using OrleansSdk = Orleans;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>
/// The per-session synapse neuron (04 D-02, D-13, R4-03): a plain neuron on <c>neuron.host</c> that is itself the synapse owner. The
/// engine hands each signal to <see cref="Deliver"/>, which queues it; <see cref="Next"/> cuts batches of at most
/// <see cref="SessionSynapseOptions.MaxBatchSignals"/> and sends the last one again until it is acknowledged. A timer checks
/// <see cref="SessionSynapseOptions.SessionTtl"/> after the last ack and then removes every synapse and cursor (RESEARCH Pitfall 3).
/// </summary>
/// <remarks>Queue changes happen only between awaits on the activation's scheduler; the long-poll completes through <see cref="_wake"/>.</remarks>
[Neuron(SessionSynapses.NeuronType)]
internal sealed class SessionSynapseNeuron(IOptions<SessionSynapseOptions> options, RuntimeClock clock, JournalPayloads payloads,
    IOptions<OrleansSdk.Configuration.SiloMessagingOptions> messaging)
    : Neuron, ISessionSynapses, ISignalSink
{
    internal const string SweepTimer = "session.ttl";

    private readonly SessionSynapseOptions _options = options.Value;
    // A poll that waited as long as the caller's response timeout lost its answer to the race: the caller timed out, the response
    // expired, and a batch cut into it was only resent on the next poll.
    private readonly TimeSpan _wait = Min(options.Value.NextTimeout, messaging.Value.ResponseTimeout * 2 / 3);
    private readonly List<SessionSignal> _pending = [];
    private SessionBatch? _outstanding;
    private long _issued;
    private DateTimeOffset _lastAck;
    private TaskCompletionSource? _wake;
    private readonly Dictionary<string, Admission> _admissions = new(StringComparer.Ordinal);
    private int _admitting;
    private long _generation;
    private bool _ended;
    private static readonly TimeSpan AdmissionLifetime = TimeSpan.FromMinutes(1);

    private HostNeuronContext Host => (HostNeuronContext)Context;

    protected override Task OnActivate(CancellationToken cancellationToken)
    {
        _lastAck = clock.Now;
        Context.Timers.Start(SweepTimer, _options.SessionSweepEvery, _options.SessionSweepEvery);
        return Task.CompletedTask;
    }

    public async Task<string> PrepareSubscription(NeuronId source, string[] aliases)
    {
        aliases = Host.Engine.ValidateAliases(source, aliases);
        if (_ended) { throw new InvalidOperationException("This session has ended."); }
        PruneAdmissions();
        if (_admissions.Count >= _options.MaxSessionSynapses || _admitting >= _options.MaxSessionSynapses)
        { throw new SessionLimitException($"Session {Id} has the maximum number of pending admissions."); }
        var ticket = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var admission = new Admission(source, aliases, _generation, RuntimeCallerContext.Current, Host.Caller, clock.Now + AdmissionLifetime);
        _admissions.Add(ticket, admission);
        _ = AskAdmission(admission);
        try
        {
            await admission.Ready.Task.ConfigureAwait(true);
            if (_ended || admission.Generation != _generation || !_admissions.ContainsKey(ticket))
            { throw new InvalidOperationException("The subscription admission is no longer valid."); }
            admission.Approved = true;
            return ticket;
        }
        catch
        {
            _admissions.Remove(ticket);
            throw;
        }
    }

    public async Task CommitSubscription(string ticket)
    {
        PruneAdmissions();
        if (string.IsNullOrEmpty(ticket) || !_admissions.TryGetValue(ticket, out var admission) || !admission.Approved
            || _ended || admission.Generation != _generation || admission.Principal != RuntimeCallerContext.Current
            || !string.Equals(admission.Caller, Host.Caller, StringComparison.Ordinal))
        { throw new UnauthorizedAccessException("The subscription admission ticket is not valid for this caller and session."); }
        _admissions.Remove(ticket);
        var source = admission.Source;
        var key = source.ToString();
        var held = Host.Engine.Sources;
        if (held.Count >= _options.MaxSessionSynapses && !held.Any(entry => entry.Source == key))
        {
            throw new SessionLimitException($"Session {Id} holds {held.Count} synapses, the most a session may ({_options.MaxSessionSynapses}).");
        }
        await Host.Engine.CommitAliases(source, admission.Aliases).ConfigureAwait(true);
    }

    public Task Unsubscribe(NeuronId source)
    {
        foreach (var ticket in _admissions.Where(pair => pair.Value.Source == source).Select(pair => pair.Key).ToArray())
        { Invalidate(ticket); }
        return Host.Engine.Remove(source);
    }

    public async Task<SessionBatch?> Next(long acked, CancellationToken cancellationToken)
    {
        if (_ended) { return null; }
        _lastAck = clock.Now;
        if (_outstanding is { } sent)
        {
            if (acked < sent.Number) { return sent; } // not acknowledged: send it again
            _outstanding = null;
        }
        var until = DateTime.UtcNow + _wait;
        while (_pending.Count == 0)
        {
            if (_ended) { return null; }
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) { return null; }
            _wake ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(left);
            try { await _wake.Task.WaitAsync(limit.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            if (_outstanding is { } built) { return built; } // an interleaved Next cut the batch meanwhile
        }
        var take = _pending.Take(_options.MaxBatchSignals).ToArray();
        _pending.RemoveRange(0, take.Length);
        _outstanding = new SessionBatch(++_issued, take);
        return _outstanding;
    }

    public async Task End()
    {
        _ended = true;
        _generation++;
        foreach (var ticket in _admissions.Keys.ToArray()) { Invalidate(ticket); }
        await Host.Engine.RemoveAll().ConfigureAwait(true);
        _pending.Clear();
        _outstanding = null;
        Wake();
        Context.Timers.Stop(SweepTimer);
        Host.DeactivateOnIdle();
    }

    public Task Deliver(JournaledSignal item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_pending.Count >= _options.MaxPendingSignals)
        {
            throw new SessionLimitException($"Session {Id} has {_pending.Count} signals waiting for Next; delivery resumes after an ack.");
        }
        var encoded = payloads.EncodeSignal(item.Signal);
        using var json = JsonDocument.Parse(encoded.Json);
        _pending.Add(new SessionSignal(item.Publisher.ToString(), item.Sequence, encoded.Type, json.RootElement.Clone(), item.At));
        Wake();
        return Task.CompletedTask;
    }

    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        if (name != SweepTimer || clock.Now - _lastAck < _options.SessionTtl) { return; }
        await End().ConfigureAwait(true);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private void PruneAdmissions()
    {
        foreach (var ticket in _admissions.Where(pair => pair.Value.Expires <= clock.Now).Select(pair => pair.Key).ToArray())
        { Invalidate(ticket); }
    }

    private void Invalidate(string ticket)
    {
        if (_admissions.Remove(ticket, out var admission))
        { admission.Ready.TrySetException(new InvalidOperationException("The subscription admission is no longer valid.")); }
    }

    private async Task AskAdmission(Admission admission)
    {
        // Invalidation releases the subscriber immediately. The source RPC still counts against the pending limit until it
        // finishes or times out, and its result is observed here even after its ticket has been discarded.
        _admitting++;
        try
        {
            await Host.Engine.AdmitAliases(admission.Source, admission.Aliases).ConfigureAwait(true);
            admission.Ready.TrySetResult();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { admission.Ready.TrySetException(error); }
        finally { _admitting--; }
    }

    private sealed record Admission(NeuronId Source, string[] Aliases, long Generation, CapabilityCaller? Principal, string Caller, DateTimeOffset Expires)
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Approved { get; set; }
    }

    private void Wake()
    {
        var wake = _wake;
        _wake = null;
        wake?.TrySetResult();
    }
}
