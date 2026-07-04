using DigitalBrain.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Kernel.Voice;

public sealed record VoiceTranscriptionRequest(
    byte[] Audio,
    string MimeType,
    string? LanguageHint,
    string CorrelationId);

public sealed record VoiceTranscriptionResult(
    string Transcript,
    string? DetectedLanguage = null,
    string? CorrelationId = null);

public interface IVoiceTranscriber
{
    Task<VoiceTranscriptionResult> TranscribeAsync(
        VoiceTranscriptionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DigitalBrainVoiceRuntimeOptions(string? Provider, string? Model)
{
    public static DigitalBrainVoiceRuntimeOptions Unconfigured { get; } = new(null, null);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Provider) && !string.IsNullOrWhiteSpace(Model);

    public static DigitalBrainVoiceRuntimeOptions FromConfiguration(IConfiguration config)
    {
        var (registryProvider, registryModel) = FindRegisteredVoiceToText(config);
        return new DigitalBrainVoiceRuntimeOptions(
            FirstNonWhiteSpace(registryProvider, config["DigitalBrain:Voice:Provider"]),
            FirstNonWhiteSpace(registryModel, config["DigitalBrain:Voice:Model"]));
    }

    private static (string? Provider, string? Model) FindRegisteredVoiceToText(IConfiguration config)
    {
        foreach (var child in config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())
        {
            var kind = child["Kind"];
            if (!string.Equals(kind, DigitalBrainCapabilityKind.VoiceToText.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (child["Provider"], child["Id"]);
        }

        return (null, null);
    }

    private static string? FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}

public sealed class NoOpVoiceTranscriber(
    DigitalBrainVoiceRuntimeOptions? options = null,
    ILogger<NoOpVoiceTranscriber>? logger = null) : IVoiceTranscriber
{
    private readonly DigitalBrainVoiceRuntimeOptions options = options ?? DigitalBrainVoiceRuntimeOptions.Unconfigured;

    public Task<VoiceTranscriptionResult> TranscribeAsync(
        VoiceTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (options.IsConfigured)
        {
            logger?.LogWarning(
                "Voice-to-text provider {Provider}/{Model} is configured, but no runtime adapter is registered.",
                options.Provider,
                options.Model);
        }
        else
        {
            logger?.LogInformation("Voice-to-text is not configured; returning an empty transcript.");
        }

        return Task.FromResult(new VoiceTranscriptionResult(string.Empty, CorrelationId: request.CorrelationId));
    }
}

public static class DigitalBrainVoiceTranscription
{
    public static IServiceCollection AddDigitalBrainVoiceTranscription(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddSingleton(DigitalBrainVoiceRuntimeOptions.FromConfiguration(config));
        services.TryAddSingleton<IVoiceTranscriber, NoOpVoiceTranscriber>();
        return services;
    }
}
