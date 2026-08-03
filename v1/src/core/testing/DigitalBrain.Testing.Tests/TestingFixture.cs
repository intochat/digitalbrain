using DigitalBrain.Testing;
using DigitalBrain.TestingTests.Harness;

namespace DigitalBrain.TestingTests;

public sealed class TestingFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<GreeterModule>();
        brain.AddModule<CapabilityProbeModule>();
    }
}
