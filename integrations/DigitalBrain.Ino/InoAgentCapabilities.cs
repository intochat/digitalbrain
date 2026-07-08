using DigitalBrain.Core;
using DigitalBrain.Core.Sdk;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DigitalBrain.Ino;

public sealed partial record InoCapabilityRecord(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Examples,
    string Tier,
    string Origin,
    string SourceKind,
    string TrustLevel,
    string InvocationGrainType = "",
    string InvocationGrainKey = "")
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
        " examples:" + string.Join(" | ", Examples) +
        (HasInvocationEndpoint ? " invocation:true" : "");

    public bool HasInvocationEndpoint =>
        !string.IsNullOrWhiteSpace(InvocationGrainType) &&
        !string.IsNullOrWhiteSpace(InvocationGrainKey);

    public bool Matches(string value)
    {
        var tokens = Tokenize(value);
        return ContainsTokenSet(tokens, Id) ||
               ContainsTokenSet(tokens, DisplayName) ||
               Aliases.Any(alias => ContainsTokenSet(tokens, alias)) ||
               Examples.Any(example => ContainsTokenSet(tokens, example));
    }

    private static bool ContainsTokenSet(IReadOnlySet<string> tokens, string value)
    {
        var expected = Tokenize(value);
        return expected.Count > 0 && expected.All(token => ContainsToken(tokens, token));
    }

    private static bool ContainsToken(IReadOnlySet<string> tokens, string token)
    {
        if (tokens.Contains(token))
        {
            return true;
        }

        return token.Length > 2 &&
               (tokens.Contains(token + "s") || tokens.Contains(token + "es"));
    }

    private static HashSet<string> Tokenize(string value) =>
        TokenRegex().Matches(value ?? string.Empty)
            .Select(match => InoAgentCapabilities.NormalizeId(match.Value))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

public static partial class InoAgentCapabilities
{
    public const string AgentSourceKind = "IAgent";
    public const string SystemTrustLevel = "System";

    public static IReadOnlyList<InoCapabilityRecord> DiscoverAgentRecords(IEnumerable<Assembly>? assemblies = null) =>
        (assemblies ?? AppDomain.CurrentDomain.GetAssemblies())
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .Where(type => type is { IsInterface: true } &&
                           type != typeof(IAgent) &&
                           typeof(IAgent).IsAssignableFrom(type))
            .Select(TryFromAgentType)
            .OfType<InoCapabilityRecord>()
            .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(record => record.Origin, StringComparer.Ordinal).First())
            .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
            SourceKind: AgentSourceKind,
            TrustLevel: SystemTrustLevel,
            metadata.InvocationGrainType,
            metadata.InvocationGrainKey);
    }

    private static InoCapabilityRecord? TryFromAgentType(Type contract)
    {
        var metadata = ReadMetadata(contract);
        if (string.IsNullOrWhiteSpace(metadata.DisplayName) &&
            metadata.Capabilities.Length == 0 &&
            metadata.RoutingExamples.Length == 0)
        {
            return null;
        }

        var aliases = metadata.Capabilities
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedId = NormalizeId(aliases.FirstOrDefault() ?? metadata.DisplayName);
        var examples = metadata.RoutingExamples.Length > 0 ? metadata.RoutingExamples : aliases;
        return new InoCapabilityRecord(
            resolvedId,
            string.IsNullOrWhiteSpace(metadata.DisplayName) ? resolvedId : metadata.DisplayName,
            metadata.Description,
            aliases,
            examples,
            resolvedId,
            contract.FullName ?? contract.Name,
            AgentSourceKind,
            SystemTrustLevel,
            metadata.InvocationGrainType,
            metadata.InvocationGrainKey);
    }

    private static NeuronAgentMetadata ReadMetadata(Type contract)
    {
        return new NeuronAgentMetadata(
            ReadStaticString(contract, nameof(IAgent.AgentDisplayName)),
            ReadStaticString(contract, nameof(IAgent.AgentDescription)),
            ReadStaticStringArray(contract, nameof(IAgent.AgentCapabilities)),
            ReadStaticString(contract, nameof(IAgent.AgentInstructions)),
            ReadStaticStringArray(contract, nameof(IAgent.AgentRoutingExamples)),
            ReadStaticString(contract, nameof(IAgent.AgentInvocationGrainType)),
            ReadStaticString(contract, nameof(IAgent.AgentInvocationGrainKey)));
    }

    private static string ReadStaticString(Type type, string propertyName) =>
        ReadStaticProperty(type, propertyName) as string ?? string.Empty;

    private static string[] ReadStaticStringArray(Type type, string propertyName) =>
        ReadStaticProperty(type, propertyName) as string[] ?? [];

    private static object? ReadStaticProperty(Type type, string propertyName)
    {
        var property = type
            .GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
            .FirstOrDefault(prop => string.Equals(prop.Name, propertyName, StringComparison.Ordinal) ||
                                    prop.Name.EndsWith("." + propertyName, StringComparison.Ordinal));
        return property?.GetValue(null);
    }

    private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    internal static string NormalizeId(string value)
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
