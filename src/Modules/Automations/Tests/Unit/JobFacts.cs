using System.Collections.Concurrent;
using DigitalBrain.Automations;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class JobFacts
{
    [Fact]
    public async Task SaveRejectsMismatchedIdMissingNameAndInvalidTrigger()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct, new CountingInvoker(), new RecordingJournal());
        var automation = brain.Get<IAutomation>("daily");
        Assert.False((await automation.Save(Definition(id: "other"))).Valid);
        Assert.False((await automation.Save(Definition(name: " "))).Valid);
        Assert.False((await automation.Save(Definition(trigger: new AutomationTrigger { Kind = AutomationTriggerKind.Schedule, IntervalSeconds = 0 }))).Valid);
        Assert.False((await automation.Save(Definition(trigger: new AutomationTrigger { Kind = AutomationTriggerKind.Event }))).Valid);
        Assert.True((await automation.Save(Definition())).Valid);
    }

    [Fact]
    public async Task ActivationRejectsAnUnknownAppOperation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct, new CountingInvoker(), new RecordingJournal(), known: false);
        var automation = brain.Get<IAutomation>("daily");
        await automation.Save(Definition());
        var result = await automation.Activate();
        Assert.False(result.Valid);
        Assert.Contains("Unknown app operation", result.Reason);
        Assert.False((await automation.Read())!.Active);
    }

    [Fact]
    public async Task ARepeatedScheduledTimeRunsTheStepOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var invoker = new CountingInvoker();
        await using var brain = await StartAsync(ct, invoker, new RecordingJournal());
        var automation = brain.Get<IAutomation>("daily");
        await automation.Save(Definition());
        Assert.True((await automation.Activate()).Valid);
        var scheduledFor = new DateTimeOffset(2026, 9, 23, 6, 0, 0, TimeSpan.Zero);

        var first = await automation.RunNow(scheduledFor);
        var again = await automation.RunNow(scheduledFor.AddMilliseconds(500));

        Assert.Equal(JobOutcome.Succeeded, first.Outcome);
        Assert.Equal(first.IntentId, again.IntentId);
        Assert.Single(invoker.IntentIds);
        Assert.Single(await automation.ReadRuns());
    }

    [Fact]
    public async Task AFailedPaidStepIsNeverRetriedAndKeepsTheFirstError()
    {
        var ct = TestContext.Current.CancellationToken;
        var invoker = new CountingInvoker { Fail = true, Error = "Card declined." };
        await using var brain = await StartAsync(ct, invoker, new RecordingJournal());
        var automation = brain.Get<IAutomation>("daily");
        await automation.Save(Definition());
        await automation.Activate();
        var scheduledFor = new DateTimeOffset(2026, 9, 23, 6, 0, 0, TimeSpan.Zero);

        var run = await automation.RunNow(scheduledFor);
        await automation.RunNow(scheduledFor);

        Assert.Equal(JobOutcome.Failed, run.Outcome);
        Assert.Equal("Card declined.", run.FirstError);
        Assert.Single(invoker.IntentIds);
        Assert.Equal(JobOutcome.Failed, Assert.Single(await automation.ReadRuns()).Outcome);
    }

    [Fact]
    public async Task AnExhaustedBudgetSkipsWithoutInvokingTheStep()
    {
        var ct = TestContext.Current.CancellationToken;
        var invoker = new CountingInvoker();
        await using var brain = await StartAsync(ct, invoker, new RecordingJournal());
        var automation = brain.Get<IAutomation>("daily");
        await automation.Save(Definition(budget: 0m, estimated: 5m));
        await automation.Activate();

        var run = await automation.RunNow(new DateTimeOffset(2026, 9, 23, 6, 0, 0, TimeSpan.Zero));

        Assert.Equal(JobOutcome.SkippedBudget, run.Outcome);
        Assert.Empty(invoker.IntentIds);
    }

    [Fact]
    public async Task RunHistoryReachesTheJournal()
    {
        var ct = TestContext.Current.CancellationToken;
        var journal = new RecordingJournal();
        await using var brain = await StartAsync(ct, new CountingInvoker(), journal);
        var automation = brain.Get<IAutomation>("daily");
        await automation.Save(Definition());
        await automation.Activate();
        await automation.RunNow(new DateTimeOffset(2026, 9, 23, 6, 0, 0, TimeSpan.Zero));

        var recorded = Assert.Single(journal.Runs);
        Assert.Equal("daily", recorded.Run.AutomationId);
        Assert.Equal(JobOutcome.Succeeded, recorded.Run.Outcome);
    }

    [Fact]
    public async Task AScheduleFiresThroughTheDurableReminder()
    {
        var ct = TestContext.Current.CancellationToken;
        var invoker = new CountingInvoker();
        await using var brain = await StartAsync(ct, invoker, new RecordingJournal());
        var automation = brain.Get<IAutomation>("daily");
        await using var runs = await brain.Observe<AutomationRunCompleted>(automation, ct);
        await automation.Save(Definition());
        await automation.Activate();

        var completed = await runs.NextAsync(ct: ct);

        Assert.Equal("daily", completed.AutomationId);
        Assert.NotEmpty(invoker.IntentIds);
    }

    [Fact]
    public async Task DeleteClearsTheAutomation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct, new CountingInvoker(), new RecordingJournal());
        var automation = brain.Get<IAutomation>("daily");
        await automation.Save(Definition());
        await automation.Activate();

        await automation.Delete();

        Assert.Null(await automation.Read());
    }

    private static AutomationDefinition Definition(string id = "daily", string name = "Daily leads", AutomationTrigger? trigger = null,
        decimal budget = 100m, decimal estimated = 5m)
        => new()
        {
            Id = id,
            WorkspaceId = "workspace",
            Name = name,
            Trigger = trigger ?? new AutomationTrigger { Kind = AutomationTriggerKind.Schedule, IntervalSeconds = 1 },
            Action = new AutomationAction { AppId = "leadgen", Operation = "pull", EstimatedCompute = estimated },
            ComputeBudget = budget,
        };

    private static Task<UnitBrain> StartAsync(CancellationToken ct, CountingInvoker invoker, RecordingJournal journal, bool known = true)
        => UnitTest.Create()
            .WithModule<TimeModule>()
            .WithModule<AutomationsModule>()
            .WithReminders()
            .ConfigureSilo(silo =>
            {
                silo.Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromMilliseconds(100));
                silo.Services.AddSingleton<IAutomationActionCatalog>(new FakeCatalog(known));
                silo.Services.AddSingleton<IAutomationActionInvoker>(invoker);
                silo.Services.AddSingleton<IAutomationJournal>(journal);
            })
            .StartAsync(ct);

    private sealed class FakeCatalog(bool known) : IAutomationActionCatalog
    {
        public Task<AutomationOperation?> FindAsync(string appId, string operation, CancellationToken cancellationToken = default)
            => Task.FromResult(known
                ? new AutomationOperation { AppId = appId, Operation = operation, ReadOnly = false, EstimatedCompute = 5m }
                : null);
    }

    private sealed class CountingInvoker : IAutomationActionInvoker
    {
        private readonly ConcurrentBag<string> intentIds = [];
        public IReadOnlyCollection<string> IntentIds => intentIds;
        public bool Fail { get; init; }
        public string Error { get; init; } = "The action failed.";

        public Task<AutomationActionResult> InvokeAsync(AutomationDefinition definition, AutomationAction action, string intentId, CancellationToken cancellationToken = default)
        {
            intentIds.Add(intentId);
            return Task.FromResult(new AutomationActionResult
            {
                Succeeded = !Fail,
                Error = Fail ? Error : null,
                ActualCompute = Fail ? 0m : action.EstimatedCompute,
            });
        }
    }

    private sealed class RecordingJournal : IAutomationJournal
    {
        private readonly ConcurrentQueue<(AutomationDefinition Definition, JobRun Run)> runs = new();
        public IReadOnlyCollection<(AutomationDefinition Definition, JobRun Run)> Runs => runs;

        public Task RecordRunAsync(AutomationDefinition definition, JobRun run, CancellationToken cancellationToken = default)
        {
            runs.Enqueue((definition, run));
            return Task.CompletedTask;
        }
    }
}
