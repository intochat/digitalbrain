using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.validation")]
public sealed record BehaviorValidation(
    [property: Id(0)] bool Valid,
    [property: Id(1)] IReadOnlyList<string> Errors,
    [property: Id(2)] IReadOnlyList<string> Order);
