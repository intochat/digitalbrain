using DigitalBrain.Identity;

namespace DigitalBrain.Tests.Unit.Identity;

public sealed class CompoundGrainKeyFacts
{
    [Fact]
    public void ConstructWithValidParametersInitializesProperties()
    {
        var brain = new BrainId("alice");
        var key = new CompoundGrainKey(brain, "apps", "calculator");

        Assert.Equal(brain, key.Brain);
        Assert.Equal("apps", key.Type);
        Assert.Equal("calculator", key.Key);
        Assert.Equal("alice/apps/calculator", key.ToString());
    }

    [Fact]
    public void CreateStaticFactoryConstructsValidKey()
    {
        var key = CompoundGrainKey.Create("bob", "storage", "docs/file.txt");

        Assert.Equal(new BrainId("bob"), key.Brain);
        Assert.Equal("storage", key.Type);
        Assert.Equal("docs/file.txt", key.Key);
        Assert.Equal("bob/storage/docs/file.txt", key.ToString());
    }

    [Theory]
    [InlineData("alice/apps/calculator", "alice", "apps", "calculator")]
    [InlineData("bob/files/work/project/code.cs", "bob", "files", "work/project/code.cs")]
    [InlineData("@system/registry/settings", "@system", "registry", "settings")]
    [InlineData("charlie-123/neuron/sub/key/path", "charlie-123", "neuron", "sub/key/path")]
    [InlineData("user-99/data/key:with:colons", "user-99", "data", "key:with:colons")]
    public void ParseValidStringProducesCorrectCompoundKey(string raw, string expectedBrain, string expectedType, string expectedKey)
    {
        var compound = CompoundGrainKey.Parse(raw);

        Assert.Equal(expectedBrain, compound.Brain.Value);
        Assert.Equal(expectedType, compound.Type);
        Assert.Equal(expectedKey, compound.Key);
        Assert.Equal(raw, compound.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alice")]
    [InlineData("alice/")]
    [InlineData("alice/apps")]
    [InlineData("alice/apps/")]
    [InlineData("/apps/calc")]
    [InlineData("INVALID_BRAIN!/apps/calc")]
    [InlineData("alice//calc")]
    [InlineData("alice/type with slash//calc")]
    public void TryParseInvalidReturnsFalse(string? raw)
    {
        var success = CompoundGrainKey.TryParse(raw, out var key);

        Assert.False(success);
        Assert.Equal(default, key);
    }

    [Fact]
    public void ParseInvalidStringThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => CompoundGrainKey.Parse("invalid-no-slashes"));
        Assert.Throws<FormatException>(() => CompoundGrainKey.Parse(""));
        Assert.Throws<FormatException>(() => CompoundGrainKey.Parse("alice/onlyone"));
    }

    [Fact]
    public void RoundTripToNeuronIdAndBackPreservesIdentity()
    {
        var original = CompoundGrainKey.Create("dave", "cache", "l1");
        var neuronId = original.ToNeuronId();

        Assert.Equal(new BrainId("dave"), neuronId.Brain);
        Assert.Equal("cache", neuronId.Type);
        Assert.Equal("l1", neuronId.Key);

        var converted = CompoundGrainKey.FromNeuronId(neuronId);
        Assert.Equal(original, converted);
        Assert.Equal(original.ToString(), neuronId.ToString());
    }

    [Fact]
    public void SlashesInsideKeyArePermittedAndPreserved()
    {
        var composite = CompoundGrainKey.Parse("brain-1/vfs/work/notes/2026/11/plan.md");

        Assert.Equal("brain-1", composite.Brain.Value);
        Assert.Equal("vfs", composite.Type);
        Assert.Equal("work/notes/2026/11/plan.md", composite.Key);
        Assert.Equal("brain-1/vfs/work/notes/2026/11/plan.md", composite.ToString());
    }

    [Fact]
    public void TypeCannotContainSlashesOrBeWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new CompoundGrainKey(new BrainId("alice"), "app/nested", "calc"));
        Assert.Throws<ArgumentException>(() => new CompoundGrainKey(new BrainId("alice"), "   ", "calc"));
        Assert.Throws<ArgumentException>(() => new CompoundGrainKey(new BrainId("alice"), "apps", ""));
    }

    [Fact]
    public void EqualityAndHashCodeConformToValueSemantics()
    {
        var a = CompoundGrainKey.Create("alice", "apps", "calc");
        var b = CompoundGrainKey.Create("alice", "apps", "calc");
        var c = CompoundGrainKey.Create("alice", "apps", "other");
        var d = CompoundGrainKey.Create("bob", "apps", "calc");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(a, c);
        Assert.True(a != c);

        Assert.NotEqual(a, d);
        Assert.True(a != d);
    }

    [Fact]
    public void DeconstructProvidesBrainTypeAndKey()
    {
        var key = CompoundGrainKey.Create("alice", "apps", "calc");
        var (brain, type, name) = key;

        Assert.Equal(new BrainId("alice"), brain);
        Assert.Equal("apps", type);
        Assert.Equal("calc", name);
    }
}
