namespace DigitalBrain.Kernel;

public sealed class ActiveBehaviorCapability
{
    public ActiveBehaviorCapability(
        string behaviorId,
        string displayName,
        string description,
        string artifactHash,
        string instanceName,
        string neuronContractId,
        string jsonSchema,
        IReadOnlyList<string> scenarioTitles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(neuronContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);
        ArgumentNullException.ThrowIfNull(scenarioTitles);

        BehaviorId = behaviorId;
        DisplayName = displayName;
        Description = description;
        ArtifactHash = artifactHash;
        InstanceName = instanceName;
        NeuronContractId = neuronContractId;
        JsonSchema = jsonSchema;
        ScenarioTitles = [.. scenarioTitles];
    }

    public string BehaviorId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string ArtifactHash { get; }

    public string InstanceName { get; }

    public string NeuronContractId { get; }

    public string JsonSchema { get; }

    public IReadOnlyList<string> ScenarioTitles { get; }
}
