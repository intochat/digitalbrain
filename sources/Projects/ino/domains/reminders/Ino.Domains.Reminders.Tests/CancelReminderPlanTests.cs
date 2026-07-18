using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Reminders.Contracts;
using Ino.Domains.Reminders.Plans;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ino.Domains.Reminders.Tests;

/// <summary>
/// Slice B: <see cref="CancelReminderPlan"/> static-body tests. The plan walks
/// the user's reminder journal for live <see cref="ReminderSet"/> entries and
/// matches one against the prompt by keyword overlap; tests drive the journal
/// via a substituted <see cref="ITraversalEngine"/>.
/// </summary>
public sealed class CancelReminderPlanTests
{
    static EventEnvelope<ReminderEvent> Envelope(ReminderEvent payload, DateTimeOffset timestamp) =>
        new(
            Payload: payload,
            EventId: Ulid.NewUlid().ToString(),
            CausedByEventId: null,
            CausedByStream: "<test>",
            CorrelationId: "corr-x",
            Timestamp: timestamp,
            TraceParent: null);

    static ITraversalEngine EngineWithJournal(IReadOnlyList<EventEnvelope<ReminderEvent>> journal)
    {
        var engine = Substitute.For<ITraversalEngine>();
        engine.VisitAsync<ReminderEvent>(
                Arg.Any<string>(),
                Arg.Any<RecallQuery<ReminderEvent>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(journal));
        return engine;
    }

    [Fact]
    public async Task Cancels_most_recent_matching_live_reminder()
    {
        var t0 = DateTimeOffset.UtcNow;
        var journal = new[]
        {
            Envelope(new ReminderSet("name-old", "call mom", t0.AddMinutes(15)), t0),
            Envelope(new ReminderSet("name-trash", "take out the trash", t0.AddMinutes(120)), t0.AddMinutes(1)),
            Envelope(new ReminderSet("name-water", "drink water", t0.AddMinutes(45)), t0.AddMinutes(2)),
        };
        var engine = EngineWithJournal(journal);
        var neuron = Substitute.For<IRemindersNeuron>();
        neuron.CancelAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var result = await CancelReminderPlan.ExecuteAsync(
            prompt: "cancel the trash reminder",
            correlationId: "corr-1",
            userKey: "user-1",
            engine: engine,
            neuron: neuron,
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("trash", result.Message, StringComparison.OrdinalIgnoreCase);
        await neuron.Received(1).CancelAsync("name-trash", "corr-1");
    }

    [Fact]
    public async Task Skips_already_cancelled_or_fired_reminders()
    {
        var t0 = DateTimeOffset.UtcNow;
        var journal = new EventEnvelope<ReminderEvent>[]
        {
            Envelope(new ReminderSet("name-trash", "take out the trash", t0.AddMinutes(15)), t0),
            // The trash reminder was already cancelled — even though its
            // ReminderSet is still in the journal, ResolveLiveSets removes it.
            Envelope(new ReminderCancelled("name-trash"), t0.AddMinutes(1)),
            Envelope(new ReminderSet("name-old-trash", "throw out the trash", t0.AddMinutes(30)), t0.AddMinutes(2)),
            Envelope(new ReminderDue("name-old-trash", "throw out the trash", t0.AddMinutes(30)), t0.AddMinutes(31)),
        };
        var engine = EngineWithJournal(journal);
        var neuron = Substitute.For<IRemindersNeuron>();

        var result = await CancelReminderPlan.ExecuteAsync(
            prompt: "cancel the trash reminder",
            correlationId: "corr-1",
            userKey: "user-1",
            engine: engine,
            neuron: neuron,
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("any active reminders", result.Message, StringComparison.OrdinalIgnoreCase);
        await neuron.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Returns_no_match_message_when_no_live_set_overlaps_prompt()
    {
        var t0 = DateTimeOffset.UtcNow;
        var journal = new[]
        {
            Envelope(new ReminderSet("name-water", "drink water", t0.AddMinutes(45)), t0),
        };
        var engine = EngineWithJournal(journal);
        var neuron = Substitute.For<IRemindersNeuron>();

        var result = await CancelReminderPlan.ExecuteAsync(
            prompt: "cancel my dentist appointment",
            correlationId: "corr-1",
            userKey: "user-1",
            engine: engine,
            neuron: neuron,
            log: NullLogger.Instance,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("couldn't find", result.Message, StringComparison.OrdinalIgnoreCase);
        await neuron.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
