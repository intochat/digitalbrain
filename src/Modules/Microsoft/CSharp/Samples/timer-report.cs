#!/usr/bin/env dotnet
#:project ../../../DigitalBrain/Kernel/Client/DigitalBrain.Client.csproj
#:project ../../../Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
#:property PublishAot=false

using DigitalBrain.Client;
using DigitalBrain.Time.Timers.Signals;
using ITimer = DigitalBrain.Time.Timers.ITimer;

// Host run: dotnet run --file timer-report.cs -- --LocalDevelopment=true --CSharpFile:Settings:TimerId=tea
// Inside a CSharpFile container the client project is referenced for you; keep only the contracts #:project line.
await using var brain = await DigitalBrainClient.ConnectAsync(args);
var timer = brain.Get<ITimer>(brain.Setting("TimerId") ?? "tea");
var stopAfterFirstTick = brain.Setting("Smoke") == "true";
await using var ticks = await brain.SubscribeAsync<TimerTick>(timer, brain.Stopping);
Console.WriteLine("TimerReport ready");
await foreach (var tick in ticks.ReadAllAsync(brain.Stopping))
{
    Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
    if (stopAfterFirstTick) { break; }
}
