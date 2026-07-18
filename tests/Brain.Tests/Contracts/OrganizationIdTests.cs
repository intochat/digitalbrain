using Brain.Contracts;
using Xunit;

namespace Brain.Tests.Contracts;

public sealed class OrganizationIdTests
{
    [Fact]
    public void OrganizationId_preserves_value()
    {
        var organizationId = new OrganizationId("org-1");
        Assert.Equal("org-1", organizationId.Value);
    }
}
