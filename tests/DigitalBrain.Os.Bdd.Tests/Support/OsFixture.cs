using DigitalBrain.Flutter;
using DigitalBrain.Testing;

namespace DigitalBrain.Os.Bdd.Tests;

public sealed class OsFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
    }
}
