using Ino.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Tier-keyed <see cref="IChatClientFactory"/> that aggregates one or more
/// <see cref="ILlmProviderFactory"/> instances. Holds the AppHost-declared
/// tier→model bindings; on <see cref="ForTier"/> looks up the bound model,
/// dispatches to the model's provider factory, and caches the resulting
/// <see cref="IChatClient"/> by model id. If no model is bound for the
/// requested tier, falls back to the highest-bound tier ≤ the requested
/// one (Reasoning > Balanced > Fast) — same fallback semantics the
/// previous xAI-only factory exposed.
/// </summary>
public sealed class ProviderBackedChatClientFactory : IChatClientFactory
{
    readonly IConfiguration _config;
    readonly IHttpClientFactory? _httpFactory;
    readonly ILoggerFactory? _logFactory;
    readonly Dictionary<LlmTier, (LlmModel Model, ILlmProviderFactory Factory)> _byTier;
    readonly Dictionary<string, IChatClient> _cache = new(StringComparer.Ordinal);
    readonly LlmModel[] _models;

    public ProviderBackedChatClientFactory(
        IEnumerable<(LlmModel Model, LlmTier Tier, ILlmProviderFactory Factory)> bindings,
        IConfiguration config,
        IHttpClientFactory? httpFactory = null,
        ILoggerFactory? logFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var materialized = bindings.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException(
                "At least one (model, tier, factory) binding is required.",
                nameof(bindings));

        _config = config;
        _httpFactory = httpFactory;
        _logFactory = logFactory;
        _byTier = materialized.ToDictionary(b => b.Tier, b => (b.Model, b.Factory));
        _models = materialized.Select(b => b.Model).ToArray();
    }

    public IReadOnlyList<LlmModel> RegisteredModels => _models;

    public IChatClient ForTier(LlmTier tier)
    {
        if (tier == LlmTier.None)
            throw new ArgumentException(
                "LlmTier.None is not a valid request — callers must ask for Fast/Balanced/Reasoning.",
                nameof(tier));

        var (model, factory) = ResolveBinding(tier)
            ?? throw new InvalidOperationException(
                $"No model bound for tier {tier} and no lower-tier fallback available. " +
                $"Bound tiers: {string.Join(", ", _byTier.Keys)}.");

        if (_cache.TryGetValue(model.Id, out var cached))
            return cached;

        var http = _httpFactory?.CreateClient(model.Provider);
        var inner = factory.CreateClient(model, _config, http);

        var built = new ChatClientBuilder(inner)
            .UseOpenTelemetry(loggerFactory: _logFactory)
            .Build();

        _cache[model.Id] = built;
        return built;
    }

    (LlmModel Model, ILlmProviderFactory Factory)? ResolveBinding(LlmTier requested)
    {
        if (_byTier.TryGetValue(requested, out var exact))
            return exact;

        // Walk down: a Reasoning request falls through to Balanced then Fast,
        // a Balanced request to Fast, never the other way.
        foreach (var tier in new[] { LlmTier.Reasoning, LlmTier.Balanced, LlmTier.Fast })
        {
            if ((int)tier <= (int)requested && _byTier.TryGetValue(tier, out var match))
                return match;
        }

        return null;
    }
}
