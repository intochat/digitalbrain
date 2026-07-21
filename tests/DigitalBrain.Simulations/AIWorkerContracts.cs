using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class AIWorkerContracts
{
    [Fact(DisplayName = "a raw same-owner runner is rejected before semantic target entry")]
    public async Task RawRunnerIsRejectedBeforeSemanticTargetEntry()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("ai-worker-raw-runner");
        var target = NeuronId.For<RawCapabilityTarget>(owner, "probe");
        var runner = SimulationCluster.Grains.GetGrain<IRawWorkflowRunner>(
            IdSpan.Create($"{owner.Value}/runner"));

        var failure = await Record.ExceptionAsync(() => runner.InvokeAsync(target));

        Assert.Equal(0, RawCapabilityTargetObservations.EntryCount(target));
        Assert.IsType<NeuronAuthorizationException>(failure);
    }

    [Fact(DisplayName = "a deliberate client entry point remains callable without a reified request")]
    public async Task ClientEntryPointRemainsCallableWithoutAReifiedRequest()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("kernel-client-entry");
        var target = NeuronId.For<KernelClientEntryTarget>(owner, "probe");
        var probe = SimulationCluster.Grains.GetGrain<IKernelClientEntryProbe>(target.ToGrainId());

        Assert.Equal(1, await probe.EnterAsync());
    }
}

[Alias("db.test.raw-workflow-runner")]
internal interface IRawWorkflowRunner : IGrainWithStringKey
{
    [Alias("Invoke")]
    Task InvokeAsync(NeuronId target);
}

internal sealed class RawWorkflowRunner(IGrainFactory grains) : Grain, IRawWorkflowRunner
{
    public Task InvokeAsync(NeuronId target)
        => grains.GetGrain<IRawCapabilityTarget>(target.ToGrainId()).EnterAsync();
}

[Alias("db.test.raw-capability-target")]
internal interface IRawCapabilityTarget : INeuron
{
    [Alias("Enter")]
    Task EnterAsync();
}

internal sealed class RawCapabilityTarget : Neuron, IRawCapabilityTarget
{
    public Task EnterAsync()
    {
        RawCapabilityTargetObservations.RecordEntry(Id);

        return Task.CompletedTask;
    }
}

internal static class RawCapabilityTargetObservations
{
    private static readonly ConcurrentDictionary<NeuronId, int> Entries = new();

    internal static int EntryCount(NeuronId target)
        => Entries.GetValueOrDefault(target);

    internal static void RecordEntry(NeuronId target)
        => Entries.AddOrUpdate(target, 1, static (_, count) => count + 1);
}

[Alias("db.test.kernel-client-entry-probe")]
[ClientEntryPoint]
internal interface IKernelClientEntryProbe : INeuron
{
    [Alias("Enter")]
    Task<int> EnterAsync();
}

internal sealed class KernelClientEntryTarget : Neuron, IKernelClientEntryProbe
{
    private int _entries;

    public Task<int> EnterAsync() => Task.FromResult(++_entries);
}
