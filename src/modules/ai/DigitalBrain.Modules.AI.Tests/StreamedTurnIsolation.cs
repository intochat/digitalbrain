using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

[GenerateSerializer]
[Alias("DigitalBrain.ModuleTests.FailingTurnTrigger")]
public sealed record FailingTurnTrigger([property: Id(0)] string RecipientName) : Synapse;

[GenerateSerializer]
[Alias("DigitalBrain.ModuleTests.OrphanedByRollback")]
public sealed record OrphanedByRollback : Synapse;

[Alias("DigitalBrain.ModuleTests.IStreamedTurnTarget")]
public partial interface IStreamedTurnTarget : INeuron
{
    [Alias(nameof(StreamOnce))]
    IAsyncEnumerable<string> StreamOnce();

    [Alias(nameof(StageThenFail))]
    Task StageThenFail(string recipientName);
}

[Alias("DigitalBrain.ModuleTests.IStreamedTurnCaller")]
[ClientEntryPoint]
public partial interface IStreamedTurnCaller : INeuron
{
    [Alias(nameof(DrainStreamedTurnTarget))]
    Task DrainStreamedTurnTarget(string targetName);

    [Alias(nameof(ProvokeStagedRollback))]
    Task<bool> ProvokeStagedRollback(string targetName, string recipientName);

    [Alias(nameof(TriggerFailingDelivery))]
    Task TriggerFailingDelivery(string targetName, string recipientName);
}

[Alias("DigitalBrain.ModuleTests.IRollbackWitness")]
public partial interface IRollbackWitness : INeuron;

public sealed class RollbackWitness : Neuron, IRollbackWitness, IHandle<OrphanedByRollback>
{
    public Task HandleAsync(OrphanedByRollback synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class StreamedTurnTarget : Neuron, IStreamedTurnTarget, IHandle<FailingTurnTrigger>
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static TaskCompletionSource _streamRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource _turnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _remainingFailures;

    internal static Task TurnEntered => _turnEntered.Task;

    internal static void ArmFailures(int failures)
    {
        _streamRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _turnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _remainingFailures = failures;
    }

    public async IAsyncEnumerable<string> StreamOnce()
    {
        _streamRecorded.TrySetResult();

        yield return await Task.FromResult("streamed");
    }

    public async Task StageThenFail(string recipientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);

        await SendAsync(NeuronId.For<IRollbackWitness>(Id.Owner, recipientName), new OrphanedByRollback());

        _turnEntered.TrySetResult();

        await _streamRecorded.Task.WaitAsync(Budget);

        throw new InvalidOperationException("The staged capability turn fails after a streamed request was committed.");
    }

    public async Task HandleAsync(FailingTurnTrigger synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        _turnEntered.TrySetResult();

        await _streamRecorded.Task.WaitAsync(Budget, cancellationToken);

        if (Interlocked.Decrement(ref _remainingFailures) >= 0)
        {
            throw new InvalidOperationException("The delivered turn fails after a streamed request was committed.");
        }
    }
}

public sealed class StreamedTurnCaller : Neuron, IStreamedTurnCaller
{
    public async Task DrainStreamedTurnTarget(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        await foreach (var _ in Target(targetName).StreamOnce())
        {
        }
    }

    public async Task<bool> ProvokeStagedRollback(string targetName, string recipientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        try
        {
            await Target(targetName).StageThenFail(recipientName);
        }
        catch (Exception failure) when (failure is not NeuronAuthorizationException)
        {
            return true;
        }

        return false;
    }

    public Task TriggerFailingDelivery(string targetName, string recipientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        return SendAsync(
            NeuronId.For<IStreamedTurnTarget>(Id.Owner, targetName),
            new FailingTurnTrigger(recipientName));
    }

    private IStreamedTurnTarget Target(string targetName)
        => GrainFactory.GetGrain<IStreamedTurnTarget>(
            NeuronId.For<IStreamedTurnTarget>(Id.Owner, targetName).ToGrainId());
}

public sealed class StreamedTurnIsolation(ModuleFixture fixture)
{
    private const string TargetName = "streamed-turn-target";
    private const string CallerName = "streamed-turn-caller";
    private const string StreamingCallerName = "streamed-turn-streaming-caller";
    private const string WitnessName = "streamed-turn-witness";

    [Fact(DisplayName = "a concurrent delivered turn that fails does not erase a committed streamed capability fact")]
    public async Task FailingDeliveredTurnDoesNotEraseTheStreamedFact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var target = test.Neuron<IStreamedTurnTarget>(TargetName);
        var caller = test.Neuron<IStreamedTurnCaller>(CallerName);
        var streamingCaller = test.Neuron<IStreamedTurnCaller>(StreamingCallerName);

        StreamedTurnTarget.ArmFailures(1);

        await caller.Reference.TriggerFailingDelivery(TargetName, WitnessName);
        await StreamedTurnTarget.TurnEntered.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await streamingCaller.Reference.DrainStreamedTurnTarget(TargetName);

        var received = await target.Incoming.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Contains(received, fact => fact.Synapse.Method == nameof(IStreamedTurnTarget.StreamOnce));
    }

    [Fact(DisplayName = "a rollback after a streamed commit leaves no orphaned deliverable outbox entry")]
    public async Task RollbackAfterStreamedCommitLeavesNoDeliverableOutboxEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var target = test.Neuron<IStreamedTurnTarget>(TargetName);
        var caller = test.Neuron<IStreamedTurnCaller>(CallerName);
        var streamingCaller = test.Neuron<IStreamedTurnCaller>(StreamingCallerName);
        var witness = test.Neuron<IRollbackWitness>(WitnessName);

        StreamedTurnTarget.ArmFailures(0);

        var rollback = caller.Reference.ProvokeStagedRollback(TargetName, WitnessName);
        await StreamedTurnTarget.TurnEntered.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await streamingCaller.Reference.DrainStreamedTurnTarget(TargetName);

        Assert.True(await rollback);

        await target.RestartHostAsync(cancellationToken);

        StreamedTurnTarget.ArmFailures(0);
        await streamingCaller.Reference.DrainStreamedTurnTarget(TargetName);

        for (var settle = 0; settle < 20; settle++)
        {
            Assert.Empty(await witness.Incoming.ReadAsync<OrphanedByRollback>(afterSequence: 0, cancellationToken: cancellationToken));

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}
