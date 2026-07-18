using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.OrleansTests.Features;

namespace DigitalBrain.OrleansTests.Capabilities;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class CapabilityCatalogProjectionTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task Projection_is_owner_independent_read_only_and_returns_detached_catalog_values()
    {
        var singleton = fixture.Cluster.Client.GetGrain<ICapabilityCatalogProjectionGrain>(0);
        var unrelatedKey = fixture.Cluster.Client.GetGrain<ICapabilityCatalogProjectionGrain>(91);

        var first = await singleton.ReadAsync();
        var second = await unrelatedKey.ReadAsync();

        var firstDescriptor = Assert.Single(first);
        var secondDescriptor = Assert.Single(second);
        Assert.Equal(firstDescriptor.Id, secondDescriptor.Id);
        Assert.Equal(firstDescriptor.Version, secondDescriptor.Version);
        Assert.Equal(firstDescriptor.Name, secondDescriptor.Name);
        Assert.Equal(firstDescriptor.Description, secondDescriptor.Description);
        Assert.Equal(firstDescriptor.Examples, secondDescriptor.Examples);
        Assert.Equal(firstDescriptor.RequiredGrants, secondDescriptor.RequiredGrants);
        Assert.Equal(firstDescriptor.RequiredConnections, secondDescriptor.RequiredConnections);
        Assert.Equal(firstDescriptor.Origin, secondDescriptor.Origin);
        Assert.Equal(firstDescriptor.Kind, secondDescriptor.Kind);
        Assert.Equal(firstDescriptor.Available, secondDescriptor.Available);
        Assert.Equal("capability.read", firstDescriptor.Id);
        first[0].RequiredConnections[0] = "tampered";
        var reread = await singleton.ReadAsync();
        Assert.Equal("google", Assert.Single(reread).RequiredConnections[0]);
    }

    [Fact]
    public async Task Projection_fails_closed_when_the_catalog_exceeds_its_read_bound()
    {
        var catalog = new StaticProjectionCatalog(Enumerable.Range(0, 257).Select(index =>
            Descriptor($"capability.{index}")));
        var grain = new CapabilityCatalogProjectionGrain(catalog);

        await Assert.ThrowsAsync<InvalidDataException>(() => grain.ReadAsync());
    }

    [Fact]
    public async Task Projection_preserves_detached_external_effect_tool_grants_with_canonical_case()
    {
        var catalog = new StaticProjectionCatalog([
            Descriptor(grants: ["SalesforceTools.UpdateRecord", "GmailTools.Send"])
        ]);
        var grain = new CapabilityCatalogProjectionGrain(catalog);

        var projected = await grain.ReadAsync();

        Assert.Equal(
            ["SalesforceTools.UpdateRecord", "GmailTools.Send"],
            Assert.Single(projected).RequiredGrants);
        projected[0].RequiredGrants[0] = "tampered";
        Assert.Equal(
            ["SalesforceTools.UpdateRecord", "GmailTools.Send"],
            Assert.Single(await grain.ReadAsync()).RequiredGrants);
    }

    [Fact]
    public async Task Projection_rejects_duplicate_or_oversized_required_grant_sets()
    {
        string[][] invalid =
        [
            ["GmailTools.Send", "GmailTools.Send"],
            Enumerable.Range(0, 33).Select(index => $"Tool{index}.Send").ToArray()
        ];

        foreach (var grants in invalid)
        {
            var grain = new CapabilityCatalogProjectionGrain(
                new StaticProjectionCatalog([Descriptor(grants: grants)]));
            await Assert.ThrowsAsync<InvalidDataException>(() => grain.ReadAsync());
        }
    }

    internal static CapabilityDescriptor Descriptor(
        string id = "capability.read",
        int version = 7,
        string[]? connections = null,
        string[]? grants = null,
        bool available = true) =>
        new(
            id,
            version,
            "Read",
            "Reads a bounded value.",
            ["Read the value."],
            grants ?? [],
            connections ?? ["google"],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            available);

    internal sealed class StaticProjectionCatalog(IEnumerable<CapabilityDescriptor> descriptors) : ICapabilityCatalog
    {
        private readonly CapabilityDescriptor[] _descriptors = descriptors.ToArray();
        public IReadOnlyList<CapabilityDescriptor> Snapshot() => _descriptors;
    }
}
