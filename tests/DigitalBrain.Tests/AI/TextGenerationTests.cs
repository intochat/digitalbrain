using AI.Contracts;
using Brain.Contracts;
using Brain.KernelTests;
using Xunit;

namespace DigitalBrain.Tests.AI;

[CollectionDefinition("AI text generation", DisableParallelization = true)]
public sealed class AiTextGenerationCollection;

[Collection("AI text generation")]
public sealed class TextGenerationTests(BrainClusterFixture<AiKindsConfigurator> fixture)
    : BrainTest<AiKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Typed_generation_uses_the_requested_model_tier_and_output_bound()
    {
        var neuron = Neuron("llm", "reasoning-text");
        var receipt = await neuron.InvokeAsync(new(
            AiCapabilityIds.TextGenerate,
            """{"instruction":"Answer briefly.","input":"Say hello.","maximumOutputTokens":256}""",
            "ai-text-1",
            OwnerSession));

        Assert.Contains("fake-reply", receipt.OutputJson);
        Assert.Equal("fake-reasoning", AiKindsConfigurator.Client.LastOptions?.ModelId);
        Assert.Equal(256, AiKindsConfigurator.Client.LastOptions?.MaxOutputTokens);
        Assert.Single((await neuron.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Invalid_typed_generation_fails_before_calling_the_model()
    {
        var neuron = Neuron("llm", "balanced-invalid");
        var callsBefore = AiKindsConfigurator.Client.Calls;

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new(
                AiCapabilityIds.TextGenerate,
                """{"instruction":"","input":"Say hello.","maximumOutputTokens":256}""",
                "ai-text-invalid",
                OwnerSession)));

        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(callsBefore, AiKindsConfigurator.Client.Calls);
        Assert.Equal(0, (await neuron.ReadAsync("usage")).Revision);
    }

    [Fact]
    public async Task Duplicate_typed_generation_does_not_call_the_model_twice()
    {
        var neuron = Neuron("llm", "balanced-typed-replay");
        var first = await neuron.InvokeAsync(new(
            AiCapabilityIds.TextGenerate,
            """{"instruction":"Answer.","input":"One.","maximumOutputTokens":128}""",
            "ai-text-replay",
            OwnerSession));
        var callsAfterFirst = AiKindsConfigurator.Client.Calls;

        var second = await neuron.InvokeAsync(new(
            AiCapabilityIds.TextGenerate,
            """{"instruction":"Answer.","input":"Two.","maximumOutputTokens":128}""",
            "ai-text-replay",
            OwnerSession));

        Assert.Equal(first, second);
        Assert.Equal(callsAfterFirst, AiKindsConfigurator.Client.Calls);
        Assert.Single((await neuron.ReadEventsAsync(0, 10)).Events);
    }
}
