using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;

namespace DigitalBrain.TestingTests;

public sealed partial class TestingProbeModule : IModule;

public sealed class TestingFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TestingProbeModule>();
    }
}
