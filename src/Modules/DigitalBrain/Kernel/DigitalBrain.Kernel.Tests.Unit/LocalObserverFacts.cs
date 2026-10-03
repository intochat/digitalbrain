using DigitalBrain.Contracts.Signals;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Tests.Unit;

public sealed class LocalObserverFacts
{
    [Fact]
    public async Task AnObserverFailureIsReportedAndUnsubscribed()
    {
        var hub = new LocalSignalHub(NullLogger<LocalSignalHub>.Instance);
        var id = GrainId.Create("test", "observer");
        var observer = new Observer(() => Task.FromException(new IOException("Observer failed")));
        hub.Subscribe(id, observer);
        hub.Publish(id, new Number(1));
        Assert.IsType<IOException>(await observer.Error.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        hub.Publish(id, new Number(2));
        Assert.Equal(1, observer.Deliveries);
    }

    [Fact]
    public async Task ASlowObserverFailsExplicitlyWhenItsBufferFills()
    {
        var hub = new LocalSignalHub(NullLogger<LocalSignalHub>.Instance);
        var id = GrainId.Create("test", "slow");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(() => pending.Task);
        hub.Subscribe(id, observer);
        for (var i = 0; i < 1100; i++) { hub.Publish(id, new Number(i)); }
        var error = await observer.Error.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(error);
        pending.TrySetResult();
    }

    private sealed class Observer(Func<Task> receive) : INeuronObserver, ILocalSignalFaultSink
    {
        public int Deliveries { get; private set; }
        public TaskCompletionSource<Exception> Error { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task OnSignalAsync(Signal signal) { Deliveries++; return receive(); }
        public void OnError(Exception error) => Error.TrySetResult(error);
    }
}
