using DigitalBrain.Quickstart;
using DigitalBrain.Testing;

namespace DigitalBrain.TestingTests;

public sealed class TestingFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<QuickstartModule>();
        brain.AddModule<CapabilityProbeModule>();
    }
}
