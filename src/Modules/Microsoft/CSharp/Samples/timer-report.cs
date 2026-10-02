#!/usr/bin/env dotnet
#:project ../../../DigitalBrain/Kernel/DigitalBrain.Modules.Kernel.Client/DigitalBrain.Modules.Kernel.Client.csproj
#:project ../../../Time/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj
#:property PublishAot=false

using DigitalBrain.Client;
using DigitalBrain.Time.Timers.Signals;
using ITimer = DigitalBrain.Time.Timers.ITimer;

// Run it as a C# file: the sandbox supplies DigitalBrain:Edge and DigitalBrain:Token and references the
// client project for you, so keep only the contracts #:project line there.
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
