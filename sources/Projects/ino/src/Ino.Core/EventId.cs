namespace Ino.Core;

[GenerateSerializer]
public readonly record struct EventId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
    public static EventId New() => new(Ulid.NewUlid().ToString());
}
