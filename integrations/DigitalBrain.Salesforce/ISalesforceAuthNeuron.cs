using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

[Alias("DigitalBrain.Salesforce.ISalesforceAuthNeuron")]
public interface ISalesforceAuthNeuron : INeuron, IHandle<Signal>
{
    [Alias("CompleteOAuthAsync")]
    Task<SalesforceOAuthCallbackResult> CompleteOAuthAsync(SalesforceOAuthCallback callback);
}
