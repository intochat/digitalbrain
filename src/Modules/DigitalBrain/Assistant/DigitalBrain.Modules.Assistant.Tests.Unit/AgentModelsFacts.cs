using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Assistant.Tests.Unit;

public sealed class AgentModelsFacts
{
    [Fact]
    public void CompletedTurnReplaysWithoutTheRemovedProviderOrProfile()
    {
        using var services = Services(_ => { });
        var completed = new AgentConversationTurn("run", "hello", "persisted answer", []);
        var snapshot = new AgentConversationState(2, null, [completed]);
        var input = new AssistantRun("thread", "run", "hello", "owner", "profile:removed");

        var prepared = AssistantTurnExecution.PrepareTurn(snapshot, input, Catalog(services));

        Assert.Equal(completed, prepared.Replay);
        Assert.Null(prepared.Model);
        Assert.Throws<ArgumentException>(() => AssistantTurnExecution.PrepareTurn(snapshot, input with { RunId = "fresh" }, Catalog(services)));
        Assert.Throws<InvalidOperationException>(() => AssistantTurnExecution.PrepareTurn(snapshot,
            input with { Message = "different message" }, Catalog(services)));
        Assert.Null(snapshot.ActiveRunId);
        Assert.Single(snapshot.Turns);
    }

    [Fact]
    public void CatalogContainsOnlyConfiguredToolModelsAndNeverSecretsOrEndpoints()
    {
        using var services = Services(options =>
        {
            options.ModelProfiles["work"] = new() { Provider = "OpenAI", Model = "private-chat", Capabilities = LlmCapabilities.Tools };
            options.ModelProfiles["no-tools"] = new() { Provider = "OpenAI", Model = "text-only" };
            options.ModelProfiles["unconfigured"] = new() { Provider = "Google", Model = "remote-chat", Capabilities = LlmCapabilities.Tools };
        }, new FixedAiCredentials().Ready("openai", "secret-api-key", "https://private.example/v1"));
        var catalog = Catalog(services);
        var result = catalog.Read();
        Assert.True(result.Automatic.Available);
        Assert.Contains(result.Models, model => model.Id == "profile:work");
        Assert.Contains(result.Models, model => model.Id == "preset:IGpt56Sol");
        Assert.DoesNotContain(result.Models, model => model.Id is "profile:no-tools" or "profile:unconfigured");
        Assert.Throws<ArgumentException>(() => catalog.Select("profile:no-tools"));
        Assert.Throws<ProviderUnavailableException>(() => catalog.Select("profile:unconfigured"));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("secret-api-key", json);
        Assert.DoesNotContain("private.example", json);
        Assert.DoesNotContain("Endpoint", json);
    }

    [Fact]
    public void UnconfiguredDeploymentHasUnavailableAutomaticAndNoChoices()
    {
        using var services = Services(_ => { });
        var catalog = Catalog(services);
        Assert.False(catalog.Read().Automatic.Available);
        Assert.Empty(catalog.Read().Models);
        Assert.Throws<ArgumentException>(() => catalog.Select(null));
    }

    [Fact]
    public void SelectionUsesCatalogIdsAndPreservesAutomaticServerOverride()
    {
        using var services = Services(options =>
        {
            options.ModelProfiles["work"] = new() { Provider = "OpenAI", Model = "private-chat", Capabilities = LlmCapabilities.Tools };
        }, new FixedAiCredentials().Ready("openai", "configured"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["IntoChat:Assistant:Model"] = "IGpt56Luna" }).Build();
        var catalog = Catalog(services, configuration);
        Assert.Equal(new AgentModelSelection(Model: "IGpt56Luna"), catalog.Select(null));
        Assert.Equal(new AgentModelSelection(Profile: "work"), catalog.Select("profile:work"));
        Assert.Equal(new AgentModelSelection(Model: "IGpt56Sol"), catalog.Select("preset:IGpt56Sol"));
        Assert.Throws<ArgumentException>(() => catalog.Select("https://attacker.example/model"));
        Assert.Throws<ArgumentException>(() => catalog.Select("profile:missing"));
    }

    [Fact]
    public void SelectingAnUnavailableModelExplainsWhatIsMissing()
    {
        using var services = Services(_ => { }, new FixedAiCredentials().Ready("openai", "configured"));

        var unavailable = Assert.Throws<ProviderUnavailableException>(() => Catalog(services).Select("preset:IGemini31Pro"));

        Assert.Equal("google", unavailable.Integration);
        Assert.Equal("Unconfigured", unavailable.Status);
        Assert.Equal(["ApiKey"], unavailable.Missing);
        Assert.DoesNotContain("configured", unavailable.Message.Replace("Unconfigured", ""));
    }

    [Fact]
    public async Task AnUnavailableProviderIsAnswered409WithTheIntegrationStatusAndMissingFields()
    {
        var http = new global::Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        await AgentEndpoints.ProviderUnavailable(new ProviderUnavailableException("google", "Unconfigured", ["ApiKey"])).ExecuteAsync(http);

        Assert.Equal(409, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("google", body.RootElement.GetProperty("integration").GetString());
        Assert.Equal("Unconfigured", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("ApiKey", body.RootElement.GetProperty("missing")[0].GetString());
    }

    [Fact]
    public void AutomaticWithoutServerOverrideKeepsAiDefault()
    {
        using var services = Services(_ => { }, new FixedAiCredentials().Ready("openai", "configured"));
        Assert.Null(Catalog(services).Select(null));
    }

    private static ServiceProvider Services(Action<AIOptions> configure, FixedAiCredentials? credentials = null) => new ServiceCollection()
        .Configure(configure).AddSingleton<IAiCredentials>(credentials ?? new FixedAiCredentials())
        .AddSingleton<ModelProfiles>().BuildServiceProvider();

    private static AgentModelCatalog Catalog(ServiceProvider services, IConfiguration? configuration = null) => new(
        services.GetRequiredService<ModelProfiles>(), services.GetRequiredService<IOptionsMonitor<AIOptions>>(),
        configuration ?? new ConfigurationBuilder().Build());
}
