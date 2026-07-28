using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

[Alias("DigitalBrain.ModuleTests.IConcurrentStreamTarget")]
public partial interface IConcurrentStreamTarget : INeuron
{
    [Alias(nameof(StreamOnceBothCallersArrive))]
    IAsyncEnumerable<string> StreamOnceBothCallersArrive();
}

[Alias("DigitalBrain.ModuleTests.IConcurrentStreamCaller")]
[ClientEntryPoint]
public partial interface IConcurrentStreamCaller : INeuron
{
    [Alias(nameof(DrainTarget))]
    Task<string> DrainTarget(string targetName);
}

public sealed class ConcurrentStreamTarget : Neuron, IConcurrentStreamTarget
{
    internal const string OutsideCapabilityTurn = "outside-capability-turn";
    internal const string InsideCapabilityTurn = "inside-capability-turn";

    private static readonly TimeSpan ArrivalBudget = TimeSpan.FromSeconds(30);
    private static readonly Lock ArrivalGate = new();
    private static TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _arrived;

    internal static void ExpectTwoCallers()
    {
        lock (ArrivalGate)
        {
            _arrived = 0;
            _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public async IAsyncEnumerable<string> StreamOnceBothCallersArrive()
    {
        Task arrival;

        lock (ArrivalGate)
        {
            arrival = _bothArrived.Task;

            if (++_arrived >= 2)
            {
                _bothArrived.TrySetResult();
            }
        }

        await arrival.WaitAsync(ArrivalBudget);

        yield return CapabilityTurnState();
    }

    private string CapabilityTurnState()
    {
        try
        {
            ValidateCapabilityCaller(Id);
        }
        catch (InvalidOperationException)
        {
            return OutsideCapabilityTurn;
        }
        catch (NeuronAuthorizationException)
        {
            return InsideCapabilityTurn;
        }

        return InsideCapabilityTurn;
    }
}

public sealed class ConcurrentStreamCaller : Neuron, IConcurrentStreamCaller
{
    public async Task<string> DrainTarget(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        var target = GrainFactory.GetGrain<IConcurrentStreamTarget>(
            NeuronId.For<IConcurrentStreamTarget>(Id.Owner, targetName).ToGrainId());
        var observed = new List<string>();

        await foreach (var state in target.StreamOnceBothCallersArrive())
        {
            observed.Add(state);
        }

        return string.Join(",", observed);
    }
}

public sealed class ConcurrentStreaming(ModuleFixture fixture)
{
    private const string TargetName = "concurrent-stream-target";
    private const string FirstCallerName = "concurrent-stream-caller-one";
    private const string SecondCallerName = "concurrent-stream-caller-two";

    [Fact(DisplayName = "two neurons streaming from one target concurrently both succeed and are both journaled")]
    public async Task ConcurrentEnumerationsAgainstOneTargetAreBothJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var target = test.Neuron<IConcurrentStreamTarget>(TargetName);
        var first = test.Neuron<IConcurrentStreamCaller>(FirstCallerName);
        var second = test.Neuron<IConcurrentStreamCaller>(SecondCallerName);

        ConcurrentStreamTarget.ExpectTwoCallers();

        var firstDrain = first.Reference.DrainTarget(TargetName);
        var secondDrain = second.Reference.DrainTarget(TargetName);

        await Task.WhenAll(firstDrain, secondDrain);

        var received = await target.Incoming.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Equal(2, received.Count(fact => fact.Synapse.Method == nameof(IConcurrentStreamTarget.StreamOnceBothCallersArrive)));
        Assert.Single(await first.Outgoing.ReadAsync<CapabilityCompleted>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Single(await second.Outgoing.ReadAsync<CapabilityCompleted>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Empty(await first.Outgoing.ReadAsync<CapabilityFailed>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Empty(await second.Outgoing.ReadAsync<CapabilityFailed>(afterSequence: 0, cancellationToken: cancellationToken));
    }

    [Fact(DisplayName = "a streamed capability body runs outside any capability turn on the target — pinned limitation")]
    public async Task StreamedCapabilityBodyRunsOutsideACapabilityTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var first = test.Neuron<IConcurrentStreamCaller>(FirstCallerName);
        var second = test.Neuron<IConcurrentStreamCaller>(SecondCallerName);

        ConcurrentStreamTarget.ExpectTwoCallers();

        var firstDrain = first.Reference.DrainTarget(TargetName);
        var secondDrain = second.Reference.DrainTarget(TargetName);

        await Task.WhenAll(firstDrain, secondDrain);

        Assert.Equal(ConcurrentStreamTarget.OutsideCapabilityTurn, await firstDrain);
        Assert.Equal(ConcurrentStreamTarget.OutsideCapabilityTurn, await secondDrain);
    }
}
