using System.Net;

namespace IntoChat.Tests.E2E.Apps;

// The built-in assistant and settings are plain neurons that every IntoChat host serves.
public sealed class BuiltInAppRoutesFacts
{
    [Fact(Timeout = 300_000)]
    public async Task BuiltInAppsActivateAndOpenAndAnUnknownAppIsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        using var activated = await brain.HttpClient.PostAsync("/workspaces/personal/built-in/activate", null, ct);
        using var settings = await brain.HttpClient.PostAsync("/workspaces/personal/built-in/settings/open", null, ct);
        using var assistant = await brain.HttpClient.PostAsync("/workspaces/personal/built-in/assistant/open", null, ct);
        using var unknown = await brain.HttpClient.PostAsync("/workspaces/personal/built-in/calculator/open", null, ct);

        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assistant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
