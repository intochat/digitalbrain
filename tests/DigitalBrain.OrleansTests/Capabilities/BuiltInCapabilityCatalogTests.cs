using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class BuiltInCapabilityCatalogTests
{
    [Fact]
    public void Snapshot_has_unique_stable_ids_and_complete_typed_bindings()
    {
        var catalog = new BuiltInCapabilityCatalog([new GoogleCapabilityDescriptorSource(), new SalesforceCapabilityDescriptorSource()]);

        var descriptors = catalog.Snapshot();

        Assert.NotEmpty(descriptors);
        Assert.Equal(descriptors.Count, descriptors.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(descriptors, descriptor =>
        {
            Assert.Matches("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", descriptor.Id);
            Assert.NotEmpty(descriptor.Examples);
            Assert.True(descriptor.Version > 0);
            Assert.True(catalog.TryBind(descriptor.Id, out _));
        });
        Assert.Contains(descriptors, x => x.Id == GoogleCapabilityIds.GmailMessageRead);
        Assert.Contains(descriptors, x => x.Id == SalesforceCapabilityIds.RecordRead);
        Assert.Contains(descriptors, x => x.Id == "assistant.answer");
    }
}
