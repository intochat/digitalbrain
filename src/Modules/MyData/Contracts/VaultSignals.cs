using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

[GenerateSerializer, Alias("mydata.field-changed")]
public sealed record VaultFieldChanged : Signal
{
    [Id(0)] public string Owner { get; init; } = "";
    [Id(1)] public string FieldPath { get; init; } = "";
    [Id(2)] public FieldKind Kind { get; init; }
    [Id(3)] public bool IsSecret { get; init; }
}

[GenerateSerializer, Alias("mydata.secret-resolved")]
public sealed record VaultSecretResolved : Signal
{
    [Id(0)] public string Owner { get; init; } = "";
    [Id(1)] public string FieldPath { get; init; } = "";
    [Id(2)] public string? AppId { get; init; }
}

[GenerateSerializer, Alias("mydata.erased")]
public sealed record VaultErased : Signal
{
    [Id(0)] public string Owner { get; init; } = "";
}
