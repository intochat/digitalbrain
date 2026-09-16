using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClickHouseHostingFacts
{
    [Fact]
    public void Short_module_name_and_server_name_preserve_the_existing_data_volume()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var previous = builder.AddClickHouse("clickhouse").WithDataVolume();
        var originalVolume = Assert.Single(previous.Resource.Annotations.OfType<ContainerMountAnnotation>()).Source;
        builder.Resources.Remove(previous.Resource);

        var brain = builder.AddDigitalBrain("brain").AddModule<ClickHouseModule>(module => module.WithClickHouse());
        var group = brain.GetModuleResource<ClickHouseModule>().Resource;
        Assert.Equal("clickhouse", group.Name);
        var server = Assert.Single(builder.Resources.OfType<ClickHouseServerResource>());
        Assert.Equal("clickhouse-server", server.Name);
        Assert.Equal(originalVolume, Assert.Single(server.Annotations.OfType<ContainerMountAnnotation>()).Source);
        Assert.Contains(server.Annotations.OfType<ResourceRelationshipAnnotation>(), relation => relation.Type == "Parent" && relation.Resource == group);
    }
}
