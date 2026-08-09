namespace DigitalBrain.Kernel;

internal sealed record SendMessageRequest(string Text);

internal sealed record OpenSceneRequest(string SceneKey, string Title);

internal sealed record ActivateControlRequest(string Intent, string? SceneKey = null);

internal sealed record ChatTurnEvent(
    long Sequence,
    bool FromUser,
    string Text,
    string CommandId,
    string Synapse,
    string NeuronId,
    string Caller,
    string CorrelationId,
    DateTimeOffset Timestamp);

internal sealed record SceneOpenedEvent(
    long Sequence,
    string SceneKey,
    string Title,
    string CommandId,
    string Shell);

internal sealed record AuthorizationEvent(
    long Sequence,
    string Kind,
    string CommandId,
    string ServerKey,
    string? ServerDisplayName,
    string? SignInUrl,
    string State,
    DateTimeOffset Timestamp);

internal sealed record BrainTopologySnapshot(
    IReadOnlyList<BrainModule> Modules,
    IReadOnlyList<BrainNeuron> Neurons,
    DateTimeOffset ObservedAt);

internal sealed record BrainModule(string Id);

internal sealed record BrainNeuron(string Id, string GrainType, string Identity, string Placement);
