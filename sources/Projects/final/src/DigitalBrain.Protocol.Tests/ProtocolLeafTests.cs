using System.Linq;
using System.Reflection;
using DigitalBrain.Protocol;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using Orleans; // [GenerateSerializer], [Id]
using Orleans.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Protocol.Tests;

// A minimal synapse defined IN the test asm proves the base type + metadata
// stamping are fully available from Protocol alone. [GenerateSerializer] +
// the Orleans.Sdk ref make it round-trippable in this assembly.
[GenerateSerializer]
public sealed record Ping([property: Id(0)] string Text) : Synapse;

public class ProtocolLeafTests
{
    [Fact]
    public void Protocol_assembly_does_not_reference_Core_or_Sdk_assemblies()
    {
        var protocolAsm = typeof(Synapse).Assembly;
        Assert.Equal("DigitalBrain.Protocol", protocolAsm.GetName().Name);

        var referenced = protocolAsm.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("DigitalBrain.Core", referenced);
        Assert.DoesNotContain("DigitalBrain.Hosting", referenced);
    }

    [Fact]
    public void Synapse_stamp_threads_correlation_and_caller()
    {
        var firing = new NeuronId("DigitalBrain.Core.INeuron", "ping-1");
        var stamped = new Ping("hi").Stamp(firing);

        Assert.NotEqual(default, stamped.CorrelationId);
        Assert.Equal(firing.Type, stamped.Metadata.Caller.Type);
        Assert.Equal(BrainScope.LocalPrivate, stamped.Scope);
    }

    [Fact]
    public void Synapse_round_trips_through_orleans_serializer()
    {
        var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();

        var original = new Ping("payload").Stamp(new NeuronId("t", "k"));
        var bytes = serializer.SerializeToArray((Synapse)original);
        var restored = (Ping)serializer.Deserialize<Synapse>(bytes);

        Assert.Equal("payload", restored.Text);
        Assert.Equal(original.CorrelationId, restored.CorrelationId);
    }
}
