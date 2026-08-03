using DigitalBrain.Testing;
using DigitalBrain.TestingTests.Harness;
using Xunit;

namespace DigitalBrain.TestingTests;

public abstract class GreeterNeuronTest : NeuronTest<IGreeter>
{
    protected override void Compose(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<GreeterModule>();
    }
}

public sealed class NeuronBaseContracts(BrainTestClusters clusters) : GreeterNeuronTest
{
    [Fact(DisplayName = "NeuronTest resolves the neuron under test and journals what it emits")]
    public async Task NeuronUnderTestJournalsItsEmission()
    {
        var brain = await BrainAsync();
        var greeter = await NeuronAsync(TestingScenario.WelcomeGreeter);

        await brain.Client.SendAsync<IGreeter>(
            greeter.Id.Name, new SayHello(TestingScenario.Guest), Cancellation);

        var greeted = await greeter.Outgoing.NextAsync<Greeted>(Cancellation);
        Assert.Equal(TestingScenario.GreetedMessage(TestingScenario.Guest), greeted.Synapse.Message);
    }

    [Fact(DisplayName = "One test method leases one brain however often it is asked for")]
    public async Task OneTestMethodLeasesOneBrain()
    {
        var first = await BrainAsync();
        var second = await BrainAsync();

        Assert.Same(first, second);
    }

    [Fact(DisplayName =
        "Test classes sharing a composition run on the one cluster that composition booted")]
    public async Task SharedCompositionBootsOneCluster()
    {
        _ = await BrainAsync();

        Assert.True(clusters.HasBootedCluster(typeof(GreeterNeuronTest)));
        Assert.Equal(1, clusters.BootedClusters);
    }
}

public sealed class SharedCompositionContracts(BrainTestClusters clusters) : GreeterNeuronTest
{
    [Fact(DisplayName = "A second class on the same composition adds no cluster of its own")]
    public async Task ASecondClassOnTheSameCompositionAddsNoCluster()
    {
        var greeter = await NeuronAsync(TestingScenario.WelcomeGreeter);

        Assert.True(clusters.HasBootedCluster(typeof(GreeterNeuronTest)));
        Assert.Equal(1, clusters.BootedClusters);
        Assert.NotNull(greeter.Reference);
    }
}

public sealed class UncomposedBrainContracts(BrainTestClusters clusters) : DigitalBrainTest
{
    protected override void Compose(DigitalBrainTestBuilder brain)
    {
    }

    [Fact(DisplayName = "A test that composes nothing and asks for no brain boots no cluster")]
    public void ATestThatNeverAsksForABrainBootsNoCluster()
        => Assert.False(clusters.HasBootedCluster(typeof(UncomposedBrainContracts)));
}

public sealed class CompositionDivergenceContracts
{
    [Fact(DisplayName =
        "A composition reuses its unbooted cluster and refuses a divergent module set")]
    public async Task ACompositionRefusesADivergentModuleSet()
    {
        await using var clusters = new BrainTestClusters();

        var first = clusters.FixtureFor(
            typeof(CompositionDivergenceContracts), brain => brain.AddModule<GreeterModule>());
        var repeated = clusters.FixtureFor(
            typeof(CompositionDivergenceContracts), brain => brain.AddModule<GreeterModule>());

        Assert.Same(first, repeated);
        Assert.False(first.HasBooted);

        var divergent = Assert.Throws<InvalidOperationException>(
            () => clusters.FixtureFor(
                typeof(CompositionDivergenceContracts), brain => brain.AddModule<CapabilityProbeModule>()));

        Assert.Contains("cannot also serve", divergent.Message, StringComparison.Ordinal);
    }
}
