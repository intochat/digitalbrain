using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.TextField;
using Orleans.Metadata;
using Orleans.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntoChat.Apps.BuiltIn;

[GenerateSerializer, Alias("intochat.settings-preferences")]
public sealed record SettingsPreferences(
    [property: Id(0)] string DisplayName = "",
    [property: Id(1)] string Theme = "system");

[GenerateSerializer, Alias("intochat.settings-app-state")]
public sealed record SettingsAppState
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public bool Active { get; init; }
    [Id(2)] public SettingsPreferences Preferences { get; init; } = new();
    [Id(3)] public UiChildRef Surface { get; init; } = new("surface", "");
    [Id(4)] public UiChildRef DisplayNameField { get; init; } = new("textfield", "");
    [Id(5)] public UiChildRef ThemeField { get; init; } = new("textfield", "");
}

[GenerateSerializer, Alias("intochat.builtin-app-changed")]
public sealed record BuiltInAppChanged([property: Id(0)] string AppId, [property: Id(1)] long Revision) : Signal;

[Alias("intochat.settings-app"), DefaultGrainType("intochat.settings-app")]
public interface ISettingsApp : INeuron
{
    Task<SettingsAppState> Activate();
    Task<SettingsAppState> Read();
    Task<SettingsAppState> Apply(SettingsPreferences preferences);
    Task<SettingsAppState> ApplyDraft();
}

// The root key is the authenticated workspace scope. Every UI child belongs to that root.
[GrainType("intochat.settings-app")]
public sealed class SettingsApp(
    [PersistentState("intochat.settings-app", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SettingsAppState> store)
    : Neuron<SettingsAppState>(store), ISettingsApp, INeuronObserver
{
    private const string AppId = "intochat.settings";
    private IGrainTimer? _renewal;

    public async Task<SettingsAppState> Activate()
    {
        if (Snapshot.Active) { return Snapshot; }
        var prefix = this.GetPrimaryKeyString() + "/apps/settings";
        var name = GrainFactory.GetGrain<ITextField>(prefix + "/display-name");
        await name.Configure("Display name", "text");
        await name.SetValue(Snapshot.Preferences.DisplayName);
        var theme = GrainFactory.GetGrain<ITextField>(prefix + "/theme");
        await theme.Configure("Theme (system, light, dark)", "text");
        await theme.SetValue(Snapshot.Preferences.Theme);
        var apply = GrainFactory.GetGrain<IButton>(prefix + "/apply");
        await apply.Set("Apply preferences", "settings.apply");
        var layout = GrainFactory.GetGrain<ILayout>(prefix + "/layout");
        await layout.Set(new("column", [new("textfield", prefix + "/display-name"), new("textfield", prefix + "/theme"), new("button", prefix + "/apply")]), (await layout.Read()).Revision);
        var surface = GrainFactory.GetGrain<ISurface>(prefix + "/surface");
        await surface.Set(new("Settings", [new("layout", prefix + "/layout")]), (await surface.Read()).Revision);
        var next = Snapshot with
        {
            Revision = Snapshot.Revision + 1, Active = true,
            Surface = new("surface", prefix + "/surface"),
            DisplayNameField = new("textfield", prefix + "/display-name"),
            ThemeField = new("textfield", prefix + "/theme"),
        };
        await Save(next, new BuiltInAppChanged(AppId, next.Revision));
        await Subscribe();
        return Snapshot;
    }

    public Task<SettingsAppState> Read() => Task.FromResult(Snapshot);

    public async Task<SettingsAppState> Apply(SettingsPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.DisplayName is null || preferences.DisplayName.Length > 120 || preferences.DisplayName.Any(char.IsControl)
            || preferences.Theme is not ("system" or "light" or "dark"))
        { throw new ArgumentException("Use a display name of at most 120 characters and a system, light or dark theme.", nameof(preferences)); }
        await Activate();
        var next = Snapshot with { Preferences = preferences, Revision = Snapshot.Revision + 1 };
        await Save(next, new BuiltInAppChanged(AppId, next.Revision));
        await GrainFactory.GetGrain<ITextField>(Snapshot.DisplayNameField.Name).SetValue(preferences.DisplayName);
        await GrainFactory.GetGrain<ITextField>(Snapshot.ThemeField.Name).SetValue(preferences.Theme);
        return Snapshot;
    }

    public async Task<SettingsAppState> ApplyDraft()
    {
        await Activate();
        var name = await GrainFactory.GetGrain<ITextField>(Snapshot.DisplayNameField.Name).Read();
        var theme = await GrainFactory.GetGrain<ITextField>(Snapshot.ThemeField.Name).Read();
        return await Apply(new(name.Value, theme.Value));
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (Snapshot.Active) { await Subscribe(); }
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _renewal?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    Task INeuronObserver.OnSignalAsync(Signal signal) =>
        signal is ButtonClicked clicked && clicked.Name == this.GetPrimaryKeyString() + "/apps/settings/apply" && clicked.Action == "settings.apply"
            ? ApplyDraft() : Task.CompletedTask;

    private async Task Subscribe()
    {
        var button = GrainFactory.GetGrain<IButton>(this.GetPrimaryKeyString() + "/apps/settings/apply");
        var observer = this.AsReference<INeuronObserver>();
        await button.Watch(observer);
        if (_renewal is not null) { return; }
        var interval = ServiceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.RenewEvery;
        _renewal = this.RegisterGrainTimer((_, _) => button.Watch(observer), 0,
            new GrainTimerCreationOptions { DueTime = interval, Period = interval, Interleave = true, KeepAlive = true });
    }
}
