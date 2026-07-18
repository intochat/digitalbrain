using System.Reflection;
using Ino.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting.Llm;

public static class AddInoChatClientsExtensions
{
    // Legacy env-var name kept for the InoTestAppHost path (Ino.Testing) which
    // still mutates Environment.SetEnvironmentVariable("INO_TEST_MODE", "true")
    // on a clean machine. The newer Ino.AppHost.Testing path stamps Ino:Mode
    // = Testing through Aspire's WithEnvironment chain instead, and that's
    // the preferred way — neither path needs to know about the other.
    public const string TestModeEnvVar = "INO_TEST_MODE";
    public const string ModeConfigKey = "Ino:Mode";
    public const string TestingMode = "Testing";

    public static IHostApplicationBuilder AddInoChatClients(
        this IHostApplicationBuilder builder)
    {
        // Test mode swaps in the BDD-mock factory: production declarations from
        // the AppHost (Grok/xAI bindings, etc.) are still propagated as
        // Ino:Llm:Models env vars, but at silo startup we ignore them and resolve
        // every IChatClient through BddMockChatClientFactory backed by
        // Features/*.feature files. This keeps the AppHost as the single source
        // of architectural truth while letting an Ino.AppHost.Testing boot (and
        // any legacy fixture that flips INO_TEST_MODE=true) succeed without a
        // real xAI key. Reading through IConfiguration unifies both wire forms
        // (Ino:Mode=Testing and INO_TEST_MODE=true) so neither call site has to
        // know about Environment.GetEnvironmentVariable.
        var testMode =
            string.Equals(builder.Configuration[ModeConfigKey], TestingMode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(builder.Configuration[TestModeEnvVar], "true", StringComparison.OrdinalIgnoreCase);

        if (testMode)
        {
            ConfigureTestMode(builder);
            return builder;
        }

        // Production silos: no Bdd scenarios, but Cortex still wants an
        // INeuronPromptCorpus to query — register an empty corpus so the
        // regex fast-path (Phase 3) is a no-op rather than a missing-service
        // failure. When Cortex misses the corpus, it falls through to the
        // LLM classifier.
        builder.Services.AddSingleton<INeuronPromptCorpus>(_ =>
            new BddScenarioPromptCorpus(Array.Empty<BddScenario>()));

        var bindings = ReadDeclaredBindings(builder.Configuration);
        if (bindings.Count == 0)
            return builder;

        // Resolve every declared model's provider factory by scanning the
        // model's home assembly for an ILlmProviderFactory. Convention:
        // each Ino.Llm.<Provider> assembly contains exactly one factory.
        var resolved = new List<(LlmModel Model, LlmTier Tier, ILlmProviderFactory Factory)>();
        var declaredProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (model, tier) in bindings)
        {
            var factory = DiscoverProviderFactory(model)
                ?? throw new InvalidOperationException(
                    $"No ILlmProviderFactory found in '{model.GetType().Assembly.GetName().Name}' " +
                    $"for provider '{model.Provider}'. Add an ILlmProviderFactory implementation " +
                    "to that assembly (convention: one factory per Ino.Llm.<Provider> package).");

            if (!factory.IsConfigured(builder.Configuration))
                throw new InvalidOperationException(
                    $"Provider '{factory.Provider}' is not configured. Aspire prompts for the " +
                    $"'{factory.Provider}-api-key' parameter in the dashboard on first run; " +
                    $"enter it there and the silo will pick it up.");

            resolved.Add((model, tier, factory));
            declaredProviders.Add(factory.Provider);
        }

        // One named, resilience-equipped HttpClient per provider. Provider
        // factories pull their client by provider name in CreateClient.
        builder.AddInoLlmResilience(declaredProviders);

        builder.Services.AddSingleton<IChatClientFactory>(sp =>
            new ProviderBackedChatClientFactory(
                resolved,
                sp.GetRequiredService<IConfiguration>(),
                sp.GetService<IHttpClientFactory>(),
                sp.GetService<ILoggerFactory>()));

        foreach (var tier in new[] { LlmTier.Fast, LlmTier.Balanced, LlmTier.Reasoning })
        {
            builder.Services.AddKeyedSingleton<IChatClient>(tier, (sp, _) =>
                sp.GetRequiredService<IChatClientFactory>().ForTier(tier));
        }

        return builder;
    }

    static void ConfigureTestMode(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IReasoningProbe, InMemoryReasoningProbe>();

        // Load scenarios once at silo startup. Both BddMockChatClientFactory
        // (mock LLM replies) and BddScenarioPromptCorpus (Cortex routing
        // table) consume the same list — the .feature files are the single
        // source of truth for both behaviours.
        builder.Services.AddSingleton<IReadOnlyList<BddScenario>>(sp =>
        {
            var log = sp.GetService<ILoggerFactory>()?.CreateLogger<BddMockChatClient>();
            var paths = new[] { Path.Combine(AppContext.BaseDirectory, "Features") };
            var result = BddScenarioLoader.LoadFromDirectories(paths);
            log?.LogInformation(
                "bdd loaded {ScenarioCount} scenario(s) from {PathCount} path(s); skipped {SkippedCount}",
                result.Scenarios.Count, paths.Length, result.SkippedReasons.Count);
            foreach (var reason in result.SkippedReasons)
                log?.LogWarning("bdd skipped scenario: {Reason}", reason);
            return result.Scenarios;
        });

        builder.Services.AddSingleton<INeuronPromptCorpus>(sp =>
            new BddScenarioPromptCorpus(sp.GetRequiredService<IReadOnlyList<BddScenario>>()));

        builder.Services.AddSingleton<IChatClientFactory>(sp =>
        {
            var probe = sp.GetRequiredService<IReasoningProbe>();
            var log = sp.GetService<ILoggerFactory>()?.CreateLogger<BddMockChatClient>();
            var scenarios = sp.GetRequiredService<IReadOnlyList<BddScenario>>();
            return new BddMockChatClientFactory(scenarios, probe, log);
        });

        foreach (var tier in new[] { LlmTier.Fast, LlmTier.Balanced, LlmTier.Reasoning })
        {
            builder.Services.AddKeyedSingleton<IChatClient>(tier, (sp, _) =>
                sp.GetRequiredService<IChatClientFactory>().ForTier(tier));
        }
    }

    static List<(LlmModel Model, LlmTier Tier)> ReadDeclaredBindings(IConfiguration config)
    {
        var section = config.GetSection("Ino:Llm:Models");
        return section.GetChildren()
            .Select(c => new
            {
                TypeName = c["Type"] ?? "",
                Tier = Enum.Parse<LlmTier>(c["Tier"] ?? "Balanced"),
            })
            .Where(c => !string.IsNullOrWhiteSpace(c.TypeName))
            .Select(c =>
            {
                var type = Type.GetType(c.TypeName, throwOnError: true)!;
                var model = (LlmModel)Activator.CreateInstance(type)!;
                return (model, c.Tier);
            })
            .ToList();
    }

    static ILlmProviderFactory? DiscoverProviderFactory(LlmModel model)
    {
        // Cache one factory instance per (assembly, provider) so models from
        // the same provider package share state (= zero state today, but
        // future factories may want a connection pool or cached HttpClient).
        var assembly = model.GetType().Assembly;
        var factoryType = assembly
            .GetTypes()
            .FirstOrDefault(t =>
                !t.IsAbstract
                && !t.IsInterface
                && typeof(ILlmProviderFactory).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null);

        if (factoryType is null)
            return null;

        var factory = (ILlmProviderFactory)Activator.CreateInstance(factoryType)!;
        if (!string.Equals(factory.Provider, model.Provider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Factory '{factoryType.FullName}' reports provider '{factory.Provider}' " +
                $"but model '{model.GetType().FullName}' declares '{model.Provider}'. " +
                "These must match.");

        return factory;
    }
}
