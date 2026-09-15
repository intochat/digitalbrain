using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Behaviors;

[Alias("behavior")]
public interface IBehavior : INeuron
{
    /// <summary>Save a validated definition while stopped. Command acceptance precedes durable application; read status after work completes.</summary>
    [Alias("save")]
    Task<Accepted<BehaviorVersion>> Save(SaveBehavior command);
    /// <summary>Start the saved behavior. Connections and owned processors are restored using the existing durable runtime.</summary>
    [Alias("start")]
    Task<Accepted<BehaviorVersion>> Start(ChangeBehavior command);
    /// <summary>Stop the behavior, removing its connections and disabling its owned processors without changing shared resources.</summary>
    [Alias("stop")]
    Task<Accepted<BehaviorVersion>> Stop(ChangeBehavior command);
    [ReadOnly, Alias("read")]
    Task<BehaviorSnapshot> Read();
    [ReadOnly, Alias("validate")]
    Task<BehaviorValidation> Validate(BehaviorDefinition definition);
    /// <summary>Discover registered composition capabilities, ownership policies and configuration documentation.</summary>
    [ReadOnly, Alias("catalog")]
    Task<IReadOnlyList<BehaviorCapability>> Catalog();
    /// <summary>List saved behaviors, including stopped definitions.</summary>
    [ReadOnly, Alias("list")]
    Task<IReadOnlyList<NeuronId>> List();
    /// <summary>Read processor failures and uncertain action identities without retrying them.</summary>
    [ReadOnly, Alias("diagnostics")]
    Task<IReadOnlyList<BehaviorDiagnostic>> Diagnostics();
}

[Alias("db.v2.behavior-directory")]
public interface IBehaviorDirectory : IGrainWithStringKey
{
    [Alias(nameof(Register))] Task Register(NeuronId behavior);
    [Alias(nameof(List))] Task<IReadOnlyList<NeuronId>> List();
}

// Internal lifecycle protocol: these operations are not general assistant tools.
[Alias("db.v2.behavior-node-protocol")]
public interface IBehaviorNode : IGrainWithStringKey
{
    [Alias(nameof(Configure))] Task Configure(BehaviorNodeActivation activation);
    [Alias(nameof(Status))] Task<BehaviorNodeStatus> Status();
}
