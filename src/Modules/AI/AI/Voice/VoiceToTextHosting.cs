using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

internal static class VoiceToTextHosting
{
    public const string ModelIdConfigurationKey = "DigitalBrain:AI:Whisper:ModelId";
    public const string EnabledConfigurationKey = "DigitalBrain:AI:Whisper:Enabled";

    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IAudioConverter, OggOpusToWavConverter>();

        var enabled = configuration.GetValue(EnabledConfigurationKey, defaultValue: true);
        if (!enabled || string.IsNullOrWhiteSpace(configuration[ModelIdConfigurationKey]))
        {
            services.TryAddSingleton<IAudioTranscriptionService, UnavailableTranscriptionService>();
            return;
        }

        services.TryAddSingleton<FoundryLocalTranscriptionService>();
        services.TryAddSingleton<IAudioTranscriptionService>(static sp =>
            sp.GetRequiredService<FoundryLocalTranscriptionService>());
        services.AddHostedService(static sp => sp.GetRequiredService<FoundryLocalTranscriptionService>());
    }
}
