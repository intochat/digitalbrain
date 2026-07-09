using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Ino;

public static class InoServiceRegistration
{
    public static IServiceCollection AddInoAi(this IServiceCollection services, IConfigurationSection? section = null)
    {
        if (section is not null)
        {
            services.Configure<InoAiOptions>(opt => section.Bind(opt));
        }
        else
        {
            services.AddOptions<InoAiOptions>();
        }

        services.AddSingleton<IInoAiConfig, InoAiConfig>();

        // Wire real (even if basic) services for thin InoNeuron split. Ino delegates where possible.
        services.AddSingleton<IInoRuntime, BasicInoRuntime>();
        services.AddSingleton<IInoToolRegistry, BasicInoToolRegistry>();
        services.AddSingleton<IBrainAwarenessService, BasicBrainAwarenessService>();
        services.AddSingleton<IConnectionStateService, BasicConnectionState>();

        return services;
    }

    private sealed class InoAiConfig(IOptions<InoAiOptions> options) : IInoAiConfig
    {
        public InoAiOptions Current => options.Value;
    }
}

public interface IInoAiConfig
{
    InoAiOptions Current { get; }
}

public interface IInoCapabilityRecall
{
    Task<IReadOnlyList<string>> RecallAsync(string prompt, int top = 5, CancellationToken cancellationToken = default);
}
