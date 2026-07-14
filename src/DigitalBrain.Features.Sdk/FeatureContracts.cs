using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
namespace DigitalBrain.Features.Sdk;

public interface IFeature
{
    Task HandleAsync(FeatureInput input, IFeatureContext context, CancellationToken cancellationToken = default);
}
public sealed class FeatureInput
{
    public FeatureInput(string inputId, string kind, DateTimeOffset occurredAt, IReadOnlyDictionary<string, string> facts)
    {
        InputId = FeatureContractGuard.Required(inputId, nameof(inputId), 256);
        Kind = FeatureContractGuard.Required(kind, nameof(kind), 128);
        OccurredAt = occurredAt;
        Facts = FeatureContractGuard.Facts(facts, nameof(facts));
    }
    public string InputId { get; }
    public string Kind { get; }
    public DateTimeOffset OccurredAt { get; }
    public IReadOnlyDictionary<string, string> Facts { get; }
}
public sealed class FeatureCapabilityDeniedException : Exception
{
    public FeatureCapabilityDeniedException(string capabilityId)
        : base($"Capability denied: {FeatureContractGuard.Required(capabilityId, nameof(capabilityId), 256)}")
    {
        CapabilityId = capabilityId;
    }
    public string CapabilityId { get; }
}
internal static class FeatureContractGuard
{
    internal static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain 1 to {maximumLength} characters.", parameterName);
        }
        return value;
    }
    internal static string Bounded(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain at most {maximumLength} characters.", parameterName);
        }
        return value;
    }
    internal static IReadOnlyDictionary<string, string> Facts(IReadOnlyDictionary<string, string> facts, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(facts, parameterName);
        if (facts.Count > 32)
        {
            throw new ArgumentException("Facts cannot contain more than 32 entries.", parameterName);
        }
        var copy = new Dictionary<string, string>(facts.Count, StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            copy.Add(Required(fact.Key, parameterName, 128), Utf8(fact.Value, parameterName, 4_096));
        }
        return new ReadOnlyDictionary<string, string>(copy);
    }
    internal static IReadOnlyList<string> Strings(IReadOnlyList<string> values, string parameterName, int maximumCount, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > maximumCount)
        {
            throw new ArgumentException($"Values cannot contain more than {maximumCount} entries.", parameterName);
        }
        var copy = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            copy[index] = Required(values[index], parameterName, maximumLength);
        }
        return new ReadOnlyCollection<string>(copy);
    }
    internal static IReadOnlyList<string> Tags(IReadOnlyList<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > 16)
            throw new ArgumentException("Tags cannot contain more than 16 entries.", parameterName);
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, parameterName);
            var tag = value.Trim().ToLowerInvariant();
            if (tag.Length is 0 or > 128 || ContainsControl(tag))
                throw new ArgumentException("Tags must be bounded non-empty text.", parameterName);
            normalized.Add(tag);
        }
        var copy = new string[normalized.Count];
        normalized.CopyTo(copy);
        return new ReadOnlyCollection<string>(copy);
    }
    internal static string MemoryFactId(string value, string parameterName)
    {
        value = Required(value, parameterName, 256);
        if (string.Equals(value, "!capacity", StringComparison.Ordinal))
            throw new ArgumentException("A reserved Memory fact identifier cannot be used.", parameterName);
        return value;
    }
    private static bool ContainsControl(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
                return true;
        }
        return false;
    }
    internal static string Utf8(string value, string parameterName, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            throw new ArgumentException($"Value must contain at most {maximumBytes} UTF-8 bytes.", parameterName);
        }
        return value;
    }
    internal static string Json(string value, string parameterName, int maximumBytes)
    {
        Utf8(value, parameterName, maximumBytes);
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Value must contain valid JSON.", parameterName, exception);
        }
        return value;
    }
}
