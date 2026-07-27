using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Identity;

public sealed class GrainKeyEncoding
{
    public static TheoryData<string> RejectedIdentityParts { get; } =
        new("", "a b", "a/b");

    [Theory]
    [MemberData(nameof(RejectedIdentityParts))]
    public void OwnerIdRejectsPartsThatWouldBreakGrainKeyEncoding(string value)
        => Assert.Throws<ArgumentException>(() => new OwnerId(value));

    [Theory]
    [MemberData(nameof(RejectedIdentityParts))]
    public void NeuronNameRejectsPartsThatWouldBreakGrainKeyEncoding(string name)
        => Assert.Throws<ArgumentException>(
            () => new NeuronId(nameof(NeuronId), new OwnerId(nameof(OwnerId)), name));

    [Fact]
    public void OwnerIdRejectsNull()
        => Assert.Throws<ArgumentNullException>(() => new OwnerId(null!));

    [Fact]
    public void GrainKeyRoundTripsThroughOwnerAndName()
    {
        var original = new NeuronId(
            nameof(NeuronId),
            new OwnerId(nameof(OwnerId)),
            nameof(NeuronId.Name));

        Assert.Equal(original, NeuronId.FromGrainKey(original.Type, original.GrainKey));
    }

    [Fact]
    public void DistinctOwnersCannotProduceTheSameGrainKey()
    {
        var left = new NeuronId(nameof(NeuronId), new OwnerId("a"), "b-c");
        var right = new NeuronId(nameof(NeuronId), new OwnerId("a-b"), "c");

        Assert.NotEqual(left.GrainKey, right.GrainKey);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("/x")]
    [InlineData("x/")]
    public void FromGrainKeyRejectsMalformedKeys(string grainKey)
        => Assert.Throws<ArgumentException>(
            () => NeuronId.FromGrainKey(nameof(NeuronId), grainKey));
}
