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

var brain = new BrainClient(host.Services.GetRequiredService<IGrainFactory>(), new OwnerId("quickstart"));

await brain.FireAsync(nameof(Greeter), "first", new Hello());

var fired = await brain.Session.ReadJournalAsync(JournalKind.Outgoing, afterSequence: 0);
var firedCount = fired.ResetSnapshot?.TotalRecorded ?? fired.Delta.Count;
var firedTypes = fired.ResetSnapshot is { } reset
    ? reset.Tallies.Select(tally => tally.SynapseType)
    : fired.Delta.Select(delivery => delivery.Synapse.GetType().Name);

Console.WriteLine($"the session durably recorded {firedCount} fired synapse(s): {string.Join(", ", firedTypes)}");

await host.StopAsync();
