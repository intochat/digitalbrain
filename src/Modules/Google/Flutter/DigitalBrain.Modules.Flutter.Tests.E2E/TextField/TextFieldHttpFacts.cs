using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.TextField;

public sealed class TextFieldHttpFacts
{
    [Fact]
    public async Task ScopedValueRouteWritesTheNeuronAndTheUnscopedReadIsGone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        var field = brain.Get<ITextField>(UiScope.Key("workspace-a", "name"));
        await field.Configure("Name", "secret");

        using var set = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/ui/textfields/name/value", new { value = "Ada" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, set.StatusCode);
        Assert.Equal("Ada", (await field.Read()).Value);

        using var removed = await brain.HttpClient.GetAsync("/workspaces/workspace-a/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        using var unscoped = await brain.HttpClient.GetAsync("/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, unscoped.StatusCode);
    }
}
