using Brain.Contracts;
using Microsoft.Extensions.Configuration;
namespace Brain.Modules.Ai;

public enum ModelTier { Fast, Balanced, Reasoning }

public sealed record ModelBinding(ModelTier Tier, string Provider, string Model);

public sealed class ModelCatalog
{
    private readonly IReadOnlyDictionary<ModelTier, ModelBinding> _bindings;

    public ModelCatalog(IEnumerable<ModelBinding> bindings)
    {
        _bindings = bindings.ToDictionary(binding => binding.Tier);
    }

    public ModelBinding Resolve(ModelTier tier) =>
        _bindings.TryGetValue(tier, out var binding)
            ? binding
            : throw new BrainException(BrainErrors.ModelUnavailable, $"no model bound for tier '{tier}'");

    public static ModelTier ParseTier(string neuronId)
    {
        const string prefix = "llm/";
        if (!neuronId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new BrainException("input.invalid", $"'{neuronId}' is not an llm neuron id");
        }
        var remainder = neuronId[prefix.Length..];
        var dashIndex = remainder.IndexOf('-');
        var segment = dashIndex >= 0 ? remainder[..dashIndex] : remainder;
        return Enum.TryParse<ModelTier>(segment, ignoreCase: true, out var tier)
            ? tier
            : throw new BrainException("input.invalid", $"'{segment}' is not a known model tier");
    }

    public static ModelCatalog FromConfiguration(IConfiguration config)
    {
        var section = config.GetSection("Brain:Ai");
        var provider = section["Provider"] ?? "ollama";
        var bindings = Enum.GetValues<ModelTier>()
            .Select(tier => new ModelBinding(tier, provider, section[tier.ToString()] ?? "llama3.1:8b"));
        return new ModelCatalog(bindings);
    }
}
