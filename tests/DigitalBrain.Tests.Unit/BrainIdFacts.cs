namespace DigitalBrain.Tests.Unit;

public sealed class BrainIdFacts
{
    // Samples of the legacy NeuronAddress.IsBrainId rule (os d9615c0d).
    [Theory]
    [InlineData("b1", true)]
    [InlineData("brain-42", true)]
    [InlineData("a.b_c", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("@kernel", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("a\tb", false)]
    public void Accepts_exactly_the_legacy_brain_ids(string value, bool valid)
    {
        Assert.Equal(valid, BrainId.IsValid(value));
        if (valid)
        {
            Assert.Equal(value, new BrainId(value).Value);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new BrainId(value));
        }
    }

    [Fact]
    public void Two_hundred_characters_is_the_limit()
    {
        Assert.True(BrainId.IsValid(new string('a', 200)));
        Assert.False(BrainId.IsValid(new string('a', 201)));
    }

    [Fact]
    public void Kernel_is_a_reserved_system_scope()
    {
        Assert.Equal("@kernel", BrainId.Kernel.Value);
        Assert.True(BrainId.Kernel.IsSystem);
        Assert.Equal(BrainId.Kernel, BrainId.System("kernel"));
        Assert.False(new BrainId("b1").IsSystem);
    }

    [Fact]
    public void System_names_follow_the_brain_id_rule()
    {
        Assert.Throws<ArgumentException>(() => BrainId.System("a/b"));
        Assert.Throws<ArgumentException>(() => BrainId.System("@x"));
    }
}
