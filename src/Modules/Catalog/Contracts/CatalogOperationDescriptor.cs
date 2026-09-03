using System.Text.Json.Serialization;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.recovery-semantics")]
public enum CatalogRecoverySemantics
{
    ReplaySafe = 0,
    Idempotent = 1,
    Reconcileable = 2,
    NonRecoverable = 3,
}

[GenerateSerializer]
[Alias("db.catalog.capability")]
public sealed record CatalogCapabilityDescriptor
{
    [JsonConstructor]
    public CatalogCapabilityDescriptor(string capabilityId, string version)
    {
        CapabilityId = CatalogContractValidation.Required(capabilityId, nameof(capabilityId));
        Version = CatalogContractValidation.Required(version, nameof(version));
    }

    [Id(0)]
    public string CapabilityId { get; }

    [Id(1)]
    public string Version { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(CapabilityId, nameof(CapabilityId));
        CatalogContractValidation.Required(Version, nameof(Version));
    }
}

[GenerateSerializer]
[Alias("db.catalog.schema")]
public sealed record CatalogSchemaReference
{
    [JsonConstructor]
    public CatalogSchemaReference(string schemaId, string sha256, string canonicalJson, int formatVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(formatVersion);
        SchemaId = CatalogContractValidation.Required(schemaId, nameof(schemaId));
        _ = new CatalogFingerprint(sha256);
        Sha256 = sha256;
        CanonicalJson = CatalogContractValidation.OpaqueRequired(canonicalJson, nameof(canonicalJson));
        FormatVersion = formatVersion;
    }

    [Id(0)]
    public string SchemaId { get; }

    [Id(1)]
    public string Sha256 { get; }

    [Id(2)]
    public string CanonicalJson { get; }

    [Id(3)]
    public int FormatVersion { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(SchemaId, nameof(SchemaId));
        _ = new CatalogFingerprint(Sha256);
        CatalogContractValidation.OpaqueRequired(CanonicalJson, nameof(CanonicalJson));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FormatVersion);
    }
}

[GenerateSerializer]
[Alias("db.catalog.operation")]
public sealed record CatalogOperationDescriptor
{
    [JsonConstructor]
    public CatalogOperationDescriptor(
        string operationId,
        string version,
        string capabilityId,
        string capabilityVersion,
        CatalogSchemaReference input,
        CatalogSchemaReference output,
        CatalogRecoverySemantics recovery,
        string bindingId,
        string bindingRevision,
        IReadOnlyList<string>? requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (!Enum.IsDefined(recovery))
        {
            throw new ArgumentOutOfRangeException(nameof(recovery));
        }

        OperationId = CatalogContractValidation.Required(operationId, nameof(operationId));
        Version = CatalogContractValidation.Required(version, nameof(version));
        CapabilityId = CatalogContractValidation.Required(capabilityId, nameof(capabilityId));
        CapabilityVersion = CatalogContractValidation.Required(capabilityVersion, nameof(capabilityVersion));
        Input = input;
        Output = output;
        Recovery = recovery;
        BindingId = CatalogContractValidation.Required(bindingId, nameof(bindingId));
        BindingRevision = CatalogContractValidation.Required(bindingRevision, nameof(bindingRevision));
        RequiredScopes = CatalogContractValidation.Set(requiredScopes, nameof(requiredScopes));
    }

    [Id(0)] public string OperationId { get; }
    [Id(1)] public string Version { get; }
    [Id(2)] public string CapabilityId { get; }
    [Id(3)] public string CapabilityVersion { get; }
    [Id(4)] public CatalogSchemaReference Input { get; }
    [Id(5)] public CatalogSchemaReference Output { get; }
    [Id(6)] public CatalogRecoverySemantics Recovery { get; }
    [Id(7)] public string BindingId { get; }
    [Id(8)] public string BindingRevision { get; }
    [Id(9)] public IReadOnlyList<string> RequiredScopes { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(OperationId, nameof(OperationId));
        CatalogContractValidation.Required(Version, nameof(Version));
        CatalogContractValidation.Required(CapabilityId, nameof(CapabilityId));
        CatalogContractValidation.Required(CapabilityVersion, nameof(CapabilityVersion));
        ArgumentNullException.ThrowIfNull(Input);
        ArgumentNullException.ThrowIfNull(Output);
        Input.Validate();
        Output.Validate();
        if (!Enum.IsDefined(Recovery))
        {
            throw new ArgumentOutOfRangeException(nameof(Recovery));
        }

        CatalogContractValidation.Required(BindingId, nameof(BindingId));
        CatalogContractValidation.Required(BindingRevision, nameof(BindingRevision));
        _ = CatalogContractValidation.Set(RequiredScopes, nameof(RequiredScopes));
    }
}

[GenerateSerializer]
[Alias("db.catalog.signal-contract")]
public sealed record CatalogSignalDescriptor
{
    [JsonConstructor]
    public CatalogSignalDescriptor(string alias, CatalogSchemaReference schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Alias = CatalogContractValidation.Required(alias, nameof(alias));
        Schema = schema;
    }

    [Id(0)] public string Alias { get; }
    [Id(1)] public CatalogSchemaReference Schema { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(Alias, nameof(Alias));
        ArgumentNullException.ThrowIfNull(Schema);
        Schema.Validate();
    }
}

[GenerateSerializer]
[Alias("db.catalog.signal-reference")]
public sealed record CatalogSignalReference
{
    [JsonConstructor]
    public CatalogSignalReference(string alias, string schemaHash)
    {
        Alias = CatalogContractValidation.Required(alias, nameof(alias));
        _ = new CatalogFingerprint(schemaHash);
        SchemaHash = schemaHash;
    }

    [Id(0)] public string Alias { get; }
    [Id(1)] public string SchemaHash { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(Alias, nameof(Alias));
        _ = new CatalogFingerprint(SchemaHash);
    }
}

[GenerateSerializer]
[Alias("db.catalog.neuron")]
public sealed record CatalogNeuronDescriptor
{
    [JsonConstructor]
    public CatalogNeuronDescriptor(
        string contractAlias,
        string grainType,
        IReadOnlyList<CatalogSignalReference>? handledSignals)
    {
        ContractAlias = CatalogContractValidation.Required(contractAlias, nameof(contractAlias));
        GrainType = CatalogContractValidation.Required(grainType, nameof(grainType));
        var canonicalSignals = handledSignals is null
            ? []
            : handledSignals
                .Select(signal => signal ?? throw new ArgumentException(
                    "Handled signals cannot contain null values.",
                    nameof(handledSignals)))
                .Distinct()
                .OrderBy(static signal => signal.Alias, StringComparer.Ordinal)
                .ThenBy(static signal => signal.SchemaHash, StringComparer.Ordinal)
                .ToArray();
        foreach (var signal in canonicalSignals)
        {
            signal.Validate();
        }

        HandledSignals = CatalogContractValidation.ReadOnlyCopy(canonicalSignals);
    }

    [Id(0)] public string ContractAlias { get; }
    [Id(1)] public string GrainType { get; }
    [Id(2)] public IReadOnlyList<CatalogSignalReference> HandledSignals { get; }

    public void Validate()
    {
        CatalogContractValidation.Required(ContractAlias, nameof(ContractAlias));
        CatalogContractValidation.Required(GrainType, nameof(GrainType));
        foreach (var signal in HandledSignals)
        {
            ArgumentNullException.ThrowIfNull(signal);
            signal.Validate();
        }
    }
}
