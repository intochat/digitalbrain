using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.TextField;

[Collection(FlutterHostCollection.Name)]
public sealed class TextFieldHttpFacts(FlutterHostFixture host)
{
    [Fact]
    public async Task ScopedValueRouteWritesTheNeuronAndTheUnscopedReadIsGone()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        var field = brain.Get<ITextField>(UiScope.Key(BrainScope.Create("owner", ws).Id, "name"));
        await field.Configure("Name", "secret");

        using var set = await brain.HttpClient.PostAsJsonAsync($"/brains/{ws}/ui/textfields/name/value", new { value = "Ada" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, set.StatusCode);
        Assert.Equal("Ada", (await field.Read()).Value);

        using var removed = await brain.HttpClient.GetAsync($"/brains/{ws}/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        using var unscoped = await brain.HttpClient.GetAsync("/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, unscoped.StatusCode);
    }
}
