using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Expander;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Expander;

public sealed class ExpanderFacts
{
    [Fact]
    public async Task SetThenToggle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var expander = brain.Get<IExpander>("more");
        await expander.Set("More", false, []);
        await expander.Toggle();
        Assert.True((await expander.Read()).Expanded);
    }
}
