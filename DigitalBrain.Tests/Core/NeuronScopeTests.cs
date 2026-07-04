using DigitalBrain.Core;

namespace DigitalBrain.Tests.Core;

public class NeuronScopeTests
{
    [Fact]
    public void TryParse_Without_Slash_Yields_UserId_Only()
    {
        Assert.True(NeuronScope.TryParse("alice", out var scope));
        Assert.Equal("alice", scope.UserId.Value);
        Assert.Null(scope.ThreadId);
        Assert.Equal("alice", scope.ToKey());
    }

    [Fact]
    public void TryParse_With_Slash_Splits_UserId_And_ThreadId()
    {
        Assert.True(NeuronScope.TryParse("alice/thread-1", out var scope));
        Assert.Equal("alice", scope.UserId.Value);
        Assert.Equal("thread-1", scope.ThreadId);
        Assert.Equal("alice/thread-1", scope.ToKey());
    }

    [Fact]
    public void TryParse_Empty_Key_Fails()
    {
        Assert.False(NeuronScope.TryParse("", out _));
        Assert.False(NeuronScope.TryParse(null!, out _));
    }

    [Fact]
    public void AsScope_Extension_Parses_NeuronId()
    {
        var scope = new NeuronId("bob").AsScope();
        Assert.Equal("bob", scope.UserId.Value);
        Assert.Null(scope.ThreadId);
    }

    [Fact]
    public void PackConfigScopes_App_Is_Default_And_ForUser_Prefixes_UserId()
    {
        Assert.Equal("default", PackConfigScopes.App);
        Assert.Equal("user:alice", PackConfigScopes.ForUser(new UserId("alice")));
    }
}
