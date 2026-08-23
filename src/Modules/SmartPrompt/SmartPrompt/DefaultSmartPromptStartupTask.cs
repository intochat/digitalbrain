using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

// Seeds the product-demo Smart Prompt once per silo start so Flutter/MCP always have
// at least one runnable automation under fakes.
internal sealed class DefaultSmartPromptStartupTask(
    IGrainFactory grains,
    IConfiguration configuration,
    ILogger<DefaultSmartPromptStartupTask> logger) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ownerValue = configuration[DigitalBrainNames.Owner];
        var owner = new OwnerId(string.IsNullOrWhiteSpace(ownerValue)
            ? DigitalBrainNames.DefaultOwner
            : ownerValue);

        var entity = grains.GetGrain<ISmartPrompt>(
            EntityId.For<ISmartPrompt>(owner, DefaultSmartPromptCatalog.NewCustomersName).ToGrainId());
        var existing = await entity.Read()
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        await entity.Save(DefaultSmartPromptCatalog.NewCustomers)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Seeded default Smart Prompt '{Name}' for owner '{Owner}'.",
            DefaultSmartPromptCatalog.NewCustomersName,
            owner.Value);
    }
}
