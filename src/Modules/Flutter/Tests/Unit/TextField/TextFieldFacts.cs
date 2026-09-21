using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.TextField;

public sealed class TextFieldFacts
{
    [Fact]
    public async Task ConfigureAndSetValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var field = brain.Get<ITextField>("name");
        await field.Configure("Name", "text");
        await field.SetValue("Ada");
        Assert.Equal("Ada", (await field.Read()).Value);
    }
}
