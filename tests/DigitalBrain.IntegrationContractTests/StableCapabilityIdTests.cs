using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class StableCapabilityIdTests
{
    [Fact]
    public void Capability_ids_are_explicit_unique_and_versioned()
    {
        string[] ids =
        [
            GoogleCapabilityIds.GmailMessageRead,
            GoogleCapabilityIds.GmailMailboxRead,
            GoogleCapabilityIds.GmailSendPropose,
            SalesforceCapabilityIds.RecordRead,
            SalesforceCapabilityIds.RecordUpdatePropose
        ];

        Assert.Equal(
            [
                "google.gmail.message.read.v1",
                "google.gmail.mailbox.read.v1",
                "google.gmail.send.propose.v1",
                "salesforce.record.read.v1",
                "salesforce.record.update.propose.v1"
            ],
            ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, static id => Assert.EndsWith(".v1", id, StringComparison.Ordinal));
    }
}
