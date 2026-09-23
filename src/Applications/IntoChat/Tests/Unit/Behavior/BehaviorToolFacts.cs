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

    [Fact]
    public void TautologicalTestsAreRejected()
        => Assert.Throws<ArgumentException>(() => BehaviorTestContract.RejectTestsThatNeverReferenceTheBehavior(
            "public sealed class TimerToText { public static string Format(int tick) => tick.ToString(); }",
            "public sealed class Tests { [Xunit.Fact] public void Passes() => Xunit.Assert.True(true); }"));

    [Fact]
    public void TestsThatExerciseTheBehaviorAreAccepted()
        => BehaviorTestContract.RejectTestsThatNeverReferenceTheBehavior(
            "public sealed class TimerToText { public static string Format(int tick) => tick.ToString(); }",
            "public sealed class Tests { [Xunit.Fact] public void Formats() => Xunit.Assert.Equal(\"7\", TimerToText.Format(7)); }");
}