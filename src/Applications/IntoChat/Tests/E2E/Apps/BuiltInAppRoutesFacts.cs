using System.Net;

namespace IntoChat.Tests.E2E.Apps;

// The built-in assistant and settings are plain neurons that every IntoChat host serves.
public sealed class BuiltInAppRoutesFacts(IntoChatHostFixture host) : BrainFact(host)
{
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
