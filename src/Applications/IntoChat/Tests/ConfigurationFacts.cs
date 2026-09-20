using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Google;

namespace IntoChat.Tests;

public sealed class ConfigurationFacts
{
    [Fact]
    public void ExplicitDefaultIsNativeWindow()
    {
        var composition = new BrainCompositionBuilder().WithModule<FlutterModule>(flutter => flutter.WithWindowHost()).Build();
        Assert.Equal("Window", Assert.Single(composition.Modules).Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
    }

    [Fact]
    public void DeepSettingsSurviveModuleOverrideTransport()
    {
        var app = new BrainCompositionBuilder().WithModule<FlutterModule>().WithModule<GoogleModule>();
        var overrides = new CompositionOverrides()
            .ConfigureModule<FlutterModule>(flutter => flutter.WithOptions(new()
            {
                Hosting = new() { Kind = FlutterHostKind.Web, ChatName = "scenario", ShellName = "visible" },
            }))
            .ConfigureModule<GoogleModule>(google => google.WithOptions(new()
            {
                PublicOrigin = new("http://localhost:1234/"), TokenEndpoint = new("http://localhost:1234/token"),
            }));
        var settings = app.ApplyOverrides(overrides.Serialize()).Build().Modules.SelectMany(m => m.Configuration).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("scenario", settings["DigitalBrain:Flutter:Hosting:ChatName"]);
        Assert.Equal("visible", settings["DigitalBrain:Flutter:Hosting:ShellName"]);
        Assert.Equal("Web", settings["DigitalBrain:Flutter:Hosting:Kind"]);
        Assert.Equal("http://localhost:1234/token", settings[GoogleModule.GmailOAuthConfigurationRoot + ":TokenEndpoint"]);
    }
}
