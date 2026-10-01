using System.Security.Cryptography;
using DigitalBrain.Platform.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Core.Tests.Unit;

public sealed class MasterKeyWrapperFacts
{
    [Fact]
    public void A_wrapped_key_unwraps_to_the_original_bytes()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wrapped = Wrapper("first master key").Wrap(key);
        Assert.StartsWith("mk3:", wrapped, StringComparison.Ordinal);
        Assert.Equal(key, Wrapper("first master key").Unwrap(wrapped));
    }

    [Fact]
    public void A_different_master_key_cannot_unwrap_the_key()
    {
        var wrapped = Wrapper("first master key").Wrap(RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsAny<CryptographicException>(() => Wrapper("different master key").Unwrap(wrapped));
    }

    [Theory]
    [InlineData("dp2:legacy")]
    [InlineData("unversioned")]
    [InlineData("")]
    public void A_value_without_the_master_key_prefix_is_rejected(string wrapped)
    {
        Assert.Throws<CryptographicException>(() => Wrapper("first master key").Unwrap(wrapped));
    }

    [Fact]
    public void Wrapping_the_same_key_twice_produces_different_ciphertexts()
    {
        var wrapper = Wrapper("first master key");
        var key = RandomNumberGenerator.GetBytes(32);
        Assert.NotEqual(wrapper.Wrap(key), wrapper.Wrap(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void A_missing_master_key_fails_with_an_actionable_message(string? masterKey)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Wrapper(masterKey));
        Assert.Contains("DigitalBrain:MasterKey", error.Message, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain__MasterKey", error.Message, StringComparison.Ordinal);
    }

    private static MasterKeyWrapper Wrapper(string? masterKey) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:MasterKey"] = masterKey })
        .Build());
}
