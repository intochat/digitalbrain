using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.AI.PersonaPlex;

public static class PersonaPlexHosting
{
    public static IServiceCollection AddPersonaPlex(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PersonaPlexOptions>()
            .Bind(configuration.GetSection(PersonaPlexOptions.SectionName));
        services.TryAddSingleton<PersonaPlexSessionFactory>();
        services.TryAddSingleton<IPersonaPlexSessionFactory>(static provider =>
            provider.GetRequiredService<PersonaPlexSessionFactory>());
        services.AddSingleton<IHostedService>(static provider =>
            provider.GetRequiredService<PersonaPlexSessionFactory>());

        return services;
    }
}
