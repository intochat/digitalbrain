using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class LlmKindTests(BrainClusterFixture<AiKindsConfigurator> fixture) : BrainTest<AiKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Completion_journals_and_returns_text()
    {
        var llm = Neuron("llm", "balanced");
        var receipt = await llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"say hi"}""", "cmd-1", OwnerSession));
        Assert.Contains("fake-reply", receipt.OutputJson);
        var events = await llm.ReadEventsAsync(0, 10);
        Assert.Single(events.Events);
        Assert.Equal("llm.completed", events.Events[0].Kind);
    }

    [Fact]
    public async Task Duplicate_command_does_not_call_model_twice()
    {
        var llm = Neuron("llm", "balanced-replay");
        var first = await llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"one"}""", "cmd-dup", OwnerSession));
        var callsAfterFirst = AiKindsConfigurator.Client.Calls;
        var second = await llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"two"}""", "cmd-dup", OwnerSession));
        Assert.Equal(first, second);
        Assert.Single((await llm.ReadEventsAsync(0, 10)).Events);
        Assert.Equal(callsAfterFirst, AiKindsConfigurator.Client.Calls);
    }

    [Fact]
    public async Task Empty_prompt_fails_closed()
    {
        var llm = Neuron("llm", "guard");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            llm.InvokeAsync(new("llm.complete.v1", """{"prompt":""}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(0, (await llm.ReadAsync("usage")).Revision);
    }

    [Fact]
    public async Task Unknown_tier_address_fails_closed()
    {
        var llm = Neuron("llm", "galaxy");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"x"}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
    }
}
