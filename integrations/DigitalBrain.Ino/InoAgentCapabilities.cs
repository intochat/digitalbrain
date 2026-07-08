using DigitalBrain.Core;
using DigitalBrain.Core.Sdk;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Ino;

public sealed record InoCapabilityRecord(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Examples,
    string Tier,
    string Origin,
    string SourceKind,
    string TrustLevel)
{
    public InoIntentClassifier.Capability ToClassifierCapability() =>
        new(Id, Description, Examples.ToArray(), Tier);

    public CapabilityRegistered ToCapabilityRegistered() =>
        new(Id, Description, Examples, Tier, Origin);

    public string ToMemoryText() =>
        "capability:" + Id +
        " source:" + SourceKind +
        " trust:" + TrustLevel +
        " origin:" + Origin +
        " display:" + DisplayName +
        " description:" + Description +
        " aliases:" + string.Join(",", Aliases) +
        " examples:" + string.Join(" | ", Examples);

    public bool Matches(string value)
    {
        var text = value.ToLowerInvariant();
        return text.Contains(Id, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(DisplayName) &&
                text.Contains(DisplayName, StringComparison.OrdinalIgnoreCase)) ||
               Aliases.Any(alias => !string.IsNullOrWhiteSpace(alias) &&
                                    text.Contains(alias.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
    }
}

public static class InoAgentCapabilities
{
    public static IReadOnlyList<InoCapabilityRecord> KnownAgentRecords { get; } =
    [
        FromAgent<IGmailNeuron>("gmail", "gmail", "DigitalBrain.Google.IGmailNeuron"),
        FromAgent<ISalesforceCrmNeuron>("salesforce", "salesforce", "DigitalBrain.Salesforce.ISalesforceCrmNeuron")
    ];

    public static InoCapabilityRecord FromAgent<TContract>(
        string? id = null,
        string? tier = null,
        string? origin = null)
        where TContract : IAgent
    {
        var metadata = NeuronAgentMetadata.ReadFrom<TContract>();
        var aliases = metadata.Capabilities
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedId = string.IsNullOrWhiteSpace(id)
            ? NormalizeId(aliases.FirstOrDefault() ?? metadata.DisplayName)
            : NormalizeId(id);

        var examples = metadata.RoutingExamples.Length > 0
            ? metadata.RoutingExamples
            : aliases;

        return new InoCapabilityRecord(
            resolvedId,
            metadata.DisplayName,
            metadata.Description,
            aliases,
            examples,
            string.IsNullOrWhiteSpace(tier) ? resolvedId : tier,
            string.IsNullOrWhiteSpace(origin) ? typeof(TContract).FullName ?? typeof(TContract).Name : origin,
            SourceKind: "IAgent",
            TrustLevel: "System");
    }

    private static string NormalizeId(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "agent" : normalized;
    }
}
