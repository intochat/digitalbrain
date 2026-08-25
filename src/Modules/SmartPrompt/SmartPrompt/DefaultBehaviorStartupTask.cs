using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

internal sealed class DefaultBehaviorStartupTask(
    IGrainFactory grains,
    IConfiguration configuration,
    ILogger<DefaultBehaviorStartupTask> logger) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var configured = configuration[DigitalBrainNames.Owner];
        var owner = new OwnerId(string.IsNullOrWhiteSpace(configured) ? DigitalBrainNames.DefaultOwner : configured);
        var catalog = grains.GetGrain<IBehaviorCatalog>(
            EntityId.For<IBehaviorCatalog>(owner, "catalog").ToGrainId());
        foreach (var example in BehaviorExamples.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await catalog.Add(example.Name);
            var definition = grains.GetGrain<IBehaviorDefinition>(
                EntityId.For<IBehaviorDefinition>(owner, example.Name).ToGrainId());
            var existing = await definition.Read();
            if (existing is null)
            {
                await definition.Save(example.Source);
                var test = await definition.Test();
                if (test.AllGreen)
                {
                    await definition.Activate();
                }
                logger.LogInformation("Seeded behavior {Behavior} ({Tests} tests).", example.Name, test.Scenarios);
            }
        }
    }
}
