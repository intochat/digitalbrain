using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Signalling;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Signalling;

// The listener pump is not the publishing call, so the stamp has to be captured when the signal is offered.
public sealed class ClusterListenerCallerFacts
{
    [Fact]
    public async Task A_listener_sees_the_caller_stamped_when_the_signal_was_offered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JournalPayloads(new SignalTypeMap([])));
        services.AddSingleton<ClusterListeners>();
        await using var provider = services.BuildServiceProvider();
        var listeners = provider.GetRequiredService<ClusterListeners>();
        var seen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await listeners.Listen<Gap>("caller", (_, _, _) =>
        {
            seen.TrySetResult(RuntimeCallerContext.Current?.PrincipalId);
            return Task.CompletedTask;
        });
        var publisher = new NeuronId(new BrainId("b"), "app", "apps/desktop");
        using (RuntimeCallerContext.Stamp(new CapabilityCaller("alice", "acct", "b")))
        {
            listeners.Offer(publisher, [new Gap(publisher, 1, 2)]);
        }

        var principal = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("alice", principal);
    }
}
