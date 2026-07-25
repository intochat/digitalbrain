using DigitalBrain.Testing;

namespace DigitalBrain.Flutter.Tests;

public sealed class FlutterFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
    }
}
