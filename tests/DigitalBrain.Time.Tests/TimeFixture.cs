using DigitalBrain.Testing;

namespace DigitalBrain.Time.Tests;

public sealed class TimeFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TimeModule>();
    }
}
