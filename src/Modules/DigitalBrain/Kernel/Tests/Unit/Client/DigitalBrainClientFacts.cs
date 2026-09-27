using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DigitalBrainClientFacts
{
    // Scripts are compiled outside this solution; this call fails to build if a second On<T> overload appears.
    [Fact]
    public void ScriptsListenThroughASingleOnExtension()
    {
        static IAsyncEnumerable<Signal> Listen(IDigitalBrain brain, INeuron source) => brain.On<Signal>(source);

        Assert.NotNull(Listen(null!, null!));
    }

    [Fact]
    public async Task RequiresGatewaysUnlessLocalDevelopment()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DigitalBrainClient.ConnectAsync([], TestContext.Current.CancellationToken));

        Assert.Contains("LocalDevelopment", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptSettingsReadTheKeysEnvironmentVariablesProduce()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CSharpFile:Settings:style"] = "bullets",
            ["CSharpFile:Settings:Account:twitter"] = "bob-twitter",
        }).Build();

        Assert.Equal("bullets", configuration[DigitalBrainConnection.SettingKey("style")]);
        Assert.Equal("bob-twitter", configuration[DigitalBrainConnection.SettingKey("Account__twitter")]);
    }
}
