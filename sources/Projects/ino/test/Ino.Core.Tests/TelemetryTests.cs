using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Tests;

public class TelemetryTests
{
    [Fact]
    public void Fire_span_name_includes_synapse_type_full_name()
    {
        Assert.Equal("fire System.String", Telemetry.Spans.Fire(typeof(string)));
    }

    [Fact]
    public void Handle_span_name_includes_synapse_type_full_name()
    {
        Assert.Equal("handle System.Int32", Telemetry.Spans.Handle(typeof(int)));
    }

    [Fact]
    public void Tag_keys_use_ino_namespace()
    {
        Assert.StartsWith("ino.", Telemetry.Tags.SynapseType);
        Assert.StartsWith("ino.", Telemetry.Tags.SourceDomain);
        Assert.StartsWith("ino.", Telemetry.Tags.TargetDomain);
        Assert.StartsWith("ino.", Telemetry.Tags.CorrelationId);
    }
}
