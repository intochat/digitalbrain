using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Ino;

/// Registration for Ino as a pluggable AI assistant integration.
/// Call this from the Kernel host (Program.cs) to wire Ino's own AI config
/// and common assistant services. Mirrors pattern used by Google/Salesforce
/// clients.
public static class InoServiceRegistration
{
    public static IServiceCollection AddInoAi(this IServiceCollection services, IConfigurationSection? section = null)
    {
        if (section is not null)
        {
            // Bind Ino-owned AI config. Ino ships its AI assistant settings as an integration.
            services.Configure<InoAiOptions>(opt => section.Bind(opt));
        }
        else
        {
            services.AddOptions<InoAiOptions>();
        }

        services.AddSingleton<IInoAiConfig, InoAiConfig>();

        return services;
    }

    // Simple accessor for resolved options (used by grain or handlers).
    private sealed class InoAiConfig(IOptions<InoAiOptions> options) : IInoAiConfig
    {
        public InoAiOptions Current => options.Value;
    }
}

/// Abstraction so Kernel code can depend on Ino integration without taking concrete options.
public interface IInoAiConfig
{
    InoAiOptions Current { get; }
}

public interface IInoCapabilityRecall
{
    Task<IReadOnlyList<string>> RecallAsync(string prompt, int top = 5, CancellationToken cancellationToken = default);
}