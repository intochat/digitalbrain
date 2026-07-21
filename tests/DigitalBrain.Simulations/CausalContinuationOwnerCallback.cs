using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[Alias("db.test.causal-continuation-owner-callback")]
internal interface ICausalContinuationOwnerCallback : IGrainWithStringKey
{
    [Alias("CompleteDeferred")]
    Task CompleteDeferredAsync(SynapseId causation);
}
