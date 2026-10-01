using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Platform.Secrets;

public static class MasterKeyServiceCollectionExtensions
{
    public static IServiceCollection AddMasterKeyWrapper(this IServiceCollection services)
    {
        services.AddOptions<MasterKeyOptions>().BindConfiguration(MasterKeyOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.MasterKey),
                $"Configure {DigitalBrainNames.MasterKeyConfigurationKey} via the {DigitalBrainNames.MasterKeyEnvironmentVariable} environment secret before starting the host.")
            .ValidateOnStart();
        services.AddSingleton<IKeyWrapper, MasterKeyWrapper>();
        return services;
    }
}
