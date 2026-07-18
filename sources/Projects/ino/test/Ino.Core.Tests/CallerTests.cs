using Ino.Core;
using Xunit;

namespace Ino.Core.Tests;

public class CallerTests
{
    [Fact]
    public void FromDomain_carries_the_domain_id()
    {
        var c = new Caller.FromDomain(DomainId.From("Ino.Domains.Travel"));
        Assert.Equal("Ino.Domains.Travel", c.Domain.Value);
    }

    [Fact]
    public void Ambient_carries_the_originating_domain()
    {
        var c = new Caller.Ambient(DomainId.From("kernel"));
        Assert.Equal("kernel", c.Domain.Value);
    }

    [Fact]
    public void Pattern_match_discriminates_cases()
    {
        Caller c1 = new Caller.FromDomain(DomainId.From("x"));
        Caller c2 = new Caller.Ambient(DomainId.From("domains"));

        Assert.True(c1 is Caller.FromDomain);
        Assert.True(c2 is Caller.Ambient);
    }
}
