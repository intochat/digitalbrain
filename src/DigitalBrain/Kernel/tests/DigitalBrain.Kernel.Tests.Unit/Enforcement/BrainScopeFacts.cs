using DigitalBrain.Kernel.Enforcement;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit.Enforcement;

public sealed class BrainScopeFacts
{
    [Fact]
    public void TheDerivationIsStableAndDistinctPerOwnerAndName()
    {
        var id = BrainScope.Create("alice", "one").Id;
        Assert.StartsWith("workspace-", id); // storage artifact, preserved on purpose
        Assert.Equal(id, BrainScope.Create("alice", "one").Id);
        Assert.NotEqual(id, BrainScope.Create("bob", "one").Id);
        Assert.NotEqual(id, BrainScope.Create("alice", "two").Id);
    }

    [Fact]
    public void AnUnstampedCallCannotResolveABrain()
        => Assert.Throws<InvalidOperationException>(() => _ = BrainScope.CurrentId());

    [Fact]
    public void InvalidIdsAreRejected()
    {
        Assert.False(BrainScope.IsValidId(null));
        Assert.False(BrainScope.IsValidId("a/b"));
        Assert.False(BrainScope.IsValidId("a\\b"));
        Assert.False(BrainScope.IsValidId(new string('x', 201)));
        Assert.True(BrainScope.IsValidId("personal"));
    }
}
