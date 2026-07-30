using DigitalBrain.Behaviors.Manifest;

namespace DigitalBrain.Behaviors;

internal static class BehaviorContractCompatibility
{
    public static BehaviorContractCompatibilityResult Assess(
        BehaviorContractManifest prior,
        BehaviorContractManifest next,
        IReadOnlyDictionary<string, string>? caseIdMappings = null)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(next);

        if (!string.Equals(prior.BehaviorContractId, next.BehaviorContractId, StringComparison.Ordinal))
        {
            return BehaviorContractCompatibilityResult.Breaking(
                "Behavior contract identity changed; a new major version is required.");
        }

        var mappings = caseIdMappings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var priorById = prior.Cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        var nextById = next.Cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        var nextNames = next.Cases.ToDictionary(item => item.CaseName, StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            if (!priorById.ContainsKey(mapping.Key))
            {
                return BehaviorContractCompatibilityResult.MappingRequired(
                    $"Case-ID mapping source '{mapping.Key}' is not present on the prior contract.");
            }

            if (!nextById.ContainsKey(mapping.Value) && !nextNames.ContainsKey(mapping.Value))
            {
                return BehaviorContractCompatibilityResult.MappingRequired(
                    $"Case-ID mapping target '{mapping.Value}' is not present on the next contract.");
            }
        }

        var mappedPriorIds = new HashSet<string>(StringComparer.Ordinal);
        var mappedNextIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            mappedPriorIds.Add(mapping.Key);
            var targetId = nextById.ContainsKey(mapping.Value)
                ? mapping.Value
                : nextNames[mapping.Value].CaseId;
            mappedNextIds.Add(targetId);
        }

        var unmappedPrior = prior.Cases
            .Where(item => !mappedPriorIds.Contains(item.CaseId))
            .Select(item => item.CaseId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unmappedNext = next.Cases
            .Where(item => !mappedNextIds.Contains(item.CaseId))
            .Select(item => item.CaseId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (!unmappedPrior.SequenceEqual(unmappedNext, StringComparer.Ordinal))
        {
            var removed = unmappedPrior.Except(unmappedNext, StringComparer.Ordinal).ToArray();
            var added = unmappedNext.Except(unmappedPrior, StringComparer.Ordinal).ToArray();

            if (removed.Length > 0 || added.Length > 0)
            {
                if (LooksLikeRename(prior, next, removed, added))
                {
                    return BehaviorContractCompatibilityResult.MappingRequired(
                        "Case rename requires an explicit case-ID mapping; otherwise raise ContractMajorVersion.");
                }

                return BehaviorContractCompatibilityResult.Breaking(
                    "Adding, removing, or replacing a union case requires a contract major version.");
            }
        }

        foreach (var caseId in unmappedPrior)
        {
            var left = priorById[caseId];
            var right = nextById[caseId];
            if (!string.Equals(left.PayloadSchemaJson, right.PayloadSchemaJson, StringComparison.Ordinal)
                || left.CaseSchemaVersion != right.CaseSchemaVersion)
            {
                return BehaviorContractCompatibilityResult.Breaking(
                    $"Case '{caseId}' payload schema changed; a contract major version is required.");
            }
        }

        if (!string.Equals(prior.ResultSchemaJson, next.ResultSchemaJson, StringComparison.Ordinal))
        {
            return BehaviorContractCompatibilityResult.Breaking(
                "Result schema changes require a contract major version.");
        }

        if (next.ContractMajorVersion < prior.ContractMajorVersion)
        {
            return BehaviorContractCompatibilityResult.Breaking(
                "Contract major version cannot decrease.");
        }

        return BehaviorContractCompatibilityResult.Compatible(
            "Case reordering and explicit case-ID mappings are compatible without a major version.");
    }

    private static bool LooksLikeRename(
        BehaviorContractManifest prior,
        BehaviorContractManifest next,
        string[] removed,
        string[] added)
        => removed.Length == 1
            && added.Length == 1
            && prior.Cases.Count == next.Cases.Count
            && string.Equals(
                prior.Cases.Single(item => item.CaseId == removed[0]).PayloadSchemaJson,
                next.Cases.Single(item => item.CaseId == added[0]).PayloadSchemaJson,
                StringComparison.Ordinal);
}

internal sealed record BehaviorContractCompatibilityResult(
    bool IsCompatible,
    bool RequiresMajorVersion,
    bool RequiresCaseIdMapping,
    string Detail)
{
    public static BehaviorContractCompatibilityResult Compatible(string detail)
        => new(true, false, false, detail);

    public static BehaviorContractCompatibilityResult Breaking(string detail)
        => new(false, true, false, detail);

    public static BehaviorContractCompatibilityResult MappingRequired(string detail)
        => new(false, false, true, detail);
}
