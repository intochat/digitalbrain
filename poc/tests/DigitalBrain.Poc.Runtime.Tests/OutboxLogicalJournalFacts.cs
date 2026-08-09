using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class OutboxLogicalJournalFacts
{
    [Fact]
    public async Task LogicalJournalKeepsAnUnrelatedSameShapedDeliveryVisible()
    {
        await using var root = PocDataRoot.Create(TestPocRoot.Find());
        var moduleIdentity = new CandidateModuleIdentity(
            new string('a', 64),
            new string('b', 64),
            new string('c', 64));
        await new RunStore(root).TransactAsync(
            document =>
            {
                document.Journal.Add(new JournalEntry("root", "SocialPostObserved", "in"));
                document.Journal.Add(new JournalEntry(
                    "unrelated-delivery",
                    "UnrelatedDelivery",
                    "in"));
                document.Outbox.Add(new OutboxEntry(
                    "unrelated-delivery",
                    "root",
                    0,
                    "UnrelatedDelivery",
                    "db.poc.unrelated.v1",
                    "json",
                    Convert.ToBase64String("{}"u8),
                    "owner-a",
                    "cf_aaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "revision-1",
                    moduleIdentity,
                    null,
                    null,
                    string.Empty,
                    false,
                    "elon-chart"));
                return Task.FromResult((true, true));
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["SocialPostObserved", "UnrelatedDelivery"],
            await new Outbox(root).ReadLogicalJournalKindsAsync(
                TestContext.Current.CancellationToken));
    }
}
