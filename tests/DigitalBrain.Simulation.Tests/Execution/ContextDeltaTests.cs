using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Execution;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class ContextDeltaTests
{
    [Fact]
    public void ContextDelta_requires_path_and_schema_hash()
    {
        var delta = new ContextDelta(
            new ContextPath("gmail.search"),
            SchemaHash: "abc",
            PayloadJson: """{"messages":[]}""",
            BlobRef: null);
        Assert.Equal("gmail.search", delta.Path.Value);
    }
}
