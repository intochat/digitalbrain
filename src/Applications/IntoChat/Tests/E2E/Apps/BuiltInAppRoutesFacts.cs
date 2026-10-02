using System.Net;
using System.Text.Json;
using DigitalBrain.Testing.E2E.Packages;

namespace IntoChat.Tests.E2E.Apps;

// The built-in assistant and settings are plain neurons that every IntoChat host serves.
public sealed class BuiltInAppRoutesFacts(IntoChatHostFixture host) : BrainFact(host)
{
    [Fact(Timeout = 2_100_000)]
    public async Task InstalledCustomerResearcherOpensThroughTheGenericRouteInItsBrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await IntoChatE2ETest.WaitUntilShippedAsync(Brain, "intochat/customer-researcher", ct, TimeSpan.FromMinutes(30));
        using var person = await People.SignedIn(Brain.HttpClient, "app-open-" + Guid.NewGuid().ToString("N"), ct);
        var scope = "/brains/" + person.BrainId;
        await People.Send(person.Client, HttpMethod.Post, scope + "/packages/intochat/customer-researcher", new { }, ct);
        var opened = await People.Send(person.Client, HttpMethod.Post, scope + "/apps/intochat%2Fcustomer-researcher/open", new { }, ct);
        Assert.Equal("customer-researcher", opened.GetProperty("id").GetString());
        Assert.Equal("Customer Researcher", opened.GetProperty("title").GetString());
        var workspace = await People.Send(person.Client, HttpMethod.Get, scope, null, ct);
        Assert.Contains(workspace.GetProperty("windows").EnumerateArray(), window =>
            window.GetProperty("id").GetString() == opened.GetProperty("id").GetString()
            && window.GetProperty("reference").GetProperty("neuronId").GetString() == opened.GetProperty("surface").GetString());
        using var missing = await person.Client.PostAsync(scope + "/apps/unknown/open", null, ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var error = JsonDocument.Parse(await missing.Content.ReadAsStringAsync(ct));
        Assert.Contains("not installed", error.RootElement.GetProperty("error").GetString());
    }

    [Fact(Timeout = 2_100_000)]
    public async Task BuiltInAppsActivateAndOpenAndAnUnknownAppIsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Brain;
        // Settings installs from the shipped package; its verification runs in the background
        // since the host booted and on a cold CI sandbox takes most of this fact's budget.
        await IntoChatE2ETest.WaitUntilShippedAsync(brain, "intochat/settings", ct, TimeSpan.FromMinutes(30));

        using var activated = await brain.HttpClient.PostAsync("/brains/personal/built-in/activate", null, ct);
        using var settings = await brain.HttpClient.PostAsync("/brains/personal/built-in/settings/open", null, ct);
        using var assistant = await brain.HttpClient.PostAsync("/brains/personal/built-in/assistant/open", null, ct);
        using var unknown = await brain.HttpClient.PostAsync("/brains/personal/built-in/calculator/open", null, ct);

        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assistant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
