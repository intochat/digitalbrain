using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.consent-approved")]
public sealed record AppConsentApproved(string AppId, string Version, DateTimeOffset At) : Signal;
