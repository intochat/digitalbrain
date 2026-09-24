using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;

namespace IntoChat.Tests.E2E.Security;

public sealed class CrossWorkspaceFacts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact(Timeout = 180_000)]
    public async Task SecondWorkspaceCannotReadTheFirstsValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        var slider = brain.Get<ISlider>(UiScope.Key("workspace-a", "volume"));
        await slider.Configure(0, 10, 1);
        await slider.SetValue(7);

        var owner = await brain.HttpClient.GetFromJsonAsync<SliderState>("/workspaces/workspace-a/ui/sliders/volume", Json, ct);
        Assert.Equal(7, owner!.Value);

        var foreign = await brain.HttpClient.GetFromJsonAsync<SliderState>("/workspaces/workspace-b/ui/sliders/volume", Json, ct);
        Assert.Equal(0, foreign!.Value);
    }

    [Fact(Timeout = 180_000)]
    public async Task ASignedInPrincipalCannotReachAnotherPrincipalsWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        using var alice = CookieClient(brain.HttpClient);
        using var bob = CookieClient(brain.HttpClient);

        using var aliceLogin = await alice.PostAsJsonAsync(
            "/identity/register",
            new { principalId = "alice", displayName = "Alice", password = "alice-password-123" }, ct);
        Assert.Equal(HttpStatusCode.OK, aliceLogin.StatusCode);

        using var bobLogin = await bob.PostAsJsonAsync(
            "/identity/register",
            new { principalId = "bob", displayName = "Bob", password = "bob-password-123" }, ct);
        Assert.Equal(HttpStatusCode.OK, bobLogin.StatusCode);

        var aliceMember = await aliceLogin.Content.ReadFromJsonAsync<DigitalBrain.Identity.Member>(Json, ct);
        var bobMember = await bobLogin.Content.ReadFromJsonAsync<DigitalBrain.Identity.Member>(Json, ct);
        var aliceWorkspace = aliceMember!.WorkspaceId;
        var bobWorkspace = bobMember!.WorkspaceId;

        // Bob reaches his own workspace but is forbidden from Alice's scoped value and app node.
        using var ownUi = await bob.GetAsync($"/workspaces/{bobWorkspace}/ui/sliders/volume", ct);
        Assert.Equal(HttpStatusCode.OK, ownUi.StatusCode);

        using var foreignWorkspace = await bob.GetAsync($"/workspaces/{aliceWorkspace}", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignWorkspace.StatusCode);
        using var foreignSources = await bob.PostAsJsonAsync($"/workspaces/{aliceWorkspace}/connected-sources", new { sources = new[] { "stolen" } }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignSources.StatusCode);

        using var foreignUi = await bob.GetAsync($"/workspaces/{aliceWorkspace}/ui/sliders/volume", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignUi.StatusCode);

        using var foreignNode = await bob.GetAsync($"/workspaces/{aliceWorkspace}/apps/node?kind=text&name={aliceWorkspace}/apps/forms/intake", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignNode.StatusCode);
    }

    private static HttpClient CookieClient(HttpClient origin)
    {
        var handler = new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        return new HttpClient(handler) { BaseAddress = origin.BaseAddress };
    }

    [Fact(Timeout = 180_000)]
    public async Task UnscopedValueRoutesAreGone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        using var slider = await brain.HttpClient.GetAsync("/ui/sliders/volume", ct);
        using var textfield = await brain.HttpClient.GetAsync("/ui/textfields/name", ct);
        Assert.Equal(HttpStatusCode.NotFound, slider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, textfield.StatusCode);
    }
}