using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Marketplace.Creators;

[ModuleConfiguration(typeof(CreatorPublishingConfigurationContract))]
public sealed class CreatorPublishingModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(CreatorPublishingModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(new CreatorPublishingOptions
        {
            SelfServeSignUp = ReadFlag(silo, nameof(CreatorPublishingOptions.SelfServeSignUp)),
            PrepaidCompute = ReadFlag(silo, nameof(CreatorPublishingOptions.PrepaidCompute)),
            ConsumerTerms = ReadFlag(silo, nameof(CreatorPublishingOptions.ConsumerTerms)),
        });
        services.TryAddSingleton<ICreatorScenarioCertifier, DeterministicFakeScenarioCertifier>();
        services.TryAddSingleton<IMarketplaceKillSwitch, InMemoryMarketplaceKillSwitch>();
    }

    private static bool ReadFlag(ISiloBuilder silo, string member) =>
        bool.TryParse(silo.Configuration[$"{CreatorPublishingOptions.SectionKey}:{member}"], out var value) && value;
}
