using DigitalBrain.Testing;

namespace DigitalBrain.Flutter.Tests;

public sealed class FlutterFixture : DigitalBrainFixture
{
    public const string ShellName = "desk";

    public const string HomeSceneKey = "home";

    public const string HomeSceneTitle = "Home";

    public const string PrimaryControlId = "primary";

    public const string SubmitIntent = "submit";

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
    }
}
