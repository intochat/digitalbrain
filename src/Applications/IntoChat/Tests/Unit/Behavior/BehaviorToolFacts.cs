using IntoChat;
using Xunit;

namespace IntoChat.Tests;

public sealed class BehaviorToolFacts
{
    [Theory]
    [InlineData("../other")]
    [InlineData("other/program")]
    [InlineData("C:\\private")]
    public void ModelCannotSelectAnExternalScopeOrPath(string id)
        => Assert.Throws<ArgumentException>(() => BehaviorToolScope.Key("workspace-trusted", id));

    [Fact]
    public void WorkspacesReceiveDifferentNeuronKeys()
        => Assert.NotEqual(BehaviorToolScope.Key("workspace-a", "invoice"), BehaviorToolScope.Key("workspace-b", "invoice"));
}