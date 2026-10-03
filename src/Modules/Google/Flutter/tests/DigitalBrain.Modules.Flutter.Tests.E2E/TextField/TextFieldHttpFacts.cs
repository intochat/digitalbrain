using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.TextField;

[Collection(FlutterBackendCollection.Name)]
public sealed class TextFieldHttpFacts(FlutterBackendFixture host)
{
    [Fact]
    public async Task ScopedValueRouteWritesTheNeuronAndTheUnscopedReadIsGone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var field = brain.Get<ITextField>(UiScope.Key(BrainScope.Create("owner", brain.WorkspaceId).Id, "name"));
        await field.Configure("Name", "secret");

        using var set = await brain.HttpClient.PostAsJsonAsync($"/brains/{brain.WorkspaceId}/ui/textfields/name/value", new { value = "Ada" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, set.StatusCode);
        Assert.Equal("Ada", (await field.Read()).Value);

        using var removed = await brain.HttpClient.GetAsync($"/brains/{brain.WorkspaceId}/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        using var unscoped = await brain.HttpClient.GetAsync("/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, unscoped.StatusCode);
    }
}
