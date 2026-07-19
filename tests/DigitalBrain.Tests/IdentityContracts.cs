using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IdentityContracts
{
    public static TheoryData<string> RejectedIdentityParts { get; } = new("", "   ", "with space", "with/separator", "\ttab");

    [Theory]
    [MemberData(nameof(RejectedIdentityParts))]
    public void OwnerIdRejectsPartsThatWouldBreakGrainKeyEncoding(string value)
        => Assert.Throws<ArgumentException>(() => new OwnerId(value));

    [Theory]
    [MemberData(nameof(RejectedIdentityParts))]
    public void NeuronNameRejectsPartsThatWouldBreakGrainKeyEncoding(string name)
        => Assert.Throws<ArgumentException>(() => new NeuronId("Echo", new OwnerId("acme"), name));

    [Fact]
    public void OwnerIdRejectsNull() => Assert.Throws<ArgumentNullException>(() => new OwnerId(null!));

    [Fact]
    public void GrainKeyRoundTripsThroughOwnerAndName()
    {
        var original = new NeuronId("Echo", new OwnerId("acme"), "first");

        var restored = NeuronId.FromGrainKey(original.Type, original.GrainKey);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void DistinctOwnersCannotProduceTheSameGrainKey()
    {
        var left = new NeuronId("Echo", new OwnerId("a"), "b-c");
        var right = new NeuronId("Echo", new OwnerId("a-b"), "c");

        Assert.NotEqual(left.GrainKey, right.GrainKey);
    }

    [Theory]
    [InlineData("no-separator")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    public void FromGrainKeyRejectsMalformedKeys(string grainKey)
        => Assert.Throws<ArgumentException>(() => NeuronId.FromGrainKey("Echo", grainKey));
}
