using DigitalBrain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.AI;

public static class ModelConfiguration
{
    public const string SectionName = "DigitalBrain:Models";

    public static IServiceCollection AddDigitalBrainModels(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var declared = Read(configuration).ToList();

        return declared.Count == 0
            ? services
            : services.AddDigitalBrainModels(catalog =>
            {
                foreach (var descriptor in declared)
                {
                    catalog.Declare(descriptor);
                }
            });
    }

    public static IEnumerable<ModelDescriptor> Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var bound in configuration.GetSection(SectionName).GetChildren())
        {
            var tier = bound["Tier"];
            var provider = bound["Provider"];
            var modelId = bound["ModelId"];

            if (tier is null || provider is null || modelId is null)
            {
                throw new InvalidOperationException(
                    $"Model binding '{bound.Path}' is incomplete: Tier, Provider and ModelId are all required.");
            }

            yield return new ModelDescriptor(Enum.Parse<ModelTier>(tier, ignoreCase: true), provider, modelId)
            {
                ApiKey = bound["ApiKey"],
                Endpoint = bound["Endpoint"] is { } endpoint ? new Uri(endpoint) : null,
            };
        }
    }
}
