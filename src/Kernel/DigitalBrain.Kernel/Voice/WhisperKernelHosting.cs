using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Kernel;

internal static class WhisperKernelHosting
{
    public static IHostApplicationBuilder AddWhisperIfConfigured(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var modelId = builder.Configuration[FoundryLocalTranscriptionService.ModelIdConfigurationKey];
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return builder;
        }

        builder.Services.RemoveAll<IAudioTranscriptionService>();
        builder.Services.TryAddSingleton<IAudioConverter, OggOpusToWavConverter>();
        builder.Services.TryAddSingleton<FoundryLocalTranscriptionService>();
        builder.Services.TryAddSingleton<IAudioTranscriptionService>(static sp =>
            sp.GetRequiredService<FoundryLocalTranscriptionService>());
        builder.Services.AddHostedService(static sp => sp.GetRequiredService<FoundryLocalTranscriptionService>());
        return builder;
    }
}
