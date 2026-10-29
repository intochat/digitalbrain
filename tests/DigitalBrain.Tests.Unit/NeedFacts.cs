namespace DigitalBrain.Tests.Unit;

public sealed class NeedFacts
{
    [Fact]
    public void GrainStore_keeps_its_name()
    {
        var need = Need.State("drive");
        Assert.Equal(NeedKind.State, need.Kind);
        Assert.Equal("drive", need.Name);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("Drive")]
    [InlineData("")]
    [InlineData("1drive")]
    [InlineData("a/b")]
    public void GrainStore_rejects_unsafe_names(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Need.State(name));
    }

    [Fact]
    public void GrainStore_rejects_names_over_63_characters()
    {
        Need.State("a" + new string('b', 62));
        Assert.ThrowsAny<ArgumentException>(() => Need.State("a" + new string('b', 63)));
    }

    [Fact]
    public void Other_needs_have_no_name()
    {
        Assert.All(new[] { Need.Streams, Need.Reminders, Need.Secrets, Need.Http, Need.Sandbox, Need.Model }, need => Assert.Null(need.Name));
        Assert.Equal(NeedKind.Streams, Need.Streams.Kind);
        Assert.Equal(NeedKind.Reminders, Need.Reminders.Kind);
        Assert.Equal(NeedKind.Secrets, Need.Secrets.Kind);
        Assert.Equal(NeedKind.Http, Need.Http.Kind);
        Assert.Equal(NeedKind.Sandbox, Need.Sandbox.Kind);
        Assert.Equal(NeedKind.Model, Need.Model.Kind);
    }

    [Fact]
    public void NeedKind_has_exactly_seven_members()
    {
        Assert.Equal(
            ["State", "Streams", "Reminders", "Secrets", "Http", "Sandbox", "Model"],
            Enum.GetNames<NeedKind>());
    }
}
