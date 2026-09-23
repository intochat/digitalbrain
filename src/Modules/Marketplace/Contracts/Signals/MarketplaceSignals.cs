using DigitalBrain.Contracts;

namespace DigitalBrain.Marketplace.Signals;

[GenerateSerializer, Alias("marketplace.certified")]
public sealed record AppCertified(string AppId, string Version, CertificationOutcome Outcome, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.listed")]
public sealed record AppListed(string AppId, string Version, ListingStatus Status, PublishRing Ring, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.intent-accepted")]
public sealed record AppIntentAccepted(string AppId, string Intent, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.app-disabled")]
public sealed record AppDisabled(string AppId, string StatementOfReasons, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.taken-down")]
public sealed record AppTakenDown(string AppId, string Reasons, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.reported")]
public sealed record AppReported(string AppId, string WorkspaceId, string Reason, DateTimeOffset At) : Signal;

[GenerateSerializer, Alias("marketplace.reviewed")]
public sealed record AppReviewed(string AppId, string WorkspaceId, int Stars, DateTimeOffset At) : Signal;
