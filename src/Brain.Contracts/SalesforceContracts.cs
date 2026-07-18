namespace DigitalBrain.Salesforce;

using Brain.Contracts;

[Alias("digitalbrain.salesforce.ISalesforce")]
[NeuronContract("salesforce.v1")]
public interface ISalesforce : IGrainWithStringKey
{
    [Alias("GetIdentityAsync")]
    Task<string> GetIdentityAsync();
}
