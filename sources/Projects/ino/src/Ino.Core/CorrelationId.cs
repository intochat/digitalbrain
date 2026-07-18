namespace Ino.Core;

[GenerateSerializer]
public readonly record struct CorrelationId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
    public static CorrelationId New() => new(Ulid.NewUlid().ToString());
}
