using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Table;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Table;

public sealed class TableFacts
{
    [Fact]
    public async Task ReplaceAndSetView()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var table = brain.Get<ITable>("grid");
        await table.Replace("T", [new TableColumn("c", "C")], [["1"]]);
        await table.SetView("c", "1");
        Assert.Equal("c", (await table.Read()).Sort);
    }
}
