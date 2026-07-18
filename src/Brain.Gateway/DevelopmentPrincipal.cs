using Brain.Contracts;

namespace Brain.Gateway;

public static class DevelopmentPrincipal
{
    public static readonly OrganizationId OrganizationId = new("dev-organization");
    public static readonly PrincipalId PrincipalId = new("dev-principal");
    public static readonly SpaceId SpaceId = new("dev-space");
}
