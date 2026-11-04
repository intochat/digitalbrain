using DigitalBrain.Rules;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// The gate path of a TestOS write: before a publisher's durable signals are appended, every gate neuron the index names
// (other than the publisher) runs its gate synapses as a turn; after the append, each allowing gate saves its next state.
internal static class TestGates
{
    private const string NoAnswer = "the gate did not answer";

    // Runs the gates of every durable signal in order; the first refusal, timeout or failure stops with RuleViolationException,
    // after the gates of the same publish that already allowed are told Aborted (they drop their provisional state).
    public static async Task<IReadOnlyList<(TestNeuronHost Gate, Signal Signal, Check.Allowed Allowed)>> Run(TestOS os, NeuronId publisher, IReadOnlyList<Signal> signals, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(signals);
        var allowed = new List<(TestNeuronHost Gate, Signal Signal, Check.Allowed Allowed)>();
        try
        {
            foreach (var signal in signals.Where(s => !Ungated(s) && !s.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false)))
            {
                foreach (var (gate, descriptor) in os.Gates.GatesFor(publisher.Brain, signal.GetType()))
                {
                    if (gate == publisher) { continue; }
                    var host = os.Host(gate);
                    // One attempt id per gate call of this publish: the gate's entry and its ticket carry it.
                    var verdict = await Ask(os, host, signal, Guid.NewGuid(), descriptor, ct).ConfigureAwait(false);
                    if (verdict is Check.Refused refused)
                    {
                        throw new RuleViolationException(new Rejected(descriptor.Name, [.. refused.Broken.Select(r => r.Name)], [.. refused.Broken.Select(r => r.Message)]));
                    }
                    allowed.Add((host, signal, (Check.Allowed)verdict));
                }
            }
        }
        catch
        {
            await Abort(os, allowed).ConfigureAwait(false);
            throw;
        }
        return allowed;
    }

    // Gates never run on StateChanged: a gate's Committed saves it while the publisher still awaits Commit (as the Orleans GateRunner).
    // Nor on Rejected: the refusing gate journals it inside its own gate turn, so a gate on it would run nested in that turn. Nor on
    // the runtime's own Tick, Installed, Retired and time registrations: their publishers (time, installs) serve the gate path itself.
    private static bool Ungated(Signal signal)
        => signal is StateChanged or Rejected or DigitalBrain.Time.Tick or Installed or Retired or DigitalBrain.Time.Registered or DigitalBrain.Time.Unregistered;

    // The publish did not happen (a later gate stopped it, or the append threw): each allowing gate drops its provisional state, as a turn.
    // A failing Aborted goes to TestOS.Failures; the gate's lease then discards the entry.
    public static async Task Abort(TestOS os, IReadOnlyList<(TestNeuronHost Gate, Signal Signal, Check.Allowed Allowed)> allowed)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(allowed);
        foreach (var (gate, signal, verdict) in allowed)
        {
            try
            {
                await os.Track(gate.Turn(interleave: false, async neuron =>
                {
                    await ((ISynapseReceiver)neuron).Aborted(signal, verdict.Ticket).ConfigureAwait(false);
                    return 0;
                })).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { os.Fail(error); }
        }
    }

    // Once the publisher's append is durable, saves the next state of each allowing gate, each as a turn of its gate neuron.
    // The publisher's entry and the gates' entries are not one transaction: a failing Committed goes to TestOS.Failures, never to the publisher.
    public static async Task Commit(TestOS os, IReadOnlyList<(TestNeuronHost Gate, Signal Signal, Check.Allowed Allowed)> allowed)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(allowed);
        foreach (var (gate, signal, verdict) in allowed)
        {
            try
            {
                if (os.Options.BeforeGateCommit is { } hook) { await hook(gate.Id, signal).ConfigureAwait(false); }
                await os.Track(gate.Turn(interleave: false, async neuron =>
                {
                    await ((ISynapseReceiver)neuron).Committed(signal, verdict).ConfigureAwait(false);
                    return 0;
                })).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { os.Fail(error); }
        }
    }

    // A gate that does not answer within GateTimeout, or fails, refuses; its call is cancelled, and an Aborted with the attempt id is
    // queued as a tracked turn behind it, so a gate that ignores the cancellation and allows late leaves no provisional entry (and a
    // gate that never added one is untouched).
    private static async Task<Check> Ask(TestOS os, TestNeuronHost host, Signal signal, Guid attempt, SynapseDescriptor descriptor, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var turn = os.Track(host.Turn(interleave: false, neuron => ((ISynapseReceiver)neuron).Gate(signal, attempt, cancel.Token)));
        try
        {
            return await turn.WaitAsync(os.Options.GateTimeout, os.Clock, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await cancel.CancelAsync().ConfigureAwait(false);
            _ = turn.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            var refused = new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
            _ = os.Track(host.Turn(interleave: false, async neuron =>
            {
                await ((ISynapseReceiver)neuron).Aborted(signal, attempt).ConfigureAwait(false);
                return 0;
            }));
            throw refused;
        }
    }
}
