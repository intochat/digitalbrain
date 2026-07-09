using DigitalBrain.Aspire;
using DigitalBrain.Core.Models;
using AzureOpenAIModels = DigitalBrain.Core.Models.AzureOpenAI;
using OllamaModels = DigitalBrain.Core.Models.Ollama;
using VoiceModels = DigitalBrain.Core.Models.Voice;

namespace DigitalBrain.Tests.Aspire;

public sealed class DigitalBrainModelRegistryTests
{
    [Fact]
    public void WithLlmRegistersTypedModelAndPreservesLegacyRuntimeSelection()
    {
        var options = new DigitalBrainOptions();

        options.WithLLM<OllamaModels.Llama31_8B>().AsFast();

        var registration = Assert.Single(options.ModelRegistry.Registrations);
        Assert.Equal(DigitalBrainCapabilityKind.LargeLanguageModel, registration.Model.Kind);
        Assert.Equal(DigitalBrainProviderIds.Ollama, registration.Model.Provider);
        Assert.Equal("llama3.1:8b", registration.Model.Id);
        Assert.Equal(DigitalBrainModelRole.Fast, registration.Role);
        Assert.Equal(DigitalBrainProviderIds.Ollama, options.LlmProvider);
        Assert.Equal("llama3.1:8b", options.LlmModel);
        Assert.Equal(DigitalBrainProviderIds.Ollama, options.ResolvedLlmProvider);
        Assert.Equal("llama3.1:8b", options.ResolvedLlmModel);
    }

    [Fact]
    public void RegistryKeepsSeparateFastBalancedAndReasoningLlmRoles()
    {
        var options = new DigitalBrainOptions();

        options
            .WithLLM<FastTestModel>().AsFast()
            .WithLLM<BalancedTestModel>().AsBalanced()
            .WithLLM<ReasoningTestModel>().AsReasoning();

        Assert.Collection(
            options.ModelRegistry.Registrations,
            fast => Assert.Equal(DigitalBrainModelRole.Fast, fast.Role),
            balanced => Assert.Equal(DigitalBrainModelRole.Balanced, balanced.Role),
            reasoning => Assert.Equal(DigitalBrainModelRole.Reasoning, reasoning.Role));

        Assert.Equal("balanced-test", options.ModelRegistry.DefaultLlm?.Model.Id);
        Assert.Equal(DigitalBrainProviderIds.OpenAI, options.ResolvedLlmProvider);
        Assert.Equal("balanced-test", options.ResolvedLlmModel);
    }

    [Fact]
    public void ManualLlmOverrideWinsOverRegistryDefault()
    {
        var options = new DigitalBrainOptions();

        options.WithLLM<AzureOpenAIModels.Gpt4oMini>().AsBalanced();
        options.LlmModel = "chat";

        Assert.Equal(DigitalBrainProviderIds.AzureOpenAI, options.ResolvedLlmProvider);
        Assert.Equal("chat", options.ResolvedLlmModel);
    }

    [Fact]
    public void EmbeddingVoiceAndVectorRegistrationsDoNotChangeChatSelection()
    {
        var options = new DigitalBrainOptions();

        options
            .WithLLM<OllamaModels.Llama31_8B>().AsBalanced()
            .WithEmbedding<TestEmbeddingModel>()
            .WithVoice2Text<TestVoiceModel>()
            .WithVectorDatabase(DigitalBrainProviderIds.Qdrant, "documents");

        Assert.Equal(DigitalBrainProviderIds.Ollama, options.LlmProvider);
        Assert.Equal("llama3.1:8b", options.LlmModel);

        Assert.Contains(options.ModelRegistry.Registrations, registration =>
            registration.Model.Kind == DigitalBrainCapabilityKind.Embedding &&
            registration.Model.Provider == DigitalBrainProviderIds.OpenAI &&
            registration.Model.Id == "text-embedding-test");

        Assert.Contains(options.ModelRegistry.Registrations, registration =>
            registration.Model.Kind == DigitalBrainCapabilityKind.VoiceToText &&
            registration.Model.Provider == DigitalBrainProviderIds.OpenAI &&
            registration.Model.Id == "whisper-test");
        Assert.Equal("whisper-test", options.ModelRegistry.DefaultVoiceToText?.Model.Id);

        Assert.Contains(options.ModelRegistry.Registrations, registration =>
            registration.Model.Kind == DigitalBrainCapabilityKind.VectorDatabase &&
            registration.Model.Provider == DigitalBrainProviderIds.Qdrant &&
            registration.Model.Id == "documents");
    }

    [Fact]
    public void LocalWhisperVoiceModelUsesOpenAICompatibleProvider()
    {
        var options = new DigitalBrainOptions();

        options.WithVoice2Text<VoiceModels.Whisper1Local>();

        var voice = Assert.Single(options.ModelRegistry.Registrations);
        Assert.Equal(DigitalBrainCapabilityKind.VoiceToText, voice.Model.Kind);
        Assert.Equal(DigitalBrainProviderIds.OpenAICompatible, voice.Model.Provider);
        Assert.Equal("whisper-1", voice.Model.Id);
        Assert.Equal("Local Whisper", voice.Model.DisplayName);
    }

    [Fact]
    public void AsFastWithoutARegisteredModelFailsClearly()
    {
        var options = new DigitalBrainOptions();

        var ex = Assert.Throws<InvalidOperationException>(() => options.AsFast());

        Assert.Contains("Register a model", ex.Message);
    }

    [Fact]
    public void RegistrationsCarryServiceKeyAndCapabilitiesReadyForEnvExport()
    {
        var options = new DigitalBrainOptions();

        options.WithLLM<ChatOnlyTestModel>().AsBalanced();

        var registration = Assert.Single(options.ModelRegistry.Registrations);
        Assert.Equal("test-provider-chat-only-test", registration.Model.ServiceKey);
        Assert.False(registration.Model.Capabilities.SupportsTools);
    }

    [Fact]
    public void ProductionLlmModelDescriptorsAreToolCapable()
    {
        var assemblies = new[] { typeof(LlmModel).Assembly, typeof(DigitalBrainOptions).Assembly }.Distinct();
        var productionModels = assemblies
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type =>
                typeof(LlmModel).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                !type.IsNested &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(static type => (LlmModel)Activator.CreateInstance(type)!)
            .ToArray();

        Assert.DoesNotContain(productionModels, model => !model.Capabilities.SupportsTools);
    }

    private sealed class FastTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.OpenAI;
        public override string Id => "fast-test";
    }

    private sealed class BalancedTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.OpenAI;
        public override string Id => "balanced-test";
    }

    private sealed class ReasoningTestModel : LlmModel
    {
        public override string Provider => DigitalBrainProviderIds.OpenAI;
        public override string Id => "reasoning-test";
    }

    private sealed class ChatOnlyTestModel : LlmModel
    {
        public override string Provider => "test-provider";
        public override string Id => "chat-only-test";
        public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ChatOnly;
    }

    private sealed class TestEmbeddingModel : EmbeddingModel
    {
        public override string Provider => DigitalBrainProviderIds.OpenAI;
        public override string Id => "text-embedding-test";
    }

    private sealed class TestVoiceModel : VoiceToTextModel
    {
        public override string Provider => DigitalBrainProviderIds.OpenAI;
        public override string Id => "whisper-test";
    }
}
