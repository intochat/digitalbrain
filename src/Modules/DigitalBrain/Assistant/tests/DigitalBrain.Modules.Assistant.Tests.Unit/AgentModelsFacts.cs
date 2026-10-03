using DigitalBrain.Assistant;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AgentModelsFacts
{
    [Fact]
    public async Task CompletedTurnReplaysWithoutTheRemovedProviderOrProfile()
    {
        await using var brain = await StartAsync(_ => { });
        var completed = new AgentConversationTurn("run", "hello", "persisted answer", []);
        var snapshot = new AgentConversationState(2, null, [completed]);
        var input = new AssistantRun("thread", "run", "hello", "owner", "profile:removed");

        var prepared = await AssistantTurnExecution.PrepareTurn(snapshot, input, id => Task.FromResult(Catalog(brain.SiloServices).Select(id)));

        Assert.Equal(completed, prepared.Replay);
        Assert.Null(prepared.Model);
        await Assert.ThrowsAsync<ArgumentException>(() => AssistantTurnExecution.PrepareTurn(snapshot, input with { RunId = "fresh" }, id => Task.FromResult(Catalog(brain.SiloServices).Select(id))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AssistantTurnExecution.PrepareTurn(snapshot,
            input with { Message = "different message" }, id => Task.FromResult(Catalog(brain.SiloServices).Select(id))));
        Assert.Null(snapshot.ActiveRunId);
        Assert.Single(snapshot.Turns);
    }

    [Fact]
    public async Task CatalogContainsOnlyConfiguredToolModelsAndNeverSecretsOrEndpoints()
    {
        await using var brain = await StartAsync(options =>
        {
            options.ModelProfiles["work"] = new() { Provider = "OpenAI", Model = "private-chat", Capabilities = LlmCapabilities.Tools };
            options.ModelProfiles["no-tools"] = new() { Provider = "OpenAI", Model = "text-only" };
            options.ModelProfiles["unconfigured"] = new() { Provider = "Google", Model = "remote-chat", Capabilities = LlmCapabilities.Tools };
        }, "secret-api-key", "https://private.example/v1");
        var catalog = Catalog(brain.SiloServices);
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
    public async Task UnconfiguredDeploymentHasUnavailableAutomaticAndNoChoices()
    {
        await using var brain = await StartAsync(_ => { });
        var catalog = Catalog(brain.SiloServices);
        Assert.False(catalog.Read().Automatic.Available);
        Assert.Empty(catalog.Read().Models);
        Assert.Throws<ArgumentException>(() => catalog.Select(null));
    }

    [Fact]
    public async Task SelectionUsesCatalogIdsAndPreservesAutomaticServerOverride()
    {
        await using var brain = await StartAsync(options =>
        {
            options.ModelProfiles["work"] = new() { Provider = "OpenAI", Model = "private-chat", Capabilities = LlmCapabilities.Tools };
        }, "configured", model: "IGpt56Luna");
        var catalog = Catalog(brain.SiloServices);
        Assert.Equal(new AgentModelSelection(Model: "IGpt56Luna"), catalog.Select(null));
        Assert.Equal(new AgentModelSelection(Profile: "work"), catalog.Select("profile:work"));
        Assert.Equal(new AgentModelSelection(Model: "IGpt56Sol"), catalog.Select("preset:IGpt56Sol"));
        Assert.Throws<ArgumentException>(() => catalog.Select("https://attacker.example/model"));
        Assert.Throws<ArgumentException>(() => catalog.Select("profile:missing"));
    }

    [Fact]
    public async Task SelectingAnUnavailableModelExplainsWhatIsMissing()
    {
        await using var brain = await StartAsync(_ => { }, "configured");

        var unavailable = Assert.Throws<ProviderUnavailableException>(() => Catalog(brain.SiloServices).Select("preset:IGemini31Pro"));

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
    public async Task AutomaticWithoutServerOverrideKeepsAiDefault()
    {
        await using var brain = await StartAsync(_ => { }, "configured");
        Assert.Null(Catalog(brain.SiloServices).Select(null));
    }

    private static Task<UnitBrain> StartAsync(Action<AIOptions> configure, string? apiKey = null, string? endpoint = null, string? model = null)
        => UnitTest.Create().WithModule<AIModule>().WithModule<AssistantModule>()
            .WithExecution(new TestExecutionOptions
            {
                PrivateConfiguration = apiKey is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>
                {
                    ["Assistant:Model"] = model,
                    ["DigitalBrain:Integrations:openai:ApiKey"] = apiKey,
                    ["DigitalBrain:Integrations:openai:Endpoint"] = endpoint ?? "https://api.openai.com/v1",
                },
            })
            .ConfigureSilo(silo => silo.Services.Configure(configure))
            .StartAsync(TestContext.Current.CancellationToken);

    private static AgentModelCatalog Catalog(IServiceProvider services) => new(
        services.GetRequiredService<ModelProfiles>(), services.GetRequiredService<IOptionsMonitor<AIOptions>>(),
        services.GetRequiredService<IOptions<AssistantOptions>>().Value.Model);
}
