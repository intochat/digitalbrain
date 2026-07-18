namespace DigitalBrain.AI;

using Brain.Contracts;

[Alias("digitalbrain.ai.IAgent")]
public interface IAgent : IGrainWithStringKey
{
    [Alias("GetIdentityAsync")]
    Task<string> GetIdentityAsync();
}

[Alias("digitalbrain.ai.IGpt56")]
[NeuronContract("agent.gpt56.v1")]
public interface IGpt56 : IAgent;

[Alias("digitalbrain.ai.IGrok45")]
[NeuronContract("agent.grok45.v1")]
public interface IGrok45 : IAgent;

[Alias("digitalbrain.ai.IGroupChat")]
[NeuronContract("chat.group.v1")]
public interface IGroupChat : IGrainWithStringKey
{
    [Alias("StartDiscussionAsync")]
    Task<CommandReceipt> StartDiscussionAsync(CommandSynapse<StartDiscussion> command);

    [Alias("ApplyUiActionAsync")]
    Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command);

    [Alias("GetSurfaceAsync")]
    Task<UiSurfaceSnapshot> GetSurfaceAsync();
}

[GenerateSerializer, Alias("brain.ui-action-request.v1")]
public sealed record UiActionRequest(
    [property: Id(0)] string ActionId,
    [property: Id(1)] long ExpectedRevision);
