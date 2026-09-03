using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.entry-id")]
public readonly record struct CatalogEntryId
{
    [JsonConstructor]
    public CatalogEntryId(string value) => Value = CatalogContractValidation.Required(value, nameof(value));

    [Id(0)]
    public string Value { get; }

    public void Validate() => CatalogContractValidation.Required(Value, nameof(Value));

    public override string ToString() => Value;
}

[GenerateSerializer]
[Alias("db.catalog.fingerprint")]
public readonly record struct CatalogFingerprint
{
    [JsonConstructor]
    public CatalogFingerprint(string value)
    {
        if (value is null || value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A catalog fingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public string Value { get; }

    public void Validate() => _ = new CatalogFingerprint(Value);

    public override string ToString() => Value;
}

[GenerateSerializer]
[Alias("db.catalog.scope-kind")]
public enum CatalogScopeKind
{
    Platform = 0,
    Owner = 1,
}

[GenerateSerializer]
[Alias("db.catalog.scope")]
public sealed record CatalogScope
{
    private static readonly CatalogScope PlatformScope = new(CatalogScopeKind.Platform, null);

    [JsonConstructor]
    public CatalogScope(CatalogScopeKind kind, OwnerId? owner)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == CatalogScopeKind.Platform && owner is not null)
        {
            throw new ArgumentException("A platform catalog scope cannot carry an owner.", nameof(owner));
        }

        if (kind == CatalogScopeKind.Owner)
        {
            CatalogContractValidation.ValidOwner(owner, nameof(owner));
        }

        Kind = kind;
        Owner = owner;
    }

    [Id(0)]
    public CatalogScopeKind Kind { get; }

    [Id(1)]
    public OwnerId? Owner { get; }

    [JsonIgnore]
    public string SortKey => Kind == CatalogScopeKind.Platform
        ? "0:platform"
        : $"1:owner:{Owner!.Value.Value}";

    public static CatalogScope Platform => PlatformScope;

    public static CatalogScope ForOwner(OwnerId owner)
    {
        CatalogContractValidation.ValidOwner(owner, nameof(owner));
        return new CatalogScope(CatalogScopeKind.Owner, owner);
    }

    public void Validate() => _ = new CatalogScope(Kind, Owner);
}

[GenerateSerializer]
[Alias("db.catalog.source-reference")]
public sealed record CatalogSourceReference
{
    [JsonConstructor]
    public CatalogSourceReference(string kind, string id)
    {
        Kind = CatalogContractValidation.Required(kind, nameof(kind));
        Id = CatalogContractValidation.Required(id, nameof(id));
    }

    [Id(0)]
    public string Kind { get; }

    [Id(1)]
    public string Id { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(Kind, nameof(Kind));
        CatalogContractValidation.Required(Id, nameof(Id));
    }
}

[GenerateSerializer]
[Alias("db.catalog.reference")]
public sealed record CatalogReference
{
    [JsonConstructor]
    public CatalogReference(
        CatalogScope scope,
        CatalogSourceReference source,
        CatalogEntryId id,
        string sourceRevision,
        CatalogFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(source);
        scope.Validate();
        source.Validate();
        id.Validate();
        fingerprint.Validate();

        Scope = scope;
        Source = source;
        Id = id;
        SourceRevision = CatalogContractValidation.Required(sourceRevision, nameof(sourceRevision));
        Fingerprint = fingerprint;
    }

    [Id(0)]
    public CatalogScope Scope { get; }

    [Id(1)]
    public CatalogSourceReference Source { get; }

    [Id(2)]
    public CatalogEntryId Id { get; }

    [Id(3)]
    public string SourceRevision { get; }

    [Id(4)]
    public CatalogFingerprint Fingerprint { get; }

    public void Validate()
    {
        Scope.Validate();
        Source.Validate();
        Id.Validate();
        CatalogContractValidation.Required(SourceRevision, nameof(SourceRevision));
        Fingerprint.Validate();
    }
}

internal static class CatalogContractValidation
{
    public static string Required(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();

    public static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? OptionalBounded(string? value, string parameterName, int maxLength)
    {
        var normalized = Optional(value);
        if (normalized?.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    public static string OpaqueRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty opaque value is required.", parameterName);
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("An opaque value cannot contain surrounding whitespace.", parameterName);
        }

        return value;
    }

    public static string? OpaqueOptional(string? value, string parameterName)
        => value is null ? null : OpaqueRequired(value, parameterName);

    public static IReadOnlyList<string> Set(IReadOnlyList<string>? values, string parameterName)
        => Values(values, parameterName, sort: true);

    public static IReadOnlyList<string> Ordered(IReadOnlyList<string>? values, string parameterName)
        => Values(values, parameterName, sort: false);

    public static IReadOnlyList<string> BoundedSet(
        IReadOnlyList<string>? values,
        string parameterName,
        int maxCount,
        int maxItemLength)
    {
        var result = Values(values, parameterName, sort: true);
        if (result.Count > maxCount)
        {
            throw new ArgumentException($"The collection cannot contain more than {maxCount} values.", parameterName);
        }

        if (result.Any(value => value.Length > maxItemLength))
        {
            throw new ArgumentException(
                $"A collection value cannot exceed {maxItemLength} characters.",
                parameterName);
        }

        return result;
    }

    public static IReadOnlyList<T> ReadOnlyCopy<T>(IReadOnlyList<T>? values)
        => Array.AsReadOnly(values?.ToArray() ?? []);

    public static void ValidOwner(OwnerId? owner, string parameterName)
    {
        if (owner is null || string.IsNullOrWhiteSpace(owner.Value.Value))
        {
            throw new ArgumentException("A valid owner is required.", parameterName);
        }
    }

    public static void ValidOwner(OwnerId owner, string parameterName)
        => ValidOwner((OwnerId?)owner, parameterName);

    public static void ValidNeuron(NeuronId neuron, string parameterName)
    {
        ValidOwner(neuron.Owner, parameterName);
        if (string.IsNullOrWhiteSpace(neuron.Type) || string.IsNullOrWhiteSpace(neuron.Name))
        {
            throw new ArgumentException("A valid neuron identity is required.", parameterName);
        }
    }

    public static void ValidEntity(EntityId entity, string parameterName)
    {
        ValidOwner(entity.Owner, parameterName);
        if (string.IsNullOrWhiteSpace(entity.Type) || string.IsNullOrWhiteSpace(entity.Name))
        {
            throw new ArgumentException("A valid entity identity is required.", parameterName);
        }
    }

    private static IReadOnlyList<string> Values(
        IReadOnlyList<string>? values,
        string parameterName,
        bool sort)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var result = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var normalized = Required(value, parameterName);
            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        if (sort)
        {
            result.Sort(StringComparer.Ordinal);
        }

        return Array.AsReadOnly(result.ToArray());
    }
}
