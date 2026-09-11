using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command")]
public abstract record Command(
    [property: Id(0)] CommandId Id,
    [property: Id(1)] long? ExpectedVersion = null);
