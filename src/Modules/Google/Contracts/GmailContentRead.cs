using System.Text.Json;

namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.gmail.content-read")]
public sealed record GmailContentRead(
    [property: Id(0)] JsonElement Content);
