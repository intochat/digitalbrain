using DigitalBrain.Testing;

namespace DigitalBrain.Quickstart.Tests;

public sealed class QuickstartFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
        => brain.AddModule<QuickstartModule>();
}
