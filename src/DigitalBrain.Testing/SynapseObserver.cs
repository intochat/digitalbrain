using System.Collections.Concurrent;
using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

public sealed class SynapseObserver : IDisposable
{
    private static readonly TimeSpan ObservationLimit = TimeSpan.FromSeconds(10);

    private readonly ConcurrentBag<Observation> _observed = [];
    private readonly ConcurrentDictionary<Observation, TaskCompletionSource> _awaited = new();
    private readonly ActivityListener _listener;

    public SynapseObserver()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SynapseTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = Record,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public async Task AwaitHandledAsync(NeuronId receiver, string synapseTypeName)
    {
        var expected = new Observation(receiver.ToString(), synapseTypeName);

        if (_observed.Contains(expected))
        {
            return;
        }

        var arrival = _awaited.GetOrAdd(expected, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        if (_observed.Contains(expected))
        {
            return;
        }

        var completed = await Task.WhenAny(arrival.Task, Task.Delay(ObservationLimit));

        if (completed != arrival.Task)
        {
            throw new SimulationAssertionException(
                $"{synapseTypeName} was never handled by {receiver} within {ObservationLimit.TotalSeconds:0} seconds. Observed: {Summarize()}.");
        }
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Activity activity)
    {
        if (activity.Status == ActivityStatusCode.Error)
        {
            return;
        }

        var receiver = activity.GetTagItem(SynapseTelemetry.ReceiverTag) as string;
        var synapse = activity.GetTagItem(SynapseTelemetry.SynapseTag) as string;

        if (receiver is null || synapse is null)
        {
            return;
        }

        var observation = new Observation(receiver, synapse);
        _observed.Add(observation);

        if (_awaited.TryGetValue(observation, out var waiting))
        {
            waiting.TrySetResult();
        }
    }

    private string Summarize()
        => _observed.IsEmpty ? "nothing" : string.Join(", ", _observed.Select(entry => $"{entry.Synapse}@{entry.Receiver}").Distinct());

    private readonly record struct Observation(string Receiver, string Synapse);
}
