using DigitalBrain.Rules;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// The gate path of a TestOS write: before a publisher's durable signals are appended, every gate neuron the index names
// (other than the publisher) runs its gate synapses as a turn; after the append, each allowing gate saves its next state.
internal static class TestGates
{
    private const string NoAnswer = "the gate did not answer";

    // Runs the gates of every durable signal in order; the first refusal, timeout or failure stops with RuleViolationException.
    public static async Task<IReadOnlyList<(TestNeuronHost Gate, Signal Signal, Check.Allowed Allowed)>> Run(TestOS os, NeuronId publisher, IReadOnlyList<Signal> signals, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(os);
        ArgumentNullException.ThrowIfNull(signals);
        var allowed = new List<(TestNeuronHost, Signal, Check.Allowed)>();
        // Gates never run on StateChanged: a gate's Committed saves it while the publisher still awaits Commit (as the Orleans GateRunner).
        foreach (var signal in signals.Where(s => s is not StateChanged && !s.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false)))
        {
            foreach (var (gate, descriptor) in os.Gates.GatesFor(publisher.Brain, signal.GetType()))
            {
                if (gate == publisher) { continue; }
                var host = os.Host(gate);
                var verdict = await Ask(os, host, signal, descriptor, ct).ConfigureAwait(false);
                if (verdict is Check.Refused refused)
                {
                    throw new RuleViolationException(new Rejected(descriptor.Name, [.. refused.Broken.Select(r => r.Name)], [.. refused.Broken.Select(r => r.Message)]));
                }
                allowed.Add((host, signal, (Check.Allowed)verdict));
            }
        }
        return allowed;
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
                await os.Track(gate.Turn(interleave: false, async neuron =>
                {
                    await ((ISynapseReceiver)neuron).Committed(signal, verdict).ConfigureAwait(false);
                    return 0;
                })).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { os.Fail(error); }
        }
    }

    // A gate that does not answer within GateTimeout, or fails, refuses; its call is cancelled and its state is not touched.
    private static async Task<Check> Ask(TestOS os, TestNeuronHost host, Signal signal, SynapseDescriptor descriptor, CancellationToken ct)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var turn = os.Track(host.Turn(interleave: false, neuron => ((ISynapseReceiver)neuron).Gate(signal, cancel.Token)));
        try
        {
            return await turn.WaitAsync(os.Options.GateTimeout, os.Clock, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await cancel.CancelAsync().ConfigureAwait(false);
            _ = turn.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw new RuleViolationException(new Rejected(descriptor.Name, [descriptor.Name], [NoAnswer]));
        }
    }
}
