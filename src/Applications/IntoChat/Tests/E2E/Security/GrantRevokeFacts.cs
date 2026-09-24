using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IntoChat.Tests.E2E.Security;

// The grants list and revoke routes at the HTTP edge are scoped to the caller's workspace.
// Enforcement of a grant on a call is covered by Identity's GrantFacts.
public sealed class GrantRevokeFacts
{
    [Fact(Timeout = 300_000)]
    public async Task GrantsAreListedAndRevokedOnlyInTheOwnersWorkspace()
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

        var member = await aliceLogin.Content.ReadFromJsonAsync<DigitalBrain.Identity.Member>(ct);
        var workspace = member!.WorkspaceId;
        using var create = await alice.PostAsJsonAsync(
            $"/workspaces/{workspace}/grants",
            new { appId = "app-1", semanticTypeId = "person.birthDate", mode = 2, workspaceId = workspace },
            ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var listed = await alice.GetAsync($"/workspaces/{workspace}/grants", ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedJson = await listed.Content.ReadAsStringAsync(ct);
        var grants = JsonSerializer.Deserialize<JsonElement[]>(listedJson)!;
        var grantJson = Assert.Single(grants);
        Assert.Equal("app-1", grantJson.GetProperty("appId").GetString());

        using var foreign = await bob.GetAsync($"/workspaces/{workspace}/grants", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var revoke = await alice.PostAsJsonAsync(
            $"/workspaces/{workspace}/grants/revoke",
            new { appId = "app-1", semanticTypeId = "person.birthDate", mode = 2 },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using var emptied = await alice.GetAsync($"/workspaces/{workspace}/grants", ct);
        Assert.Empty(JsonSerializer.Deserialize<JsonElement[]>(await emptied.Content.ReadAsStringAsync(ct))!);
    }

    private static HttpClient CookieClient(HttpClient origin)
    {
        var handler = new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        return new HttpClient(handler) { BaseAddress = origin.BaseAddress };
    }
}