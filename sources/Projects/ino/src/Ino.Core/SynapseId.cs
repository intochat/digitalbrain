namespace Ino.Core;

[GenerateSerializer]
public readonly record struct SynapseId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
    public static SynapseId New() => new(Ulid.NewUlid().ToString());
}
