using DigitalBrain.Contracts;

namespace DigitalBrain.Marketplace.Creators;

[GenerateSerializer, Alias("marketplace.creators.app-published")]
public sealed record AppPublished(string AppId, string Version, string Ring, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.creators.re-consent-required")]
public sealed record CreatorReConsentRequired(string AppId, string Version, IReadOnlyList<string> AddedPermissions) : Signal;

[GenerateSerializer, Alias("marketplace.creators.app-killed")]
public sealed record CreatorAppKilled(string AppId, string Reason) : Signal;
