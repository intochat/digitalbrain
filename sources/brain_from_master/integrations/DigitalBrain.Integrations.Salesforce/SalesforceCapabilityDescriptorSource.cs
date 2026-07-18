using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;
namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceCapabilityDescriptorSource : ICapabilityDescriptorSource
{
    private const string SalesforceConnection = "salesforce";
    public IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
    [
        new CapabilityDescriptor(
            SalesforceCapabilityIds.RecordRead,
            1,
            "Read a Salesforce record",
            "Reads a single Salesforce record and its fields.",
            ["What is the current stage of the Acme opportunity?", "Show me the contact details for Jane Doe in Salesforce."],
            [],
            [SalesforceConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            true),
        new CapabilityDescriptor(
            SalesforceCapabilityIds.AccountSearch,
            1,
            "Search Salesforce accounts",
            "Finds Salesforce accounts whose names contain the requested company name.",
            ["Find the Salesforce account for Northstar Robotics.", "Search Salesforce accounts for Acme."],
            [],
            [SalesforceConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            true),
        new CapabilityDescriptor(
            SalesforceCapabilityIds.RecordUpdatePropose,
            1,
            "Propose a Salesforce record update",
            "Prepares a change to one Salesforce record field as a proposal that the user must approve before it is applied.",
            ["Move the Acme opportunity to Closed Won.", "Update the phone number on the Globex account."],
            [SalesforceTools.UpdateRecord],
            [SalesforceConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.ExternalEffect,
            true)
    ];
}
