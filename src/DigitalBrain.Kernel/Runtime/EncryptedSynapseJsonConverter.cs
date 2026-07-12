using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Kernel;

public sealed class EncryptedSynapseJsonConverter : JsonConverter<Synapse>
{
    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly EncryptedRuntimeStateProtector _protector;
    private readonly string _scopeHash;
    private readonly IReadOnlyDictionary<string, Type> _typesByName;
    private readonly IReadOnlyDictionary<Type, string> _namesByType;
    private long _nextRevision = RandomNumberGenerator.GetInt32(1, int.MaxValue - 1);

    public EncryptedSynapseJsonConverter(
        EncryptedRuntimeStateProtector protector,
        string journalScopeHash,
        IEnumerable<Type> allowedSynapseTypes)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(allowedSynapseTypes);
        RuntimeStateKeys.DemandScopeHash(journalScopeHash);
        var types = allowedSynapseTypes.Append(typeof(Synapse)).Distinct().ToArray();
        if (types.Any(static type => type.IsAbstract || type.ContainsGenericParameters ||
                !typeof(Synapse).IsAssignableFrom(type) || type.FullName is null))
            throw new ArgumentException("Allowed journal types must be closed, concrete Synapse types.", nameof(allowedSynapseTypes));
        var duplicateName = types.GroupBy(static type => type.FullName!, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicateName is not null)
            throw new ArgumentException("Allowed journal Synapse type names must be unique.", nameof(allowedSynapseTypes));
        _protector = protector;
        _scopeHash = journalScopeHash;
        _typesByName = types.ToDictionary(static type => type.FullName!, StringComparer.Ordinal);
        _namesByType = types.ToDictionary(static type => type, static type => type.FullName!);
    }

    public static IReadOnlyList<Type> DiscoverLoadedSynapseTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => assembly.GetName().Name?.StartsWith("DigitalBrain.", StringComparison.Ordinal) == true)
            .SelectMany(GetLoadableTypes)
            .Where(static type => !type.IsAbstract && !type.ContainsGenericParameters && type.FullName is not null &&
                                  typeof(Synapse).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    public override Synapse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var envelope = JsonSerializer.Deserialize<EncryptedRuntimeStateEnvelope>(ref reader, EnvelopeJson)
                       ?? throw new JsonException("Encrypted Synapse envelope is missing.");
        EncryptedSynapsePlaintext plaintext;
        try
        {
            plaintext = _protector.Unprotect<EncryptedSynapsePlaintext>(
                _scopeHash,
                RuntimeStateKinds.SynapseJournal,
                RuntimeStateSchemas.SynapseJournal,
                envelope);
        }
        catch (RuntimeStateIntegrityException exception)
        {
            throw new JsonException("Encrypted Synapse authentication failed.", exception);
        }

        if (string.IsNullOrWhiteSpace(plaintext.TypeName) || plaintext.TypeName.Length > 512 ||
            plaintext.PayloadUtf8 is null || plaintext.PayloadUtf8.Length > 4 * 1024 * 1024)
            throw new JsonException("Encrypted Synapse plaintext metadata is invalid.");

        try
        {
            if (!_typesByName.TryGetValue(plaintext.TypeName, out var runtimeType))
                throw new JsonException("Encrypted Synapse type is not allow-listed.");
            var value = JsonSerializer.Deserialize(plaintext.PayloadUtf8, runtimeType, CreateInnerOptions(options));
            return value as Synapse ?? throw new JsonException("Encrypted Synapse payload has an invalid runtime type.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext.PayloadUtf8);
        }
    }

    public override void Write(Utf8JsonWriter writer, Synapse value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        var runtimeType = value.GetType();
        if (!_namesByType.TryGetValue(runtimeType, out var typeName))
            throw new JsonException("Synapse runtime type is not allow-listed for encrypted journaling.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, runtimeType, CreateInnerOptions(options));
        try
        {
            var revision = Interlocked.Increment(ref _nextRevision);
            if (revision < 1) throw new JsonException("Encrypted Synapse revision space is exhausted.");
            var envelope = _protector.Protect(
                _scopeHash,
                RuntimeStateKinds.SynapseJournal,
                RuntimeStateSchemas.SynapseJournal,
                revision,
                new EncryptedSynapsePlaintext { TypeName = typeName, PayloadUtf8 = payload });
            JsonSerializer.Serialize(writer, envelope, EnvelopeJson);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static JsonSerializerOptions CreateInnerOptions(JsonSerializerOptions outer)
    {
        var inner = new JsonSerializerOptions(outer);
        for (var index = inner.Converters.Count - 1; index >= 0; index--)
        {
            if (inner.Converters[index] is EncryptedSynapseJsonConverter) inner.Converters.RemoveAt(index);
        }
        return inner;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }
}

internal sealed class EncryptedSynapsePlaintext
{
    public string TypeName { get; set; } = string.Empty;
    public byte[] PayloadUtf8 { get; set; } = [];
}
