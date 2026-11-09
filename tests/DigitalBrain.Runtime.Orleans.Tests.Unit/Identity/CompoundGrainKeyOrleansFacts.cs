using DigitalBrain;
using DigitalBrain.Identity;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Identity;

public sealed class CompoundGrainKeyOrleansFacts
{
    [Fact]
    public void ParseIdSpanProducesCorrectCompoundKey()
    {
        var span = IdSpan.Create("alice/service/main");
        var key = CompoundGrainKeyOrleansExtensions.Parse(span);

        Assert.Equal(new BrainId("alice"), key.Brain);
        Assert.Equal("service", key.Type);
        Assert.Equal("main", key.Key);
    }

    [Fact]
    public void RoundTripToIdSpanAndBackPreservesIdentity()
    {
        var original = CompoundGrainKey.Create("user-alpha", "channels", "general/topic");
        var span = original.ToIdSpan();

        Assert.Equal("user-alpha/channels/general/topic", span.ToString());

        var parsed = span.ToCompoundGrainKey();
        Assert.Equal(original, parsed);
        Assert.True(CompoundGrainKeyOrleansExtensions.TryParse(span, out var tryParsed));
        Assert.Equal(original, tryParsed);
    }
}
