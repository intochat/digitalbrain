using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Os.Bdd.Tests;

public sealed class BrainWorld
{
    private TestBrain? _brain;

    public TestBrain Brain =>
        _brain ?? throw new InvalidOperationException(
            "No DigitalBrain is open. A scenario must start with a Given that opens one.");

    public static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name)
        where TNeuron : class, INeuron
        => Brain.Neuron<TNeuron>(name);

    internal async Task OpenAsync()
    {
        await CloseAsync();
        _brain = await OsCluster.Fixture.CreateBrainAsync(CancellationToken);
    }

    internal async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _brain, null) is { } brain)
        {
            await brain.DisposeAsync();
        }
    }
}
