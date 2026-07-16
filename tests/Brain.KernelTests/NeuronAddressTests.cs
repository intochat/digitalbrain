using Brain.Contracts;
using Xunit;
namespace Brain.KernelTests;

public class NeuronAddressTests
{
    [Fact]
    public void Round_trips_through_grain_key()
    {
        var address = new NeuronAddress("local-owner", "actor/dev", "chat/main");
        var parsed = NeuronAddress.Parse(address.ToGrainKey());
        Assert.Equal(address, parsed);
        Assert.Equal("chat", parsed.Kind);
    }

    [Fact]
    public void Rejects_malformed_keys()
    {
        Assert.Throws<ArgumentException>(() => NeuronAddress.Parse("no-separators"));
    }
}
