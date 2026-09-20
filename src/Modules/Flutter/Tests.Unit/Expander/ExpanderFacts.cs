using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Expander;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ExpanderFacts
{
    [Fact]
    public async Task SetThenToggle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(FlutterModule))] }, ct);
        var expander = brain.Get<IExpander>("more");
        await expander.Set("More", false, []);
        await expander.Toggle();
        Assert.True((await expander.Read()).Expanded);
    }
}
