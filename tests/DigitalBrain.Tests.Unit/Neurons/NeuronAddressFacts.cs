using DigitalBrain.Brains;

namespace DigitalBrain.Tests.Unit.Neurons;

public sealed class NeuronAddressFacts
{
    [Theory]
    [InlineData("brain-1", null)]
    [InlineData("brain-1/x", "brain-1")]
    [InlineData("brain-1/apps/a", "brain-1")]
    [InlineData("@platform/ledger", null)]
    [InlineData("bad:brain/x", null)]
    [InlineData("/x", null)]
    public void TheBrainOfAnAddressFollowsScopeOf(string key, string? brain)
    {
        Assert.Equal(brain, new NeuronAddress("t", key).Brain?.Value);
        Assert.Equal(brain, NeuronAddress.ScopeOf(key));
    }

    [Fact]
    public void TheStorageKeyIsTheTypeAndTheKey()
        => Assert.Equal("t/brain-1/x", new NeuronAddress("t", "brain-1/x").StorageKey);
}
