using Orleans;

namespace Brain.FeasibilityTests.TypedReferences;

[AttributeUsage(AttributeTargets.Interface)]
public sealed class NeuronContractAttribute(string contractId) : Attribute
{
    public string ContractId { get; } = contractId;
}

[Alias("typed-ref.IAgent")]
public interface IAgent : IGrainWithStringKey
{
    [Alias("GetIdentityAsync")]
    Task<string> GetIdentityAsync();
}

[Alias("typed-ref.IGpt56")]
[NeuronContract("agent.gpt56.v1")]
public interface IGpt56 : IAgent;

[Alias("typed-ref.IGrok45")]
[NeuronContract("agent.grok45.v1")]
public interface IGrok45 : IAgent;

[Alias("typed-ref.IGroupChat")]
[NeuronContract("chat.group.v1")]
public interface IGroupChat : IGrainWithStringKey
{
    [Alias("SetParticipantsAsync")]
    Task SetParticipantsAsync(IReadOnlyList<IAgent> participants);

    [Alias("GetParticipantsAsync")]
    Task<IReadOnlyList<IAgent>> GetParticipantsAsync();
}
