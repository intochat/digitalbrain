using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class FixtureCompositionContracts(TestingFixture fixture)
{
    [Fact]
    public void DuplicateModuleIdentityIsRejected()
    {
        var builder = new DigitalBrainTestBuilder();
        builder.AddModule<TestingProbeModule>();

        var failure = Assert.Throws<InvalidOperationException>(
            () => builder.AddModule<TestingProbeModule>());

        Assert.Equal(
            $"Module '{TestingProbeModule.Id}' is already configured for this fixture.",
            failure.Message);
    }

    [Fact]
    public void SealedCompositionRejectsFurtherModules()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            fixture.AddProbeModuleAfterInitialization);

        Assert.Equal(
            "The DigitalBrain test composition is already sealed.",
            failure.Message);
    }
}
