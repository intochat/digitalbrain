namespace DigitalBrain.Core.V2;

public sealed record V2ModelDescriptor(string Key, bool Healthy, bool SupportsTools, bool SupportsStructuredOutput, bool SupportsEmbedding, bool SupportsVoice, string PrivacyClass, string Residency, decimal CostPerToken, TimeSpan TypicalLatency);
public sealed record V2ModelPolicy(string RequiredPrivacy, string RequiredResidency, decimal MaxCostPerToken, TimeSpan MaxLatency, int MaxTokens, bool RequireTools, bool RequireStructuredOutput, bool RequireEmbedding, bool RequireVoice);
public sealed record V2ModelSelection(string Key, int MaxTokens, string Reason);

public interface IV2ModelHealth
{
    bool IsHealthy(string key);
}

public sealed class V2ModelRouter(IReadOnlyList<V2ModelDescriptor> models, IV2ModelHealth? health = null)
{
    public V2ModelSelection Select(V2ModelPolicy policy)
    {
        if (policy.MaxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(policy.MaxTokens));
        var candidates = models
            .Where(x => x.Healthy && (health?.IsHealthy(x.Key) ?? true))
            .Where(x => string.Equals(x.PrivacyClass, policy.RequiredPrivacy, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(x.Residency, policy.RequiredResidency, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.CostPerToken <= policy.MaxCostPerToken && x.TypicalLatency <= policy.MaxLatency)
            .Where(x => !policy.RequireTools || x.SupportsTools)
            .Where(x => !policy.RequireStructuredOutput || x.SupportsStructuredOutput)
            .Where(x => !policy.RequireEmbedding || x.SupportsEmbedding)
            .Where(x => !policy.RequireVoice || x.SupportsVoice)
            .OrderBy(x => x.CostPerToken)
            .ThenBy(x => x.TypicalLatency)
            .ToArray();
        var selected = candidates.FirstOrDefault() ?? throw new InvalidOperationException("No V2 model satisfies the tenant/workspace policy and capability budget.");
        return new V2ModelSelection(selected.Key, policy.MaxTokens, "policy-compatible-lowest-cost");
    }
}
