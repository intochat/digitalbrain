namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-rejection")]
public sealed record CommandRejection(
    [property: Id(0)] int Incarnation,
    [property: Id(1)] string Reason,
    [property: Id(2)] string Message);
