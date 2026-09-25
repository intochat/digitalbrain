namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.profile-state")]
internal sealed record CodingProfileState([property: Id(0)] CodingProfile? Profile);
