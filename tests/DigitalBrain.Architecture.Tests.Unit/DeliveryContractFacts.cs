namespace DigitalBrain.Architecture.Tests.Unit;

// RT-03, 04 D-07: the delivery contract exists before stream code and names every guarantee.
public sealed class DeliveryContractFacts
{
    private static readonly string[] Headings =
    [
        "## Guarantees", "## Payload", "## Ordering and dedupe", "## Catch-up", "## Retention floor and Gap",
        "## Dead subscribers", "## Write-then-crash window", "## Authorization", "## Topologies", "## Stream identity",
    ];

    private static readonly string[] Terms = ["at-least-once", "Gap(", "ISynapseRegistryIndex", "ResumeAsync", "AdmitSynapse"];

    [Fact]
    public void Delivery_contract_names_every_guarantee()
    {
        var path = Path.Combine(Repository.Root, "docs", "delivery-contract.md");
        Assert.True(File.Exists(path), $"{path} is missing.");
        var lines = File.ReadAllLines(path);
        var text = string.Join('\n', lines);

        foreach (var heading in Headings) { Assert.Contains(heading, lines); }
        foreach (var term in Terms) { Assert.Contains(term, text, StringComparison.OrdinalIgnoreCase); }
        Assert.DoesNotContain("legacy", text, StringComparison.OrdinalIgnoreCase);
    }
}
