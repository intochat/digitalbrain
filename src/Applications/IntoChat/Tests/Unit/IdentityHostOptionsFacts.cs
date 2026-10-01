using DigitalBrain.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntoChat.Tests.Unit;

public sealed class IdentityHostOptionsFacts
{
    [Fact]
    public void TheHostPreservesItsCookieAndPersistedProtectionIdentity()
    {
        var services = new ServiceCollection();
        services.AddIntoChatOptions();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<IdentityHostOptions>>().Value;
        Assert.Equal("intochat.session", options.CookieName);
        Assert.Equal("IntoChat.v1", options.ProtectionApplicationName);
        Assert.Equal("intochat-protection-v1", options.ProtectionContainerName);
    }
}
