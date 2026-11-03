using DigitalBrain.Brains;

namespace DigitalBrain.Tests.Unit.Brains;

public sealed class BrainScopeFacts
{
    [Fact]
    public void BrainScopeCreateYieldsAValidId()
    {
        var scope = BrainScope.Create("ada-account", "ada-brain");
        Assert.Equal("ada-brain", scope.Id);
        Assert.Equal("ada-account", scope.Owner);
        Assert.True(BrainScope.IsValidId(scope.Id));
        Assert.False(BrainScope.IsValidId("invalid/brain"));
        Assert.False(BrainScope.IsValidId("@platform"));
    }

    [Fact]
    public void BrainKeyBrainOfReturnsThePrefix()
    {
        Assert.Equal("ada-brain", BrainKey.BrainOf("ada-brain/connection/gmail/1"));
        Assert.Equal("ada-brain", BrainKey.BrainOf("ada-brain"));
        Assert.True(BrainKey.BelongsTo("ada-brain/connection/gmail/1", "ada-brain"));
        Assert.False(BrainKey.BelongsTo("ada-brain-other/connection/gmail/1", "ada-brain"));
        Assert.Equal("ada-brain", BrainKey.ScopeOf("ada-brain/connection/gmail/1"));
        Assert.Null(BrainKey.ScopeOf("ada-brain"));
    }
}
