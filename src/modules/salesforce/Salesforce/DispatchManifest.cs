using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Salesforce.Salesforce", "DigitalBrain.Salesforce.ApproveSalesforceMutation", true),
        ("DigitalBrain.Salesforce.Salesforce", "DigitalBrain.Salesforce.SalesforceRequest", true),
        ("DigitalBrain.Salesforce.Salesforce", "DigitalBrain.Salesforce.SalesforceResponse", false),
    ];
}
