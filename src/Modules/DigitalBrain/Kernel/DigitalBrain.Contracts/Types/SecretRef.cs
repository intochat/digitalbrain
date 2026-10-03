namespace DigitalBrain.Contracts.Types;

public enum SecretStatus
{
    Unset = 0,
    Set = 1,
}

[GenerateSerializer, Alias("semantic.secret-ref")]
public sealed record SecretRef
{
    [Id(0)] public string Reference { get; init; } = "";
    [Id(1)] public string Label { get; init; } = "";
    [Id(2)] public SecretStatus Status { get; init; }

    public bool IsSet => Status == SecretStatus.Set;

}
