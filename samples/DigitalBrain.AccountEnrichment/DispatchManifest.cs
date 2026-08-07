using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.AccountEnrichment.AccountEnrichment", "DigitalBrain.AccountEnrichment.AccountEnriched", false),
        ("DigitalBrain.AccountEnrichment.AccountEnrichment", "DigitalBrain.AccountEnrichment.AccountEnrichmentProposed", false),
        ("DigitalBrain.AccountEnrichment.AccountEnrichment", "DigitalBrain.AccountEnrichment.EnrichAccountFromEmail", true),
        ("DigitalBrain.AccountEnrichment.AccountEnrichment", "DigitalBrain.Google.GmailResponse", true),
        ("DigitalBrain.AccountEnrichment.AccountEnrichment", "DigitalBrain.Salesforce.SalesforceMutationApproval", true),
        ("DigitalBrain.AccountEnrichment.AccountEnrichment", "DigitalBrain.Salesforce.SalesforceResponse", true),
    ];
}
