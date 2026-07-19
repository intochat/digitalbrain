using DigitalBrain;
using DigitalBrain.Quickstart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()
    .UseInMemoryReminderService()
    .AddDigitalBrain()
    .AddDevelopmentJournalStorage());

using var host = builder.Build();

await host.StartAsync();

var brain = new BrainClient(host.Services.GetRequiredService<IGrainFactory>(), new OwnerId("quickstart"));

await brain.FireAsync(nameof(Greeter), "first", new Hello());

var fired = await brain.Session.ReadJournalAsync(JournalKind.Outgoing);

Console.WriteLine($"the session durably recorded {fired.Count} fired synapse(s): {string.Join(", ", fired.Select(synapse => synapse.GetType().Name))}");

await host.StopAsync();
