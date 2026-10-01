namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("intochat.app-draft-attempt")]
public sealed record AppDraftAttempt(
    [property: Id(0)] string Revision,
    [property: Id(1)] bool Green,
    [property: Id(2)] string Failures);

