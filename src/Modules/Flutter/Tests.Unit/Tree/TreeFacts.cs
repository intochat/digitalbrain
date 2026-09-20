using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tree;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TreeFacts
{
    [Fact]
    public async Task SetThenSelect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new FlutterModule()] }, ct);
        var tree = brain.Get<ITree>("fs");
        await tree.Set([new TreeNode("root", null, "root"), new TreeNode("child", "root", "child")]);
        await tree.Select("child");
        Assert.Equal("child", (await tree.Read()).SelectedId);
    }
}
