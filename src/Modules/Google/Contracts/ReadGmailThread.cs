namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.gmail.read-thread")]
public sealed record ReadGmailThread(
    [property: Id(0)] string ThreadId,
    [property: Id(1)] string MessageFormat = "MINIMAL");
