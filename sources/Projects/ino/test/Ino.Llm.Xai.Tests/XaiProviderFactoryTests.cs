using Ino.Core;
using Ino.Core.Hosting.Llm;
using Ino.Llm.Xai;
using Ino.Llm.Xai.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ino.Llm.Xai.Tests;

public class XaiProviderFactoryTests
{
    static IConfiguration ConfigWith(string? xaiApiKey)
    {
        var data = xaiApiKey is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [LlmConfig.XaiApiKey] = xaiApiKey };
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Fact]
    public void Provider_is_xai()
    {
        Assert.Equal("xai", new XaiProviderFactory().Provider);
    }

    [Fact]
    public void IsConfigured_true_when_api_key_present()
    {
        var factory = new XaiProviderFactory();
        Assert.True(factory.IsConfigured(ConfigWith("test")));
    }

    [Fact]
    public void IsConfigured_false_when_api_key_missing()
    {
        var factory = new XaiProviderFactory();
        Assert.False(factory.IsConfigured(ConfigWith(null)));
        Assert.False(factory.IsConfigured(ConfigWith("")));
        Assert.False(factory.IsConfigured(ConfigWith("   ")));
    }

    [Fact]
    public void CreateClient_throws_when_api_key_missing()
    {
        var factory = new XaiProviderFactory();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            factory.CreateClient(new Grok4FastNonReasoning(), ConfigWith(null)));
        Assert.Contains("xai-api-key", ex.Message);
    }

    [Fact]
    public void CreateClient_returns_chat_client_when_configured()
    {
        var factory = new XaiProviderFactory();
        var client = factory.CreateClient(new Grok4FastNonReasoning(), ConfigWith("test"));
        Assert.NotNull(client);
    }
}

public class ProviderBackedChatClientFactoryTests
{
    static IConfiguration Config => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [LlmConfig.XaiApiKey] = "test" })
        .Build();

    static (LlmModel, LlmTier, ILlmProviderFactory) Bind(LlmModel model, LlmTier tier)
        => (model, tier, new XaiProviderFactory());

    [Fact]
    public void ForTier_returns_client_for_bound_tier()
    {
        var bindings = new[]
        {
            Bind(new Grok4FastNonReasoning(), LlmTier.Fast),
            Bind(new Grok4FastReasoning(), LlmTier.Balanced),
            Bind(new Grok420(), LlmTier.Reasoning),
        };
        var factory = new ProviderBackedChatClientFactory(bindings, Config);

        Assert.NotNull(factory.ForTier(LlmTier.Fast));
        Assert.NotNull(factory.ForTier(LlmTier.Balanced));
        Assert.NotNull(factory.ForTier(LlmTier.Reasoning));
    }

    [Fact]
    public void ForTier_falls_back_to_highest_below_when_tier_unbound()
    {
        var bindings = new[]
        {
            Bind(new Grok4FastNonReasoning(), LlmTier.Fast),
            Bind(new Grok420(), LlmTier.Reasoning),
        };
        var factory = new ProviderBackedChatClientFactory(bindings, Config);

        var balancedClient = factory.ForTier(LlmTier.Balanced);
        Assert.NotNull(balancedClient);
        Assert.Same(factory.ForTier(LlmTier.Fast), balancedClient);
    }

    [Fact]
    public void ForTier_throws_for_None()
    {
        var bindings = new[] { Bind(new Grok4FastNonReasoning(), LlmTier.Fast) };
        var factory = new ProviderBackedChatClientFactory(bindings, Config);

        var ex = Assert.Throws<ArgumentException>(() => factory.ForTier(LlmTier.None));
        Assert.Contains("None", ex.Message);
    }

    [Fact]
    public void RegisteredModels_mirrors_bindings()
    {
        var bindings = new[]
        {
            Bind(new Grok4FastNonReasoning(), LlmTier.Fast),
            Bind(new Grok4FastReasoning(), LlmTier.Balanced),
        };
        var factory = new ProviderBackedChatClientFactory(bindings, Config);

        Assert.Equal(2, factory.RegisteredModels.Count);
        Assert.Contains(factory.RegisteredModels, m => m.Id == "grok-4-1-fast-non-reasoning");
    }
}
