using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Tools;
using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Tools.Tests.Unit;

public sealed class ToolCapabilityFacts
{
    [Fact]
    public void CapabilityIdsParse()
    {
        Assert.Equal(CapabilityId.Parse("tool@1"), Capability.Tools.V1);
        Assert.Equal("tool@1", Capability.Tools.V1Id);
        Assert.Equal(Capability.Tools.V1, typeof(ITool).GetCustomAttribute<CapabilityAttribute>()!.Id);
        Assert.Equal("tool", typeof(ITool).GetCustomAttribute<NeuronAliasAttribute>()!.Alias);
    }

    [Fact]
    public void ListIsReadOnly()
    {
        Assert.NotNull(typeof(ITool).GetMethod(nameof(ITool.List))!.GetCustomAttribute<ReadOnlyCallAttribute>());
        Assert.Null(typeof(ITool).GetMethod(nameof(ITool.Run))!.GetCustomAttribute<ReadOnlyCallAttribute>());
    }
}
