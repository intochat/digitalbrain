using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class HarnessCancellationFacts
{
    [Fact]
    public async Task CanceledStartupCancelsAndJoinsTheDeployment()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var service = new HeldStartup();
        var starting = DigitalBrainSimulation.StartAsync(new()
        {
            ConfigureSilo = silo => silo.Services.AddSingleton<IHostedService>(service)
        }, cancel.Token);
        try
        {
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
            Assert.True(service.Canceled, "Cancellation must reach startup before the host returns.");
        }
        finally
        {
            service.Release.TrySetResult();
            try { await starting; } catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationCallbacksCannotBypassTheShutdownDeadline(bool throws)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = await DigitalBrainSimulation.StartAsync(cancellationToken: ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var run = brain.RunBehavior(async (_, token) =>
        {
            using var registration = token.Register(() =>
            {
                if (throws) { throw new InvalidOperationException("callback failed"); }
                release.Wait();
            });
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Task? disposing = null;
        try
        {
            disposing = brain.DisposeAsync().AsTask();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => disposing.WaitAsync(TimeSpan.FromSeconds(7), ct));
            if (throws) { Assert.Contains("callback failed", failure.ToString()); }
            else { Assert.Contains("timed out", failure.ToString(), StringComparison.OrdinalIgnoreCase); }
            await Assert.ThrowsAnyAsync<Exception>(() => brain.SubscribeAsync<Number>(brain.Get<ITestEmitter>("closed"), ct));
        }
        finally
        {
            release.Set();
            if (disposing is not null) { try { await disposing; } catch (Exception) { } }
            try { await run.Completion; } catch (OperationCanceledException) { }
        }
    }

    private sealed class HeldStartup : IHostedService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Canceled { get; private set; }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try { await Release.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
