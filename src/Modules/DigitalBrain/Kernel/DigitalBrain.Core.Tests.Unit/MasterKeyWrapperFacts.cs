using System.Security.Cryptography;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        Assert.DoesNotContain(Convert.ToBase64String(key), wrapped, StringComparison.Ordinal);
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
    public async Task A_production_host_rejects_a_blank_master_key_at_startup_with_the_operator_configuration_names(string? masterKey)
    {
        using var host = new HostBuilder().UseEnvironment(Environments.Production)
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { [DigitalBrainNames.MasterKeyConfigurationKey] = masterKey }))
            .ConfigureServices(services => services.AddMasterKeyWrapper())
            .Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains(DigitalBrainNames.MasterKeyConfigurationKey, error.Message, StringComparison.Ordinal);
        Assert.Contains(DigitalBrainNames.MasterKeyEnvironmentVariable, error.Message, StringComparison.Ordinal);
    }

    private static MasterKeyWrapper Wrapper(string masterKey) => new(Options.Create(new MasterKeyOptions { MasterKey = masterKey }));
}
