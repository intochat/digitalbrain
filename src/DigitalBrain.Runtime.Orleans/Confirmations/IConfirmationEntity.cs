using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;

namespace DigitalBrain.Runtime.Orleans.Confirmations;

[NeuronAlias("runtime.confirmations")]
public interface IConfirmationEntity
{
    Task Begin(ConfirmationPlan plan, string origin);
    Task<ConfirmationResult?> Result(string planId);
    Task ResolveAsRequester(string planId, bool approve);
    Task Cancel(string planId);
}
