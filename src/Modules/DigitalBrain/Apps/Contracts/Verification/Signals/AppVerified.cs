using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.app-verified")]
public sealed record AppVerified([property: Id(0)] PackageRevisionRef Revision, [property: Id(1)] bool Green) : Signal;
