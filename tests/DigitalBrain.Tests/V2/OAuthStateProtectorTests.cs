using DigitalBrain.Core;
using DigitalBrain.Kernel.Config;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Tests.V2;

public sealed class OAuthStateProtectorTests
{
    [Fact]
    public void State_is_opaque_tamper_evident_and_owner_specific()
    {
        var protector = new DataProtectionOAuthStateProtector(new EphemeralDataProtectionProvider());
        var firstOwner = new NeuronId("principal-scope-a");
        var secondOwner = new NeuronId("principal-scope-b");

        var first = protector.Protect(firstOwner);
        var second = protector.Protect(secondOwner);

        Assert.DoesNotContain(firstOwner.Value, first, StringComparison.Ordinal);
        Assert.DoesNotContain(secondOwner.Value, second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(protector.TryUnprotect(first, out var recoveredFirst));
        Assert.True(protector.TryUnprotect(second, out var recoveredSecond));
        Assert.Equal(firstOwner, recoveredFirst);
        Assert.Equal(secondOwner, recoveredSecond);
        Assert.False(protector.TryUnprotect(first + "tampered", out _));
    }
}
