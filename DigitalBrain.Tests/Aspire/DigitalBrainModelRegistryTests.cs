using DigitalBrain.Aspire;

namespace DigitalBrain.Tests.Aspire;

public sealed class DigitalBrainModelRegistryTests
{
    [Fact]
    public void WithLlmRegistersTypedModelAndPreservesLegacyRuntimeSelection()
    {
        var options = new DigitalBrainOptions();

        options.WithLLM<Qwen25Coder1_5B>().AsFast();

        var registration = Assert.Single(options.ModelRegistry.Registrations);
        Assert.Equal(DigitalBrainCapabilityKind.LargeLanguageModel, registration.Model.Kind);
        Assert.Equal(DigitalBrainProviderIds.Ollama, registration.Model.Provider);
        Assert.Equal("qwen2.5-coder:1.5b", registration.Model.Id);
        Assert.Equal(DigitalBrainModelRole.Fast, registration.Role);
        Assert.Equal(DigitalBrainProviderIds.Ollama, options.LlmProvider);
        Assert.Equal("qwen2.5-coder:1.5b", options.LlmModel);
        Assert.Equal(DigitalBrainProviderIds.Ollama, options.ResolvedLlmProvider);
        Assert.Equal("qwen2.5-coder:1.5b", options.ResolvedLlmModel);
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

        options.WithLLM<Gpt4oMini>().AsBalanced();
        options.LlmModel = "chat";

        Assert.Equal(DigitalBrainProviderIds.AzureOpenAI, options.ResolvedLlmProvider);
        Assert.Equal("chat", options.ResolvedLlmModel);
    }

    [Fact]
    public void EmbeddingVoiceAndVectorRegistrationsDoNotChangeChatSelection()
    {
        var options = new DigitalBrainOptions();

        options
            .WithLLM<Qwen25Coder1_5B>().AsBalanced()
            .WithEmbedding<TestEmbeddingModel>()
            .WithVoice2Text<TestVoiceModel>()
            .WithVectorDatabase(DigitalBrainProviderIds.Qdrant, "documents");

        Assert.Equal(DigitalBrainProviderIds.Ollama, options.LlmProvider);
        Assert.Equal("qwen2.5-coder:1.5b", options.LlmModel);

        Assert.Contains(options.ModelRegistry.Registrations, registration =>
            registration.Model.Kind == DigitalBrainCapabilityKind.Embedding &&
            registration.Model.Provider == DigitalBrainProviderIds.OpenAI &&
            registration.Model.Id == "text-embedding-test");

        Assert.Contains(options.ModelRegistry.Registrations, registration =>
            registration.Model.Kind == DigitalBrainCapabilityKind.VoiceToText &&
            registration.Model.Provider == DigitalBrainProviderIds.OpenAI &&
            registration.Model.Id == "whisper-test");

        Assert.Contains(options.ModelRegistry.Registrations, registration =>
            registration.Model.Kind == DigitalBrainCapabilityKind.VectorDatabase &&
            registration.Model.Provider == DigitalBrainProviderIds.Qdrant &&
            registration.Model.Id == "documents");
    }

    [Fact]
    public void AsFastWithoutARegisteredModelFailsClearly()
    {
        var options = new DigitalBrainOptions();

        var ex = Assert.Throws<InvalidOperationException>(() => options.AsFast());

        Assert.Contains("Register a model", ex.Message);
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
