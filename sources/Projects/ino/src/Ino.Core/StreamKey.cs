namespace Ino.Core;

[GenerateSerializer]
public readonly record struct StreamKey([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}
