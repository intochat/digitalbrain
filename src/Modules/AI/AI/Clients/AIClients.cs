using DigitalBrain.AI.Interactions;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace DigitalBrain.AI;

internal static class AIClients
{
    internal const string ConfigurationRoot = "DigitalBrain:AI";
    internal const string DefaultModelKey = $"{ConfigurationRoot}:Default:Model";
    internal const string DefaultEmbeddingKey = $"{ConfigurationRoot}:Default:Embedding";
    internal const string DefaultTranscriptionKey = $"{ConfigurationRoot}:Default:Transcription";
    internal const string DefaultImageKey = $"{ConfigurationRoot}:Default:Image";
    internal const string SensitiveTelemetryKey = $"{ConfigurationRoot}:Telemetry:EnableSensitiveData";
    private const string TelemetrySource = "DigitalBrain.AI";

    private static readonly IReadOnlyDictionary<AiProvider, ILlmProviderFactory> Factories =
        new ILlmProviderFactory[]
        {
            new OpenAIProviderFactory(),
            new AnthropicProviderFactory(),
            new GoogleProviderFactory(),
            new XAIProviderFactory(),
            new OllamaProviderFactory(),
        }.ToDictionary(static factory => factory.Provider);

    internal static void Add(IServiceCollection services)
    {
        services.TryAddSingleton<IUntrustedContentScreen, UntrustedContentScreen>();
        services.TryAddSingleton<IIntentUsageSink>(provider => new GrainIntentUsageSink(
            provider.GetRequiredService<IGrainFactory>(), provider.GetService<IMeterSink>()));
        foreach (var model in LLMModel.All)
        {
            services.AddKeyedSingleton<IChatClient>(
                model.Marker,
                (provider, _) => BuildChatPipeline(provider, model));
        }

        foreach (var model in EmbeddingModel.All)
        {
            services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                model.Marker,
                (provider, _) =>
                {
                    var inner = Factories[model.Provider].CreateEmbeddingGenerator(
                        model,
                        provider.GetRequiredService<IOptions<AIOptions>>().Value);
                    return provider.GetService<IIntentUsageSink>() is { } sink
                        ? new MeteringEmbeddingGenerator(inner, sink, model.Provider.ToString(), model.Id)
                        : inner;
                });
        }

        // Resolve the final options after application Configure delegates have run.
        // An empty marker preserves Providers' fallback to the default IChatClient.
        services.TryAddSingleton(provider => new AIDefaults(
            provider.GetRequiredService<IOptions<AIOptions>>().Value.Default.Model ?? string.Empty));
        services.TryAddSingleton(DefaultChatClient);
        services.TryAddSingleton(DefaultEmbeddingGenerator);

        services.AddHostedService<LlmWarmupHostedService>();
    }

    internal static ILlmProviderFactory Factory(AiProvider provider)
        => Factories.GetValueOrDefault(provider)
            ?? throw new ArgumentException($"Provider '{provider}' does not support runtime chat model selection.", nameof(provider));

    private static IChatClient BuildChatPipeline(IServiceProvider provider, LLMModel model)
        => BuildChatPipeline(provider, model, Factories[model.Provider].CreateChatClient(
            model, provider.GetRequiredService<IOptions<AIOptions>>().Value));

    internal static IChatClient BuildChatPipeline(IServiceProvider provider, LLMModel model, IChatClient innerClient)
        => BuildChatPipeline(provider, model.SupportsTools, model.Marker.Name, innerClient,
            model.Provider.ToString(), model.Id);

    internal static IChatClient BuildChatPipeline(IServiceProvider provider, bool supportsTools, string telemetryName,
        IChatClient innerClient, string? meterProvider = null, string? meterModel = null,
        bool rejectUnsupportedTools = false, bool useFunctionInvocation = true)
    {
        var configuration = provider.GetRequiredService<IOptions<AIOptions>>().Value;
        // Capture is deployment-gated and request-gated: the host must allow it, and the calling
        // request must be the local owner's ordinary dev run (D14). The per-turn agent client is
        // rebuilt inside that request's ambient scope, so Personal/Credential runs stay uncaptured.
        var captureContent = (configuration.Telemetry.EnableSensitiveData ?? false)
            && ContentCaptureScope.Current is { AllowsCapture: true };
        var loggerFactory = provider.GetService<ILoggerFactory>();
        var pipeline = new ChatClientBuilder(innerClient);
        if (!supportsTools)
        {
            // Models that cannot emit tool calls must never be told about tools —
            // the assistant then answers capability questions honestly with "no".
            pipeline = pipeline.Use(async (messages, options, next, cancellationToken) =>
            {
                if (options?.Tools is { Count: > 0 })
                {
                    if (rejectUnsupportedTools) { throw new InvalidOperationException("The pinned model does not declare tool support."); }
                    options = options.Clone();
                    options.Tools = null;
                    options.ToolMode = null;
                }

                await next(messages, options, cancellationToken).ConfigureAwait(false);
            });
        }
        // Inference-only clients leave the tool loop to the caller but still need GenAI spans.
        if (useFunctionInvocation) { pipeline = pipeline.UseFunctionInvocation(); }
        pipeline = pipeline.UseOpenTelemetry(
            loggerFactory: loggerFactory,
            sourceName: $"{TelemetrySource}.{telemetryName}",
            configure: telemetry => telemetry.EnableSensitiveData = captureContent);
        // Innermost: one metered entry per raw provider call, before any function-invocation
        // aggregation, and never a second entry for the same call.
        if (meterProvider is not null && meterModel is not null && provider.GetService<IIntentUsageSink>() is { } sink)
        {
            pipeline = pipeline.Use(inner => new MeteringChatClient(inner, sink, meterProvider, meterModel));
        }
        return pipeline.Build(provider);
    }

    private static IChatClient DefaultChatClient(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IOptions<AIOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(configuration.Default.Profile)
            || !string.IsNullOrWhiteSpace(configuration.Default.Provider)
            || configuration.Default.Reasoning is not null || configuration.Default.MaxOutputTokens is not null)
        {
            var profiles = provider.GetRequiredService<ModelProfiles>();
            return profiles.CreateClient(profiles.Resolve(null));
        }
        var model = configuration.Default.Model is { Length: > 0 } markerName
            ? LLMModel.FindByMarkerName(markerName)
                ?? throw UnknownMarker(DefaultModelKey, markerName, LLMModel.All.Select(static m => m.Marker.Name))
            : FirstConfiguredModel(configuration);
        return provider.GetRequiredKeyedService<IChatClient>(model.Marker);
    }

    private static LLMModel FirstConfiguredModel(AIOptions configuration)
        => LLMModel.All.FirstOrDefault(model => Factories[model.Provider].IsConfigured(configuration))
            ?? throw new InvalidOperationException(
                $"No LLM provider is configured. Supply a provider API key (for example "
                + $"{ConfigurationRoot}:OpenAI:ApiKey) or an Ollama endpoint, or pin {DefaultModelKey}.");

    private static IEmbeddingGenerator<string, Embedding<float>> DefaultEmbeddingGenerator(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IOptions<AIOptions>>().Value;

        if (configuration.Default.Embedding is { Length: > 0 } markerName)
        {
            var configured = EmbeddingModel.FindByMarkerName(markerName)
                ?? throw UnknownMarker(DefaultEmbeddingKey, markerName, EmbeddingModel.All.Select(static m => m.Marker.Name));
            return provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(configured.Marker);
        }

        // The default embedding stays pinned to the local model unless explicitly
        // configured: silently switching it changes vector dimensions and orphans
        // every existing Qdrant collection.
        var local = EmbeddingModel.All.Single(static model => model.Marker == typeof(Ollama.IEmbeddingGemma));
        if (!Factories[local.Provider].IsConfigured(configuration))
        {
            throw new InvalidOperationException(
                $"No embedding model is configured. Supply an Ollama endpoint for {local.Marker.Name}, "
                + $"or pin {DefaultEmbeddingKey} to a configured cloud embedding "
                + $"({string.Join(", ", EmbeddingModel.All.Select(static m => m.Marker.Name))}).");
        }

        return provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(local.Marker);
    }

    internal static void AddImageGeneration(IServiceCollection services, IConfiguration configuration)
        => AddImageGeneration(services, AIOptions.Read(configuration));

    internal static void AddImageGeneration(IServiceCollection services, AIOptions configuration)
    {
        // The marker names the model, as with chat and embeddings; an unpinned
        // key keeps the catalogue's first entry.
        var markerName = configuration.Default.Image;
        var model = string.IsNullOrEmpty(markerName)
            ? ImageModel.All[0]
            : ImageModel.FindByMarkerName(markerName);

        // Resolution failure is deferred into the factory rather than thrown here:
        // this runs during service registration, so throwing would take the whole
        // silo down over a typo in a peripheral feature. Default:Model does the
        // same, and Default:Transcription degrades to a 503 with the reason.
        if (model is null)
        {
            services.AddSingleton<IImageGeneration>(_ => throw UnknownMarker(
                DefaultImageKey, markerName!, ImageModel.All.Select(static m => m.Marker.Name)));
            return;
        }

        if (configuration.Provider(model.Provider).ApiKey is { Length: > 0 })
        {
            services.AddSingleton<IImageGeneration>(sp =>
                new OpenAIImageGeneration(model, sp.GetRequiredService<IOptions<AIOptions>>()));
        }
    }

    private static InvalidOperationException UnknownMarker(
        string configurationKey,
        string markerName,
        IEnumerable<string> knownMarkerNames)
        => new($"{configurationKey} names unknown model '{markerName}'. "
            + $"Known models: {string.Join(", ", knownMarkerNames)}.");
}