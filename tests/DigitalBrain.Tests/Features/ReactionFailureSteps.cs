using System.Collections.Concurrent;
using System.Diagnostics;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class ReactionFailureSteps : IDisposable
{
    private readonly ConcurrentQueue<Activity> _failures = new();
    private ActivityListener? _listener;

    [Given("reaction failures are observed")]
    public void Observe()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DigitalBrain",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "db.drain.failed")
                {
                    _failures.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [Then(@"reaction ""(.*)"" failed with ""(.*)""")]
    public void Failed(string neuron, string message)
        => Assert.Contains(_failures, activity => activity.GetTagItem("neuron")?.ToString() == neuron
            && activity.StatusDescription?.Contains(message, StringComparison.Ordinal) == true);

    public void Dispose() => _listener?.Dispose();
}
