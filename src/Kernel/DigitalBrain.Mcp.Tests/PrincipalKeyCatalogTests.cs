using DigitalBrain.Mcp;

namespace DigitalBrain.Mcp.Tests;

// MCP operator principal catalog is explicit (not free-form spoof in production).
// Full cookie/auth gate is host-level; this locks the known bootstrap keys.
public sealed class PrincipalKeyCatalogTests
{
    [Theory]
    [InlineData("operator")]
    [InlineData("alice")]
    [InlineData("bob")]
    public void ResolvePrincipal_accepts_known_keys(string key)
    {
        var (principal, username) = ChatTools.ResolvePrincipal(key);
        Assert.NotEqual(Guid.Empty, principal.Value);
        Assert.False(string.IsNullOrWhiteSpace(username));
    }

    [Fact]
    public void ResolvePrincipal_rejects_unknown_key()
    {
        Assert.ThrowsAny<Exception>(() => ChatTools.ResolvePrincipal("eve"));
    }
}
