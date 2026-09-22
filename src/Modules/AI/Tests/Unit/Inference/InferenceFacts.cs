using System.Text.Json;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class InferenceFacts
{
    [Fact]
    public void TypedProviderSettingsCannotBeAppliedToAnotherProvider()
    {
        var model = new ResolvedAgentModel("OpenAI", "model", null, null, "revision", LlmCapabilities.Tools, null, null);
        var options = new InferenceOptions(Provider: new DigitalBrain.AI.OpenAI.OpenAIInferenceOptions(ParallelToolCalls: false));
        Assert.False(InferenceMapping.CreateOptions(model, options).AllowMultipleToolCalls);
        Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(model with { Provider = "Anthropic" }, options));
        var restored = JsonSerializer.Deserialize<InferenceOptions>(JsonSerializer.Serialize(options));
        Assert.IsType<DigitalBrain.AI.OpenAI.OpenAIInferenceOptions>(restored!.Provider);
    }

    [Fact]
    public void ResolvedReasoningCannotBypassProfileAllowedValues()
    {
        var model = new ResolvedAgentModel("OpenAI", "model", null, "profile", "revision", LlmCapabilities.None, "high", null);
        var descriptor = new ModelDescriptor("OpenAI", "model", "profile", "revision", CapabilitySupport.Unsupported,
            CapabilitySupport.Unsupported, CapabilitySupport.Unsupported, AllowedReasoning: ["none", "low"]);
        Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(model, null, descriptor));
    }

    [Fact]
    public void OversizedInlineImagesAreRejectedBeforeDecoding()
    {
        var content = new AiImage("data:image/png;base64," + new string('A', 12 * 1024 * 1024));
        Assert.Throws<ArgumentException>(() => InferenceMapping.ToContent(content));
    }

    [Fact]
    public void RequestsCannotPromoteTheirOwnCapabilities()
    {
        var descriptor = new ModelDescriptor("OpenAI", "model", "profile", "revision", CapabilitySupport.Unsupported,
            CapabilitySupport.Unsupported, CapabilitySupport.Unsupported);
        Assert.Throws<ArgumentException>(() => InferenceMapping.ValidateRequest(new([new("user", [new AiText("hi")])],
            Model: new(Capabilities: LlmCapabilities.Tools)), descriptor));
    }

    [Fact]
    public void StructuredMessagesRoundTripThroughJson()
    {
        var original = new AiMessage("assistant", [new AiText("answer"), new AiReasoning("summary"),
            new AiImage("https://example.test/image.png", "image/png"), new AiAudio("data:audio/wav;base64,AQ=="),
            new AiToolCall("call", "lookup", "{}"), new AiToolResult("call", "{\"ok\":true}")]);
        var restored = JsonSerializer.Deserialize<AiMessage>(JsonSerializer.Serialize(original));
        Assert.NotNull(restored);
        Assert.Equal(original.Content, restored.Content);
    }

    [Fact]
    public void StructuredToolMessagesRoundTripWithoutExecutingFunctions()
    {
        var original = new AiMessage("assistant", [new AiText("checking"), new AiToolCall("call-1", "lookup", "{\"id\":42}")]);
        var sdk = InferenceMapping.ToChatMessage(original);
        var roundTrip = InferenceMapping.FromChatMessage(sdk);
        Assert.Equal("assistant", roundTrip.Role);
        var call = Assert.IsType<AiToolCall>(roundTrip.Content[1]);
        Assert.Equal("call-1", call.CallId);
        Assert.Equal(42, JsonDocument.Parse(call.ArgumentsJson).RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void UnknownContentCannotSilentlyDisappear()
        => Assert.Throws<NotSupportedException>(() => InferenceMapping.FromContent(new ErrorContent("failed")));

    [Fact]
    public void UnknownProviderOptionsAreRejected()
        => Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(
            new("OpenAI", "model", null, null, "revision", LlmCapabilities.None, null, null),
            new(ProviderOptions: new Dictionary<string, string> { ["made-up"] = "true" })));

    [Fact]
    public void ProviderSpecificOptionsValidateTheirOwnerAndValue()
    {
        var model = new ResolvedAgentModel("OpenAI", "model", null, null, "revision", LlmCapabilities.Tools, null, null);
        var settings = new InferenceOptions(ProviderOptions: new Dictionary<string, string> { ["parallel_tool_calls"] = "false" });
        Assert.False(InferenceMapping.CreateOptions(model, settings).AllowMultipleToolCalls);
        Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(model with { Provider = "Anthropic" }, settings));
    }

    [Fact]
    public void ProfileSettingsAndLimitsAreDescribedAndEnforced()
    {
        using var services = new ServiceCollection().AddOptions().Configure<AIOptions>(options =>
        {
            options.OpenAI.ApiKey = "test-only";
            options.ModelProfiles["bounded"] = new()
            {
                Provider = "OpenAI",
                Model = "test-model",
                Capabilities = LlmCapabilities.StructuredOutput,
                ContextWindowTokens = 4000,
                MaximumOutputTokens = 500,
                SupportsTemperature = true,
                SupportsTopP = false,
                AllowedReasoning = ["none", "low"],
            };
        }).AddSingleton<ModelProfiles>().AddSingleton<InferenceService>().BuildServiceProvider();
        var descriptor = services.GetRequiredService<InferenceService>().Describe(new(Profile: "bounded"));
        Assert.Equal(4000, descriptor.ContextWindowTokens);
        Assert.Equal(500, descriptor.MaximumOutputTokens);
        Assert.Equal(CapabilitySupport.Unsupported, descriptor.TopP);
        var model = services.GetRequiredService<ModelProfiles>().Resolve(new(Profile: "bounded"));
        Assert.Equal(0.5f, InferenceMapping.CreateOptions(model, new(Temperature: 0.5f), descriptor).Temperature);
        Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(model, new(TopP: 0.5f), descriptor));
        Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(model, new(MaxOutputTokens: 501), descriptor));
        Assert.Throws<ArgumentException>(() => InferenceMapping.CreateOptions(model, new(Temperature: 0.5f)));
        Assert.NotNull(InferenceMapping.CreateOptions(model, new(ResponseSchemaJson: "{\"type\":\"object\"}"), descriptor).ResponseFormat);
        Assert.Throws<ArgumentException>(() => InferenceMapping.ValidateRequest(new([new("user", [new AiText("hi")])],
            Options: new(Reasoning: "high")), descriptor));
    }

    [Fact]
    public async Task GenericAndTypedNeuronsResolveAndRejectMismatchedProfiles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo => silo.Services.Configure<AIOptions>(options =>
            {
                options.OpenAI.ApiKey = "test-only";
                options.ModelProfiles["custom"] = new() { Provider = "OpenAI", Model = "custom-model" };
            })).StartAsync(ct);
        var generic = brain.Get<ILLM>("generic");
        var description = await generic.Describe(new(Profile: "custom"));
        Assert.Equal("custom-model", description.Model);
        Assert.Equal(CapabilitySupport.Unknown, description.Tools);
        Assert.Null(description.ContextWindowTokens);
        Assert.Equal("custom-model", (await brain.Get<ILLM>("custom").Describe()).Model);
        var typed = brain.Get<DigitalBrain.AI.OpenAI.IGpt56Sol>("typed");
        Assert.Equal("gpt-5.6-sol", (await typed.Describe()).Model);
        await Assert.ThrowsAsync<ArgumentException>(() => typed.Describe(new(Profile: "custom")));
    }

    [Fact]
    public void EveryCompiledMarkerHasItsOwnGrain()
    {
        foreach (var model in LLMModel.All)
        {
            var implementations = typeof(InferenceService).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(LlmNeuronBase).IsAssignableFrom(type) && model.Marker.IsAssignableFrom(type)).ToArray();
            Assert.Single(implementations);
        }
    }
}