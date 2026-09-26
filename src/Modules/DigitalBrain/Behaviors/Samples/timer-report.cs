#!/usr/bin/env dotnet
#:project ../Runtime/DigitalBrain.Behavior.csproj
#:project ../../../Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
#:property PublishAot=false

using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Time.Timers.Signals;
using Microsoft.Extensions.Configuration;
using Timer = DigitalBrain.Time.Timers.ITimer;

// Standalone development: dotnet run --file timer-report.cs -- --LocalDevelopment=true
// Managed drafts omit the SDK directives: the host supplies approved references.
var settings = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
var timerId = settings["TimerId"] ?? "tea";
await BehaviorApp.RunAsync<TimerReportBehavior>(args,
    brain => [SubscriptionRequirement.For<TimerTick>(brain.Get<Timer>(timerId))]);

public sealed class TimerReportBehavior(IDigitalBrain brain, IConfiguration configuration) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var timer = brain.Get<Timer>(configuration["TimerId"] ?? "tea");
        var ticks = await brain.SubscribeAsync<TimerTick>(timer, cancellation);
        Console.WriteLine("TimerReport ready");
        await foreach (var tick in ticks.ReadAllAsync(cancellation))
        {
            Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
            if (configuration.GetValue<bool>("Smoke")) { return; }
        }
    }
}
