using Brain.Contracts;

namespace Brain.Gateway;

public sealed class DevelopmentPrincipal
{
    public static DevelopmentPrincipal Current { get; } = new();

    public OrganizationId OrganizationId { get; } = new("dev-organization");
    public PrincipalId PrincipalId { get; } = new("dev-principal");
    public SpaceId SpaceId { get; } = new("dev-space");
}
