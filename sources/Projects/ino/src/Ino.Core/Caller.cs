namespace Ino.Core;

[GenerateSerializer]
public abstract record Caller
{
    [GenerateSerializer]
    public sealed record FromDomain([property: Id(0)] DomainId Domain) : Caller;

    [GenerateSerializer]
    public sealed record Ambient([property: Id(0)] DomainId Domain) : Caller;

    private Caller() { }
}
