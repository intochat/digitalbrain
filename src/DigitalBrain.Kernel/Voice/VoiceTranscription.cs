using System.Net.Http.Headers;
using System.Text.Json;
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

public sealed record DigitalBrainVoiceRuntimeOptions(
    string? Provider,
    string? Model,
    string? Endpoint = null,
    string? ApiKey = null)
{
    public static DigitalBrainVoiceRuntimeOptions Unconfigured { get; } = new(null, null);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Provider) && !string.IsNullOrWhiteSpace(Model);

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);

    public string? TranscriptionEndpoint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                return null;
            }

            var endpoint = Endpoint.Trim();
            return endpoint.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase)
                ? endpoint
                : endpoint.TrimEnd('/') + "/audio/transcriptions";
        }
    }

    public static DigitalBrainVoiceRuntimeOptions FromConfiguration(IConfiguration config)
    {
        var (registryProvider, registryModel) = FindRegisteredVoiceToText(config);
        return new DigitalBrainVoiceRuntimeOptions(
            FirstNonWhiteSpace(registryProvider, config["DigitalBrain:Voice:Provider"]),
            FirstNonWhiteSpace(registryModel, config["DigitalBrain:Voice:Model"]),
            FirstNonWhiteSpace(
                config["DigitalBrain:Voice:Endpoint"],
                config["DigitalBrain:Voice:OpenAIEndpoint"]),
            FirstNonWhiteSpace(
                config["DigitalBrain:Voice:ApiKey"],
                config["OPENAI_API_KEY"]));
    }

    private static (string? Provider, string? Model) FindRegisteredVoiceToText(IConfiguration config)
    {
        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
        var match = DigitalBrainModelRegistrySnapshot.FirstOrDefault(entries, DigitalBrainCapabilityKind.VoiceToText);
        return (match?.Provider, match?.Id);
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
        if (options.IsConfigured &&
            OpenAICompatibleVoiceTranscriber.IsSupportedProvider(options.Provider) &&
            !options.HasEndpoint)
        {
            logger?.LogWarning(
                "Voice-to-text provider {Provider}/{Model} is configured, but DigitalBrain:Voice:Endpoint is not set.",
                options.Provider,
                options.Model);
        }
        else if (options.IsConfigured)
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

public sealed class OpenAICompatibleVoiceTranscriber(
    DigitalBrainVoiceRuntimeOptions options,
    HttpClient httpClient,
    ILogger<OpenAICompatibleVoiceTranscriber>? logger = null) : IVoiceTranscriber
{
    public static bool IsSupportedProvider(string? provider) =>
        string.Equals(provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, DigitalBrainProviderIds.OpenAICompatible, StringComparison.OrdinalIgnoreCase);

    public static bool CanHandle(DigitalBrainVoiceRuntimeOptions options) =>
        options.IsConfigured &&
        options.HasEndpoint &&
        IsSupportedProvider(options.Provider);

    public async Task<VoiceTranscriptionResult> TranscribeAsync(
        VoiceTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = options.TranscriptionEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            logger?.LogWarning(
                "Voice-to-text provider {Provider}/{Model} has no transcription endpoint; returning an empty transcript.",
                options.Provider,
                options.Model);
            return Empty(request);
        }

        try
        {
            using var form = new MultipartFormDataContent();
            using var audio = new ByteArrayContent(request.Audio);
            audio.Headers.ContentType = MediaTypeHeaderValue.Parse(
                string.IsNullOrWhiteSpace(request.MimeType) ? "application/octet-stream" : request.MimeType);

            form.Add(audio, "file", FileNameFor(request.MimeType));
            form.Add(new StringContent(options.Model!), "model");
            form.Add(new StringContent("json"), "response_format");
            if (!string.IsNullOrWhiteSpace(request.LanguageHint))
            {
                form.Add(new StringContent(request.LanguageHint), "language");
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = form
            };
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning(
                    "Voice-to-text provider {Provider}/{Model} returned HTTP {StatusCode}; returning an empty transcript.",
                    options.Provider,
                    options.Model,
                    (int)response.StatusCode);
                return Empty(request);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var transcript = root.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
            var detectedLanguage = root.TryGetProperty("language", out var language) ? language.GetString() : null;

            return new VoiceTranscriptionResult(transcript, detectedLanguage, request.CorrelationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "Voice-to-text provider {Provider}/{Model} failed; returning an empty transcript.",
                options.Provider,
                options.Model);
            return Empty(request);
        }
    }

    private static VoiceTranscriptionResult Empty(VoiceTranscriptionRequest request) =>
        new(string.Empty, CorrelationId: request.CorrelationId);

    private static string FileNameFor(string? mimeType) =>
        mimeType?.ToLowerInvariant() switch
        {
            "audio/mpeg" => "audio.mp3",
            "audio/mp3" => "audio.mp3",
            "audio/ogg" => "audio.ogg",
            "audio/webm" => "audio.webm",
            "audio/x-m4a" => "audio.m4a",
            "audio/mp4" => "audio.m4a",
            _ => "audio.wav"
        };
}

public static class DigitalBrainVoiceTranscription
{
    public static IServiceCollection AddDigitalBrainVoiceTranscription(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddSingleton(DigitalBrainVoiceRuntimeOptions.FromConfiguration(config));
        services.TryAddSingleton<HttpClient>();
        services.TryAddSingleton<NoOpVoiceTranscriber>();
        services.TryAddSingleton<OpenAICompatibleVoiceTranscriber>();
        services.TryAddSingleton<IVoiceTranscriber>(sp =>
        {
            var options = sp.GetRequiredService<DigitalBrainVoiceRuntimeOptions>();
            return OpenAICompatibleVoiceTranscriber.CanHandle(options)
                ? sp.GetRequiredService<OpenAICompatibleVoiceTranscriber>()
                : sp.GetRequiredService<NoOpVoiceTranscriber>();
        });

        // Keyed registration per declared voice-to-text model, symmetric to
        // DigitalBrainChatClientRegistration.AddDigitalBrainChatClients — lets [Voice2Text<TModel>] resolve
        // a specific model's transcriber instead of only ever getting the flat unkeyed default above.
        var entries = DigitalBrainModelRegistrySnapshot.Read(config);
        foreach (var entry in entries)
        {
            if (entry.Kind != DigitalBrainCapabilityKind.VoiceToText || string.IsNullOrWhiteSpace(entry.ServiceKey))
            {
                continue;
            }

            services.AddKeyedSingleton<IVoiceTranscriber>(entry.ServiceKey, (sp, _) =>
                sp.GetRequiredService<IVoiceTranscriber>());
        }

        return services;
    }
}
