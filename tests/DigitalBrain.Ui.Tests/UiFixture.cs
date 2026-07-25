using DigitalBrain.Flutter;
using DigitalBrain.Testing;

namespace DigitalBrain.Ui.Tests;

public sealed class UiFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
    }
}
