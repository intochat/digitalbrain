using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.Quickstart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()
    .UseInMemoryReminderService()
    .AddDigitalBrain()
    .AddBroadcastHandlers(typeof(Greeter).Assembly)
    .AddDevelopmentJournalStorage());

using var host = builder.Build();

await host.StartAsync();

var grains = host.Services.GetRequiredService<IGrainFactory>();
var brain = DigitalBrainClient.Connect(grains, "quickstart");
var greeter = brain.Get<IGreeter>("first");

await greeter.SayHelloAsync();

var sessionId = new NeuronId(ISessionNeuron.GrainTypeName, brain.Owner, "session");
var session = grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());
var greeterId = new NeuronId(nameof(Greeter), brain.Owner, "first");
var fired = await session.ReadNeuronJournalAsync(greeterId, JournalKind.Outgoing, afterSequence: 0);
var firedCount = fired.ResetSnapshot?.TotalRecorded ?? fired.Delta.Count;
var firedTypes = fired.ResetSnapshot is { } reset
    ? reset.Tallies.Select(tally => tally.SynapseType)
    : fired.Delta.Select(delivery => delivery.Synapse.GetType().Name);

Console.WriteLine($"the greeter durably recorded {firedCount} outgoing synapse(s): {string.Join(", ", firedTypes)}");

await host.StopAsync();
