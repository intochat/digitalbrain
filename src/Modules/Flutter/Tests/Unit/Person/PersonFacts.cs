using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Person;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Person;

public sealed class PersonFacts
{
    [Fact]
    public async Task SetWritesDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IPerson>("ada").Set("Ada", "https://example.com/ada.png");
        Assert.Equal("Ada", (await brain.Get<IPerson>("ada").Read()).DisplayName);
    }
}
