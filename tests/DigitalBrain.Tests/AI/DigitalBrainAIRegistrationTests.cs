using Anthropic;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Xunit;

namespace DigitalBrain.Tests.AI;

public sealed class DigitalBrainAIRegistrationTests
{
    [Fact]
    public void Kernel_graph_registers_concrete_role_wrappers_without_keyed_provider_DI()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDigitalBrainAI(
            CompleteConfiguration(),
            new DigitalBrainAIHttpClients(
                OpenAI: new HttpClient(new ProviderTestHttpHandler((_, _) => Task.FromResult(
                    ProviderTestHttpHandler.Json(
                        OpenAIProviderClientTests.ChatResponseJson("gpt-5-mini", "ok"))))),
                Anthropic: new HttpClient(new ProviderTestHttpHandler((_, _) => Task.FromResult(
                    ProviderTestHttpHandler.Json(
                        AnthropicProviderClientTests.MessageResponseJson("ok")))))));

        using var provider = services.BuildServiceProvider();

        var fast = provider.GetRequiredService<FastModelClient>();
        var balanced = provider.GetRequiredService<BalancedModelClient>();
        var reasoning = provider.GetRequiredService<ReasoningModelClient>();
        var embedding = provider.GetRequiredService<EmbeddingModelClient>();
        Assert.IsType<ChatClient>(fast.Client.GetService(typeof(ChatClient)));
        Assert.NotNull(balanced.Client.GetService(typeof(IAnthropicClient)));
        Assert.IsType<ChatClient>(reasoning.Client.GetService(typeof(ChatClient)));
        Assert.IsType<EmbeddingClient>(embedding.Client.GetService(typeof(EmbeddingClient)));
        Assert.False(Assert.IsType<OpenTelemetryChatClient>(fast.Client).EnableSensitiveData);
        Assert.False(Assert.IsType<OpenTelemetryChatClient>(balanced.Client).EnableSensitiveData);
        Assert.False(Assert.IsType<OpenTelemetryChatClient>(reasoning.Client).EnableSensitiveData);
        Assert.False(
            Assert.IsType<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>(embedding.Client)
                .EnableSensitiveData);
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceKey is not null &&
                (descriptor.ServiceType == typeof(IChatClient) ||
                 descriptor.ServiceType.IsGenericType &&
                 descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IEmbeddingGenerator<,>)));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IChatClient));
    }

    [Fact]
    public async Task Health_check_is_registered_without_sending_credentials_or_model_content()
    {
        var openAIHandler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json(
                OpenAIProviderClientTests.ChatResponseJson("gpt-5-mini", "unused"))));
        var anthropicHandler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json(
                AnthropicProviderClientTests.MessageResponseJson("unused"))));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDigitalBrainAI(
            CompleteConfiguration(),
            new DigitalBrainAIHttpClients(
                new HttpClient(openAIHandler),
                new HttpClient(anthropicHandler)));
        using var provider = services.BuildServiceProvider();

        var result = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == DigitalBrainAIHealthCheck.Name);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(openAIHandler.Requests);
        Assert.Empty(anthropicHandler.Requests);
        Assert.DoesNotContain(
            result.Entries.Values.SelectMany(entry => entry.Data.Values),
            value => value?.ToString()?.Contains("test-", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("DigitalBrain:AI:OpenAI:ApiKey")]
    [InlineData("DigitalBrain:AI:OpenAI:Endpoint")]
    [InlineData("DigitalBrain:AI:OpenAI:FastModelId")]
    [InlineData("DigitalBrain:AI:OpenAI:ReasoningModelId")]
    [InlineData("DigitalBrain:AI:OpenAI:EmbeddingModelId")]
    [InlineData("DigitalBrain:AI:Anthropic:ApiKey")]
    [InlineData("DigitalBrain:AI:Anthropic:Endpoint")]
    [InlineData("DigitalBrain:AI:Anthropic:BalancedModelId")]
    public void Missing_provider_configuration_fails_options_validation(string missingKey)
    {
        var values = CompleteConfigurationValues();
        values.Remove(missingKey);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDigitalBrainAI(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;
        });
    }

    [Fact]
    public async Task Missing_provider_configuration_fails_host_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDigitalBrainAI(new ConfigurationBuilder().Build());
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<AggregateException>(() => host.StartAsync());
        Assert.Equal(
            2,
            failure.InnerExceptions.Count(exception => exception is OptionsValidationException));
    }

    [Theory]
    [InlineData("DigitalBrain:AI:OpenAI:Endpoint")]
    [InlineData("DigitalBrain:AI:Anthropic:Endpoint")]
    public void Malformed_provider_endpoint_fails_options_validation(string endpointKey)
    {
        var values = CompleteConfigurationValues();
        values[endpointKey] = "not-an-absolute-http-endpoint";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDigitalBrainAI(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;
        });
    }

    [Theory]
    [InlineData("DigitalBrain:AI:OpenAI:Endpoint")]
    [InlineData("DigitalBrain:AI:Anthropic:Endpoint")]
    public void Non_loopback_plaintext_provider_endpoint_fails_options_validation(string endpointKey)
    {
        var values = CompleteConfigurationValues();
        values[endpointKey] = "http://provider.example";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDigitalBrainAI(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;
        });
    }

    [Fact]
    public void Provider_options_and_role_services_are_not_public_contracts()
    {
        foreach (var type in new[]
                 {
                     typeof(OpenAIProviderOptions),
                     typeof(AnthropicProviderOptions),
                     typeof(FastModelClient),
                     typeof(BalancedModelClient),
                     typeof(ReasoningModelClient),
                     typeof(EmbeddingModelClient),
                     typeof(ConversationRoleInvoker)
                 })
            Assert.False(type.IsPublic);
    }

    internal static IConfiguration CompleteConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(CompleteConfigurationValues())
            .Build();

    internal static Dictionary<string, string?> CompleteConfigurationValues() =>
        new(StringComparer.Ordinal)
        {
            ["DigitalBrain:AI:OpenAI:ApiKey"] = "test-openai-key",
            ["DigitalBrain:AI:OpenAI:Endpoint"] = "https://openai.test/v1",
            ["DigitalBrain:AI:OpenAI:FastModelId"] = "gpt-5-mini",
            ["DigitalBrain:AI:OpenAI:ReasoningModelId"] = "gpt-5",
            ["DigitalBrain:AI:OpenAI:EmbeddingModelId"] = "text-embedding-3-small",
            ["DigitalBrain:AI:Anthropic:ApiKey"] = "test-anthropic-key",
            ["DigitalBrain:AI:Anthropic:Endpoint"] = "https://anthropic.test",
            ["DigitalBrain:AI:Anthropic:BalancedModelId"] = "claude-sonnet-4-5"
        };
}
