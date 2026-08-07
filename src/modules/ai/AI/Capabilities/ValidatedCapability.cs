using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

public sealed class ValidatedCapability
{
    public ValidatedCapability(
        string kind,
        string toolName,
        string contractId,
        int schemaVersion,
        string neuronContractId,
        string defaultInstanceName,
        string description,
        string jsonSchema,
        IReadOnlyList<string> examples,
        string? moduleId = null,
        string? behaviorId = null,
        string? artifactHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(neuronContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultInstanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);
        ArgumentNullException.ThrowIfNull(examples);

        Kind = kind;
        ToolName = toolName;
        ContractId = contractId;
        SchemaVersion = schemaVersion;
        NeuronContractId = neuronContractId;
        DefaultInstanceName = defaultInstanceName;
        Description = description;
        JsonSchema = jsonSchema;
        Examples = examples;
        ModuleId = moduleId;
        BehaviorId = behaviorId;
        ArtifactHash = artifactHash;
    }

    public string Kind { get; }

    public string ToolName { get; }

    public string ContractId { get; }

    public int SchemaVersion { get; }

    public string NeuronContractId { get; }

    public string DefaultInstanceName { get; }

    public string Description { get; }

    public string JsonSchema { get; }

    public IReadOnlyList<string> Examples { get; }

    public string? ModuleId { get; }

    public string? BehaviorId { get; }

    public string? ArtifactHash { get; }

    public static string ToolNameFor(string contractId, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);

        var chars = contractId.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            var ch = chars[index];
            if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')
            {
                continue;
            }

            chars[index] = '_';
        }

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{new string(chars)}_v{schemaVersion}");
    }
}
