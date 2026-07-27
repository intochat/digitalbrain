using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class BrainWorld
{
    private static readonly TimeSpan ScenarioDeadline = TimeSpan.FromSeconds(60);

    private CancellationTokenSource? _deadline;
    private TestBrain? _brain;

    public TestBrain Brain =>
        _brain ?? throw new InvalidOperationException(
            "No DigitalBrain is open. A scenario must start with a Given that opens one.");

    public CancellationToken CancellationToken =>
        _deadline?.Token ?? TestContext.Current.CancellationToken;

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name)
        where TNeuron : class, INeuron
        => Brain.Neuron<TNeuron>(name);

    internal async Task OpenAsync()
    {
        await CloseAsync();

        _deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        _deadline.CancelAfter(ScenarioDeadline);
        _brain = await OSCluster.Fixture.CreateBrainAsync(_deadline.Token);
    }

    internal async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _brain, null) is { } brain)
        {
            await brain.DisposeAsync();
        }

        if (Interlocked.Exchange(ref _deadline, null) is { } deadline)
        {
            deadline.Dispose();
        }
    }
}
