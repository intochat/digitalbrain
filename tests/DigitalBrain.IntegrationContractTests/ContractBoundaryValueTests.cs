using System.Text.Json;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class ContractBoundaryValueTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Mailbox_page_size_is_bounded(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GmailMailboxReadRequest(limit));
    }

    [Fact]
    public void Provider_identifiers_are_required_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => new GmailMessageReadRequest(string.Empty));
        Assert.Throws<ArgumentException>(() => new GmailMessageReadRequest(new string('m', 513)));
        Assert.Throws<ArgumentException>(() => new SalesforceRecordReference("Account", "not-an-id"));
        Assert.Throws<ArgumentException>(() => new SalesforceRecordReference(new string('o', 256), "001000000000000AAA"));
    }

    [Fact]
    public void Requested_collections_are_bounded_and_copied()
    {
        var fields = Enumerable.Range(0, 101).Select(static index => $"Field{index}").ToArray();
        var reference = new SalesforceRecordReference("Account", "001000000000000AAA");
        Assert.Throws<ArgumentException>(() => new SalesforceRecordReadRequest(reference, fields));

        var messages = new List<GmailMessageSummary>
        {
            new("message-1", null, DateTimeOffset.UnixEpoch, null, null)
        };
        var page = new GmailMailboxPage(messages);
        messages.Clear();
        Assert.Single(page.Messages);
    }

    [Fact]
    public void Effect_content_and_operation_keys_are_bounded()
    {
        Assert.Throws<ArgumentException>(() => new GmailSendProposalRequest(
            "person@example.com",
            "Subject",
            new string('b', 100_001),
            "operation-1"));
        Assert.Throws<ArgumentException>(() => new GmailSendProposalRequest(
            "person@example.com",
            "Subject",
            "Body",
            new string('k', 257)));
    }

    [Fact]
    public void Salesforce_json_is_bounded_and_detached_from_its_document()
    {
        var reference = new SalesforceRecordReference("Account", "001000000000000AAA");
        using var largeDocument = JsonDocument.Parse($"\"{new string('v', 65_537)}\"");
        Assert.Throws<ArgumentException>(() => new SalesforceUpdateProposalRequest(
            reference,
            "Description",
            largeDocument.RootElement,
            "operation-1"));

        SalesforceUpdateProposalRequest proposal;
        using (var document = JsonDocument.Parse("{\"value\":42}"))
        {
            proposal = new SalesforceUpdateProposalRequest(
                reference,
                "Description",
                document.RootElement,
                "operation-1");
        }

        Assert.Equal(42, proposal.NewValue.GetProperty("value").GetInt32());
    }
}
