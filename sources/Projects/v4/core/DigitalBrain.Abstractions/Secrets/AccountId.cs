using DigitalBrain.Core.Clusters;

namespace DigitalBrain.Abstractions.Secrets;

[GenerateSerializer]
public readonly record struct AccountId([property: Id(0)] string Value)
{
    public static readonly AccountId Connectors = new("connectors");

    public override string ToString() => Value;

    public static implicit operator AccountId(string value) => new(value);
}

[GenerateSerializer]
public readonly record struct SecretKey([property: Id(0)] string Value)
{
    public override string ToString() => Value;

    public static implicit operator SecretKey(string value) => new(value);
}

[GenerateSerializer]
public readonly record struct SecretKeyPath(
    [property: Id(0)] BrainId BrainId,
    [property: Id(1)] AccountId AccountId,
    [property: Id(2)] SecretKey SecretKey)
{
    public string Value => $"{BrainId.Value}:{AccountId.Value}:{SecretKey.Value}";

    public static SecretKeyPath Create(BrainId brainId, AccountId accountId, SecretKey secretKey) =>
        new(brainId, accountId, secretKey);

    public override string ToString() => Value;
}

