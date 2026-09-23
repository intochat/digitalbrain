using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.installed")]
public sealed record AppInstalled(string AppId, string Version, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("apps.uninstalled")]
public sealed record AppUninstalled(string AppId, IReadOnlyList<string> KeptData) : Signal;

[GenerateSerializer, Alias("apps.rolled-back")]
public sealed record AppRolledBack(string AppId, string Version) : Signal;

[GenerateSerializer, Alias("apps.catalogued")]
public sealed record AppCatalogued(string AppId, string Version, DateTimeOffset At) : Signal;
[GenerateSerializer, Alias("apps.consent-approved")]
public sealed record AppConsentApproved(string AppId, string Version, DateTimeOffset At) : Signal;
