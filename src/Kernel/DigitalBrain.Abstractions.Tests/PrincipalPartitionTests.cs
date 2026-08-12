using DigitalBrain.Abstractions;

namespace DigitalBrain.Abstractions.Tests;

public sealed class PrincipalPartitionTests
{
    [Fact]
    public void InstanceName_and_TryParse_roundtrip()
    {
        var principal = new PrincipalId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var name = PrincipalPartition.InstanceName(principal, "sales");

        Assert.True(PrincipalPartition.TryParse(name, out var parsed, out var local));
        Assert.Equal(principal, parsed);
        Assert.Equal("sales", local);
        Assert.True(PrincipalPartition.OwnsInstance(principal, name));
    }

    [Fact]
    public void InstanceName_rejects_slash_and_whitespace()
    {
        var principal = new PrincipalId(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => PrincipalPartition.InstanceName(principal, "a/b"));
        Assert.Throws<ArgumentException>(() => PrincipalPartition.InstanceName(principal, "a b"));
    }

    [Fact]
    public void TryParse_rejects_owner_slash_keys_and_empty_local()
    {
        Assert.False(PrincipalPartition.TryParse("owner/main", out _, out _));
        Assert.False(PrincipalPartition.TryParse($"{Guid.NewGuid():N}.", out _, out _));
        Assert.False(PrincipalPartition.TryParse("not-a-guid.main", out _, out _));
        Assert.False(PrincipalPartition.TryParse("", out _, out _));
    }

    [Fact]
    public void OwnsInstance_false_for_other_principal()
    {
        var alice = new PrincipalId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var bob = new PrincipalId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var name = PrincipalPartition.InstanceName(alice, "chart");
        Assert.False(PrincipalPartition.OwnsInstance(bob, name));
    }
}
