using Brain.Gateway;
using Xunit;

namespace Brain.Tests.Gateway;

public sealed class DevelopmentPrincipalTests
{
    [Fact]
    public void Development_principal_populates_organization_principal_and_space()
    {
        var first = DevelopmentPrincipal.Current;
        var second = DevelopmentPrincipal.Current;

        Assert.False(string.IsNullOrWhiteSpace(first.OrganizationId.Value));
        Assert.False(string.IsNullOrWhiteSpace(first.PrincipalId.Value));
        Assert.False(string.IsNullOrWhiteSpace(first.SpaceId.Value));
        Assert.Equal(first.OrganizationId, second.OrganizationId);
        Assert.Equal(first.PrincipalId, second.PrincipalId);
        Assert.Equal(first.SpaceId, second.SpaceId);
    }
}
