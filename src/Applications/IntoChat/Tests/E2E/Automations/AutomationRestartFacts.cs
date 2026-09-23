using DigitalBrain.Automations;
using DigitalBrain.Testing.Unit;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;

namespace IntoChat.Tests.E2E.Automations;

public sealed class AutomationRestartFacts
{
    [Fact(Timeout = 180_000)]
    public async Task ScheduledAutomationSurvivesSiloRestartAndNeverRunsAPaidStepTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        SharedGrainStorage.Reset();
        SharedReminderTable.Reset();
        var invoker = new PaidStepInvoker();

        await using var brain = await UnitTest.Create()
            .WithModule<TimeModule>()
            .WithModule<AutomationsModule>()
            .WithReminders()
            .ConfigureSilo(silo =>
            {
                silo.Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromMilliseconds(100));
                var memoryStorage = silo.Services.LastOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(IGrainStorage) && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, "Default"));
                if (memoryStorage is not null) { silo.Services.Remove(memoryStorage); }
                silo.Services.AddKeyedSingleton<IGrainStorage>("Default", static (_, _) => SharedGrainStorage.Instance);
                silo.Services.AddSingleton<IReminderTable>(SharedReminderTable.Instance);
                silo.Services.AddSingleton<IAutomationActionCatalog>(new FixedAutomationCatalog());
                silo.Services.AddSingleton<IAutomationActionInvoker>(invoker);
                silo.Services.AddSingleton<IAutomationJournal>(new NullAutomationJournal());
            })
            .StartAsync(ct);

        var automation = brain.Get<IAutomation>("nightly");
        await automation.Save(Definition());
        Assert.True((await automation.Activate()).Valid);

        await WaitForRunsAsync(automation, 1, ct);
        await brain.RestartSiloAsync(ct);
        await WaitForRunsAsync(automation, 2, ct);

        var runs = await automation.ReadRuns();
        Assert.True(runs.Count >= 2, $"Expected runs after the restart, saw {runs.Count}.");
        Assert.Equal(runs.Count, runs.Select(run => run.ScheduledFor).Distinct().Count());
        Assert.Equal(invoker.IntentIds.Count, invoker.IntentIds.Distinct().Count());
        Assert.All(runs, run => Assert.Equal(JobOutcome.Succeeded, run.Outcome));
    }

    private static AutomationDefinition Definition() => new()
    {
        Id = "nightly",
        WorkspaceId = "workspace",
        Name = "Nightly lead pull",
        Trigger = new AutomationTrigger { Kind = AutomationTriggerKind.Schedule, IntervalSeconds = 1 },
        Action = new AutomationAction { AppId = "leadgen", Operation = "pull", EstimatedCompute = 5m },
        ComputeBudget = 1000m,
    };

    private static async Task WaitForRunsAsync(IAutomation automation, int minimum, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await automation.ReadRuns()).Count >= minimum) { return; }
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"The automation produced fewer than {minimum} runs within the deadline.");
    }
}
