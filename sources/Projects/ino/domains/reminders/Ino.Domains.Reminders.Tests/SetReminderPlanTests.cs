using Ino.Core;
using Ino.Domains.Reminders.Contracts;
using Ino.Domains.Reminders.Plans;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ino.Domains.Reminders.Tests;

/// <summary>
/// Slice B: <see cref="SetReminderPlan"/> static-body tests. The plan extracts
/// (description, delay) from the prompt and calls
/// <see cref="IRemindersNeuron.SetAsync"/>; tests substitute both the neuron
/// and the chat client so no Orleans activation / IAW substrate is needed.
///
/// Neuron-level integration ("ScheduleJob actually fires
/// OnScheduledJobDueAsync after delay") needs IAW's <c>AgentTest&lt;&gt;</c>
/// harness wired into ino's test infra — tracked as follow-up work in
/// docs/plan-poc-phase-4.md Slice B "Tests" section.
/// </summary>
public sealed class SetReminderPlanTests
{
    static IChatClient NoChat()
    {
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));
        return c;
    }

    [Theory]
    [InlineData("remind me to call mom in 30 minutes", "call mom", 30 * 60)]
    [InlineData("remind me to take out the trash in 2 hours", "take out the trash", 2 * 60 * 60)]
    [InlineData("set a reminder to drink water in 45 min", "drink water", 45 * 60)]
    [InlineData("remind me to stretch in 90 seconds", "stretch", 90)]
    [InlineData("Set a reminder to leave for the airport in 1 hour", "leave for the airport", 60 * 60)]
    public async Task Regex_extracts_description_and_delay(string prompt, string expectedDescription, int expectedSeconds)
    {
        var neuron = Substitute.For<IRemindersNeuron>();
        neuron.SetAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>())
            .Returns(Task.FromResult("ulid-name"));

        var result = await SetReminderPlan.ExecuteAsync(
            prompt: prompt,
            correlationId: "corr-1",
            neuron: neuron,
            chatClient: NoChat(),
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        await neuron.Received(1).SetAsync(
            expectedDescription,
            TimeSpan.FromSeconds(expectedSeconds),
            "corr-1");
    }

    [Fact]
    public async Task Successful_set_message_includes_description_and_humanised_delay()
    {
        var neuron = Substitute.For<IRemindersNeuron>();
        neuron.SetAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>())
            .Returns(Task.FromResult("ulid-name"));

        var result = await SetReminderPlan.ExecuteAsync(
            "remind me to call mom in 30 minutes",
            correlationId: "corr-1",
            neuron: neuron,
            chatClient: NoChat(),
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.Contains("call mom", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30 minutes", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unparseable_prompt_returns_clarification_without_calling_neuron()
    {
        var neuron = Substitute.For<IRemindersNeuron>();
        var chat = NoChat();

        var result = await SetReminderPlan.ExecuteAsync(
            "do something for me",
            correlationId: "corr-1",
            neuron: neuron,
            chatClient: chat,
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("couldn't tell", result.Message, StringComparison.OrdinalIgnoreCase);
        await neuron.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Llm_fallback_invoked_when_regex_misses()
    {
        // Regex only fires on "<desc> in <n> <unit>". A natural-language phrasing
        // like "in five minutes do X" goes to the LLM fallback.
        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                "{\"description\":\"feed the dog\",\"delaySeconds\":300}"))));

        var neuron = Substitute.For<IRemindersNeuron>();
        neuron.SetAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>())
            .Returns(Task.FromResult("ulid-name"));

        var result = await SetReminderPlan.ExecuteAsync(
            "in five minutes can you remind me to feed the dog please",
            correlationId: "corr-1",
            neuron: neuron,
            chatClient: chat,
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        await neuron.Received(1).SetAsync(
            "feed the dog",
            TimeSpan.FromMinutes(5),
            "corr-1");
    }

    [Fact]
    public async Task Llm_returning_null_description_falls_through_to_clarification()
    {
        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                "{\"description\":null,\"delaySeconds\":0}"))));

        var neuron = Substitute.For<IRemindersNeuron>();

        var result = await SetReminderPlan.ExecuteAsync(
            "tell me a joke",
            correlationId: "corr-1",
            neuron: neuron,
            chatClient: chat,
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("couldn't tell", result.Message, StringComparison.OrdinalIgnoreCase);
        await neuron.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>());
    }
}
