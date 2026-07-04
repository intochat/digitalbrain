using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

public interface ISalesforceAuthNeuron : INeuron, IHandle<Signal>
{
    Task<SalesforceOAuthCallbackResult> CompleteOAuthAsync(SalesforceOAuthCallback callback);
}
