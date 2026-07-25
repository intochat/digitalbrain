using DigitalBrain.Aspire;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Testing;
using DigitalBrain.Ui;

namespace DigitalBrain.Ui.Tests;

public sealed class UiFixture : DigitalBrainFixture
{
    public const string HealthPath = UiEdgeContract.HealthPath;

    public const string OpenScenePath = UiEdgeContract.OpenScenePath;

    public const string ShellEventsPath = UiEdgeContract.ShellEventsPath;

    public const string ActivateControlPath = UiEdgeContract.ActivateControlPath;

    public const string SceneOpenedEvent = UiEdgeContract.SceneOpenedEvent;

    public const string DefaultShellName = FlutterHostingExtensions.DefaultShellName;

    public const string DefaultUiResourceName = FlutterHostingExtensions.DefaultUiResourceName;

    public const string UiBaseEnvironmentVariable = FlutterHostingExtensions.UiBaseEnvironmentVariable;

    public const string DefaultOwner = FlutterHostingExtensions.DefaultOwner;

    public const string OwnerConfigurationKey = DigitalBrainClientHostingExtensions.OwnerConfigurationKey;

    public static Uri ResolveProductUiBaseAddress()
    {
        var configured = Environment.GetEnvironmentVariable(UiBaseEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return LaunchSettingsUiBase;
        }

        return new Uri(configured.TrimEnd('/') + "/");
    }

    private static readonly Uri LaunchSettingsUiBase = new("http://localhost:5080/");

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
    }
}
