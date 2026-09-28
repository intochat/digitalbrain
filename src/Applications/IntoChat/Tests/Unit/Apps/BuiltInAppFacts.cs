using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Testing.Unit;
using IntoChat.Apps.BuiltIn;
using Xunit;

namespace IntoChat.Tests;

public sealed class BuiltInAppFacts
{
    [Fact]
    public async Task SettingsOwnsEditableNeuronsAndPreservesPreferencesAcrossActivation()
    {
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .WithFastSubscriptions().StartAsync(TestContext.Current.CancellationToken);
        var first = brain.Get<ISettingsApp>("workspace-a");
        var second = brain.Get<ISettingsApp>("workspace-b");
        var opened = await first.Activate();
        var other = await second.Activate();
        await brain.Get<ITextField>(opened.DisplayNameField.Name).SetValue("Alice");
        await brain.Get<ITextField>(opened.ThemeField.Name).SetValue("dark");
        // A settings window must remain wired after the original observer lease expires.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        await brain.Get<IButton>("workspace-a/apps/settings/apply").Click();
        var applied = await first.Read();
        var reopened = await first.Activate();

        Assert.Equal(new SettingsPreferences("Alice", "dark"), applied.Preferences);
        Assert.Equal(applied.Revision, reopened.Revision);
        Assert.Equal("Alice", (await brain.Get<ITextField>(reopened.DisplayNameField.Name).Read()).Value);
        Assert.Equal(new SettingsPreferences(), (await second.Read()).Preferences);
        Assert.NotEqual(opened.Surface.Name, other.Surface.Name);
        Assert.NotEmpty((await brain.Get<ISurface>(opened.Surface.Name).Read()).Definition.Children);
        await Assert.ThrowsAsync<ArgumentException>(() => first.Apply(new SettingsPreferences("Alice", "invalid")));
        Assert.Equal(applied.Preferences, (await first.Read()).Preferences);
    }

}
