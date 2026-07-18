namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.organization-id.v1")]
public readonly record struct OrganizationId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer, Alias("brain.principal-id.v1")]
public readonly record struct PrincipalId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer, Alias("brain.space-id.v1")]
public readonly record struct SpaceId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}
