#pragma warning disable MEAI001

using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using DigitalBrain.Runtime.Ai;
using DigitalBrain.SDK.Postgres;

using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.Sqlite.Sqlite;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm.Providers;
using DigitalBrain.SDK.Google.Auth;
using DigitalBrain.SDK.Google.Gmail;
using DigitalBrain.SDK.Google.YouTube;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.AiHealth;
using DigitalBrain.SDK.DigitalBrain.Ai.Embedding;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using DigitalBrain.SDK.DigitalBrain.Ai.Voice;
using DigitalBrain.SDK.DigitalBrain.Persistence;
using DigitalBrain.SDK.Microsoft.CSharp;
using DigitalBrain.SDK.DigitalBrain.Security;
using DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub;
using DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;
using DigitalBrain.SDK.Microsoft.Aspire.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public static class DigitalBrainSiloDomainsExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainSiloDomains(this IHostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        // 1. Core Orleans Substrate Configuration (Orleans silo)
        builder.AddDigitalBrainSilo();

        // 2. Sqlite / Data Domain
        builder.Services.AddSingleton<IDatabaseContextFactory, DatabaseContextFactory>();

        // 3. AI / LLM Domain
        ConfigureAiDomain(builder, configuration);

        // 4. Aspire Domain
        builder.Services.AddDigitalBrainSdkAspire();

        // 5. Developer Domain
        ConfigureDeveloperDomain(builder, configuration);

        // 5b. Widget-Canvas demo neurons (Clock / Reminder / Flight)
        builder.Services.AddSingleton<IInterpretedNeuronSource, global::DigitalBrain.WidgetCanvas.WidgetCanvasInoSource>();

        // 6. Google Domain
        ConfigureGoogleDomain(builder, configuration);

        // 7. Identity Domain
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-locked-out", "DigitalBrain.SDK.Identity.IdentityStore"));
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-valid-login", "DigitalBrain.SDK.Identity.IdentityStore"));
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-equal", "DigitalBrain.SDK.Identity.EqualityStore"));

        // 8. Onboarding Domain
        builder.Services.AddSingleton(new PredicateNeuronBinding("accepted-version", "DigitalBrain.Domains.Onboarding.OnboardingStore"));
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-current-version", "DigitalBrain.Kernel.Settings.SettingsStore"));

        // 9. Postgres Domain
        ConfigurePostgresDomain(builder, configuration);

        // 10. Security Domain
        builder.Services.AddSingleton<IKernelUser, OrleansKernelUser>();
        builder.Services.AddSingleton<ISettingService, OrleansSettingService>();
        builder.Services.AddSingleton<ISecretVault, OrleansSecretVault>();
        builder.Services.AddSingleton<IDynamicScriptingService, DynamicScriptingService>();

        return builder;
    }

    private static void ConfigureAiDomain(IHostApplicationBuilder builder, IConfiguration configuration)
    {
        builder.Services.AddSingleton<IAiHealthLogic, AiHealthLogic>();
        var useMock = string.Equals(
            configuration["DigitalBrain:Ai:UseMockClient"], "true", StringComparison.OrdinalIgnoreCase);

        if (useMock)
        {
            var registry = new MockModelRegistry();
            foreach (var model in LlmModel.All)
            {
                registry.LlmModelIds.Add(model.ServiceKey);
                var mock = new BddMockChatClient();
                builder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey, (sp, _) =>
                    new ChatClientBuilder(mock)
                        .Build(sp));
            }

            registry.LlmModelIds.Add("ino-local");
            var localMock = new BddMockChatClient();
            builder.Services.AddKeyedSingleton<IChatClient>("ino-local", (sp, _) =>
                new ChatClientBuilder(localMock)
                    .Build(sp));

            // Even with the deterministic mock on for intent/planner/creator/etc., route the
            // multi-agent panel's tiers to the real local Ollama when a hosted endpoint is
            // present. Registered after the mock keys so the real clients win. The endpoint is
            // absent in tests, so they stay fully mocked.
            var localPanelEndpoint = configuration["ConnectionStrings:ino-local"];
            if (!string.IsNullOrEmpty(localPanelEndpoint))
            {
                var turnModel = new NemotronMini();
                var synthModel = new Phi4();
                RegisterLocalOllamaChatClient(builder, localPanelEndpoint, turnModel.ServiceKey, turnModel.Id);
                RegisterLocalOllamaChatClient(builder, localPanelEndpoint, synthModel.ServiceKey, synthModel.Id);
            }

            foreach (var model in EmbeddingModel.All)
            {
                var stub = new StubEmbeddingGenerator();
                builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(model.ServiceKey, stub);
            }

            builder.Services.AddSingleton<ISpeechToTextClient>(_ => new StubSpeechToTextClient());
            RegisterFacetMappers(builder.Services);
            builder.Services.AddSingleton(registry);
            builder.Services.AddHostedService<MockChatClientAutoPrimer>();
        }
        else
        {
            ILlmProviderFactory[] factories =
            [
                new OpenAiProviderFactory(),
                new AnthropicProviderFactory(),
                new OllamaProviderFactory(),
                new GrokProviderFactory(),
            ];
            var factoriesDict = factories.ToDictionary(f => f.ProviderName, StringComparer.Ordinal);
            var privateCluster = string.Equals(
                configuration["DigitalBrain:Ai:PrivateCluster"], "true", StringComparison.OrdinalIgnoreCase);

            foreach (var model in LlmModel.All)
            {
                if (privateCluster)
                {
                    if (factoriesDict.TryGetValue("ollama", out var ollamaFactory))
                    {
                        builder.Services.AddKeyedSingleton(model.ServiceKey, (sp, _) =>
                            new ChatClientBuilder(ollamaFactory.CreateClient(model, configuration))
                                .UseLogging(sp.GetRequiredService<ILoggerFactory>())
                                .UseStreamingUsage()
                                .UseOpenTelemetry(
                                    loggerFactory: sp.GetService<ILoggerFactory>(),
                                    configure: telemetry => telemetry.EnableSensitiveData = true)
                                .Build(sp));
                    }
                    continue;
                }

                if (!factoriesDict.TryGetValue(model.Provider, out var factory)) continue;
                if (!factory.IsConfigured(configuration)) continue;

                builder.Services.AddKeyedSingleton(model.ServiceKey, (sp, _) =>
                    new ChatClientBuilder(factory.CreateClient(model, configuration))
                        .UseLogging(sp.GetRequiredService<ILoggerFactory>())
                        .UseStreamingUsage()
                        .UseOpenTelemetry(
                            loggerFactory: sp.GetService<ILoggerFactory>(),
                            configure: telemetry => telemetry.EnableSensitiveData = true)
                        .Build(sp));
            }

            var localModelEndpoint = configuration["ConnectionStrings:ino-local"];
            if (!string.IsNullOrEmpty(localModelEndpoint))
            {
                var localModelName = configuration["DigitalBrain:Ai:LocalModelName"] ?? new NemotronMini().Id;

                // Back-compat key used by the .ino LocalModel resolution path.
                RegisterLocalOllamaChatClient(builder, localModelEndpoint, "ino-local", localModelName);

                // Model-keyed clients so [Llm<NemotronMini>] / [Llm<Phi4>] resolve against
                // the same local Ollama instance (one instance serves both models).
                var turnModel = new NemotronMini();
                var synthModel = new Phi4();
                RegisterLocalOllamaChatClient(builder, localModelEndpoint, turnModel.ServiceKey, turnModel.Id);
                RegisterLocalOllamaChatClient(builder, localModelEndpoint, synthModel.ServiceKey, synthModel.Id);
            }

            var openAiKey = configuration["DigitalBrain:Ai:OpenAiApiKey"];
            if (!string.IsNullOrEmpty(openAiKey))
            {
                foreach (var model in EmbeddingModel.All.Where(m => m.Provider == "openai"))
                {
                    var capturedKey = openAiKey;
                    var capturedId = model.Id;
                    builder.Services.AddKeyedSingleton(
                        model.ServiceKey, (_, _) =>
                        {
                            var client = new OpenAIClient(new ApiKeyCredential(capturedKey));
                            return client.GetEmbeddingClient(capturedId).AsIEmbeddingGenerator();
                        });
                }
            }

            RegisterFacetMappers(builder.Services);

            var voiceId = configuration["DigitalBrain:Ai:Voice:Id"];
            if (!string.IsNullOrEmpty(voiceId))
            {
                var fileName = configuration["DigitalBrain:Ai:Voice:FileName"]
                    ?? throw new InvalidOperationException(
                        "DigitalBrain:Ai:Voice:Id is set but DigitalBrain:Ai:Voice:FileName is missing.");
                var sha = configuration["DigitalBrain:Ai:Voice:Sha256"];
                builder.Services.AddSingleton(_ => WhisperSpeechToTextClientFactory.Create(fileName, sha));
            }
        }
    }

    private static void RegisterLocalOllamaChatClient(
        IHostApplicationBuilder builder, string endpoint, string serviceKey, string modelName)
    {
        builder.Services.AddKeyedSingleton<IChatClient>(serviceKey, (sp, _) =>
        {
            var uriString = endpoint;
            if (endpoint.Contains('='))
            {
                var parts = endpoint.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && string.Equals(kv[0].Trim(), "Endpoint", StringComparison.OrdinalIgnoreCase))
                    {
                        uriString = kv[1].Trim();
                        break;
                    }
                }
            }
            var openAiEndpoint = new Uri(new Uri(uriString), "/v1");
            var openAiClient = new OpenAIClient(
                new ApiKeyCredential("ollama"),
                new OpenAIClientOptions { Endpoint = openAiEndpoint });

            return new ChatClientBuilder(openAiClient.GetChatClient(modelName).AsIChatClient())
                .UseLogging(sp.GetRequiredService<ILoggerFactory>())
                .UseStreamingUsage()
                .UseOpenTelemetry(
                    loggerFactory: sp.GetService<ILoggerFactory>(),
                    configure: telemetry => telemetry.EnableSensitiveData = true)
                .Build(sp);
        });
    }

    private static void RegisterFacetMappers(IServiceCollection services)
    {
        var llmAttributeOpenType = typeof(LlmAttribute<>);
        var llmMapperOpenType = typeof(LlmAttributeMapper<>);
        var llmFacetOpenType = typeof(IAttributeToFactoryMapper<>);

        foreach (var model in LlmModel.All)
        {
            var modelType = model.GetType();
            var attributeType = llmAttributeOpenType.MakeGenericType(modelType);
            var mapperType = llmMapperOpenType.MakeGenericType(modelType);
            var serviceType = llmFacetOpenType.MakeGenericType(attributeType);
            services.AddSingleton(serviceType, mapperType);
        }

        var embeddingAttributeOpenType = typeof(EmbeddingAttribute<>);
        var embeddingMapperOpenType = typeof(EmbeddingAttributeMapper<>);

        foreach (var model in EmbeddingModel.All)
        {
            var modelType = model.GetType();
            var attributeType = embeddingAttributeOpenType.MakeGenericType(modelType);
            var mapperType = embeddingMapperOpenType.MakeGenericType(modelType);
            var serviceType = llmFacetOpenType.MakeGenericType(attributeType);
            services.AddSingleton(serviceType, mapperType);
        }
    }

    private static void ConfigureDeveloperDomain(IHostApplicationBuilder builder, IConfiguration configuration)
    {
        var useStubs = !string.Equals(
            configuration["DigitalBrain:Developer:UseStubServices"], "false",
            StringComparison.OrdinalIgnoreCase);

        if (useStubs)
        {
            builder.Services.AddSingleton<SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub.ITokenProtector, SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub.InMemoryTokenProtector>();
        }
        else
        {
            builder.Services.AddSingleton<SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub.ITokenProtector>(_ =>
                OperatingSystem.IsWindows()
                    ? new SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub.DpapiTokenProtector()
                    : new SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub.InMemoryTokenProtector());
        }

        builder.Services.AddSingleton<IInterpretedNeuronSource, DeveloperInoSource>();
    }

    private static void ConfigureGoogleDomain(IHostApplicationBuilder builder, IConfiguration configuration)
    {
        var useStubs = !string.Equals(
            configuration["DigitalBrain:Google:UseStubServices"], "false",
            StringComparison.OrdinalIgnoreCase);

        if (useStubs)
        {
            builder.Services.AddSingleton<SDK.Google.Auth.ITokenProtector, SDK.Google.Auth.InMemoryTokenProtector>();
            builder.Services.AddSingleton<IGoogleAuthBroker, StubGoogleAuthBroker>();
            builder.Services.AddSingleton<IGmailService, StubGmailService>();
            builder.Services.AddSingleton<IYouTubeService, StubYouTubeService>();
        }
        else
        {
            builder.Services.AddSingleton<SDK.Google.Auth.ITokenProtector>(_ =>
                OperatingSystem.IsWindows()
                    ? new SDK.Google.Auth.DpapiTokenProtector()
                    : throw new PlatformNotSupportedException(
                        "ITokenProtector requires Windows DPAPI in this build."));
            builder.Services.AddSingleton<GoogleAuthBroker>();
            builder.Services.AddSingleton<IGoogleAuthBroker>(sp => sp.GetRequiredService<GoogleAuthBroker>());
            builder.Services.AddSingleton<IGmailService, GoogleGmailService>();
            builder.Services.AddSingleton<IYouTubeService, GoogleYouTubeService>();
        }
    }

    private static void ConfigurePostgresDomain(IHostApplicationBuilder builder, IConfiguration configuration)
    {
        builder.Services.AddSingleton<INpgsqlConnectionFactory, NpgsqlConnectionFactory>();
        builder.Services.AddSingleton<SynapseToPostgresMapper>();

        var cs = configuration["DigitalBrain:Data:Postgres:ConnectionString"];
        if (string.IsNullOrEmpty(cs) || cs.Equals("InMemory", StringComparison.OrdinalIgnoreCase) || cs.Contains("mock") || cs.Contains("localhost"))
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "synapses_test.db");
            builder.Services.AddPooledDbContextFactory<SynapseDbContext>(o => 
                o.UseSqlite($"Data Source={dbPath};Pooling=False"));
        }
        else
        {
            builder.Services.AddPooledDbContextFactory<SynapseDbContext>(o => 
                o.UseNpgsql(cs));
        }

        builder.Services.AddSingleton<ISynapsePersistenceService, EfCoreSynapsePersistenceService>();
    }
}

internal sealed class StreamingUsageChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private const string StreamOptionsKey = "stream_options";
    private static readonly System.Text.Json.JsonDocument StreamOptionsDoc = System.Text.Json.JsonDocument.Parse("""{"include_usage": true}""");

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = EnsureStreamOptions(options);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = EnsureStreamOptions(options);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private static ChatOptions EnsureStreamOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return new ChatOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [StreamOptionsKey] = StreamOptionsDoc.RootElement.Clone()
                }
            };
        }

        var clonedOptions = options.Clone();
        clonedOptions.AdditionalProperties ??= [];

        if (!clonedOptions.AdditionalProperties.ContainsKey(StreamOptionsKey))
        {
            clonedOptions.AdditionalProperties[StreamOptionsKey] = StreamOptionsDoc.RootElement.Clone();
        }

        return clonedOptions;
    }
}

public static class StreamingUsageChatClientExtensions
{
    public static ChatClientBuilder UseStreamingUsage(this ChatClientBuilder builder)
    {
        return builder.Use(inner => new StreamingUsageChatClient(inner));
    }
}
