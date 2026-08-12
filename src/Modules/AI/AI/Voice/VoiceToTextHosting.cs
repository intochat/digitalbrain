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

        // Seam 6 #3: fail closed with honest STT-off copy — never register a second STT stack.
        var enabled = configuration.GetValue(EnabledConfigurationKey, defaultValue: true);
        if (!enabled)
        {
            services.TryAddSingleton<IAudioTranscriptionService>(_ =>
                new UnavailableTranscriptionService(UnavailableTranscriptionService.DisabledMessage));
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration[ModelIdConfigurationKey]))
        {
            // Grill / AppHost EnableVoiceToText=false skips WithVoiceToText → no ModelId projected.
            services.TryAddSingleton<IAudioTranscriptionService>(_ =>
                new UnavailableTranscriptionService(UnavailableTranscriptionService.NotConfiguredMessage));
            return;
        }

        services.TryAddSingleton<FoundryLocalTranscriptionService>();
        services.TryAddSingleton<IAudioTranscriptionService>(static sp =>
            sp.GetRequiredService<FoundryLocalTranscriptionService>());
        services.AddHostedService(static sp => sp.GetRequiredService<FoundryLocalTranscriptionService>());
    }
}
