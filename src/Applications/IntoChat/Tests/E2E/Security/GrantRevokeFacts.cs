using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Apps;

namespace IntoChat.Tests.E2E.Security;

// J5 second half at the HTTP edge: a grant lets an app read an owner's field, revoking it makes the
// value look missing again, and the grants list/revoke routes are scoped to the caller's workspace.
public sealed class GrantRevokeFacts
{
    private const string Workspace = "workspace-grants";

    [Fact(Timeout = 300_000)]
    public async Task GrantThenRevokeMakesTheValueLookEmptyAndForeignWorkspacesAreForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);

        using var alice = CookieClient(brain.HttpClient);
        using var bob = CookieClient(brain.HttpClient);
        using var aliceLogin = await alice.PostAsJsonAsync(
            "/identity/login",
            new { principalId = "alice", displayName = "Alice", workspaceId = Workspace }, ct);
        Assert.Equal(HttpStatusCode.OK, aliceLogin.StatusCode);
        using var bobLogin = await bob.PostAsJsonAsync(
            "/identity/login",
            new { principalId = "bob", displayName = "Bob", workspaceId = "workspace-bob" }, ct);
        Assert.Equal(HttpStatusCode.OK, bobLogin.StatusCode);

        var proxy = brain.Get<IAppProxy>("grant-gate");
        var appRead = new AppProxyRequest
        {
            AppId = "app-1",
            Operation = "Read",
            TargetNeuron = "vault",
            PrincipalId = "alice",
            AccountId = "alice",
            WorkspaceId = Workspace,
            SemanticTypeIds = ["person.birthDate"],
        };

        var before = await proxy.Invoke(appRead);
        Assert.False(before.Allowed);
        Assert.Equal("MissingGrant", before.Denial);

        using var create = await alice.PostAsJsonAsync(
            $"/workspaces/{Workspace}/grants",
            new { appId = "app-1", semanticTypeId = "person.birthDate", mode = 2, workspaceId = Workspace },
            ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var listed = await alice.GetAsync($"/workspaces/{Workspace}/grants", ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedJson = await listed.Content.ReadAsStringAsync(ct);
        var grants = JsonSerializer.Deserialize<JsonElement[]>(listedJson)!;
        var grantJson = Assert.Single(grants);
        Assert.Equal("app-1", grantJson.GetProperty("appId").GetString());

        var allowed = await proxy.Invoke(appRead);
        Assert.True(allowed.Allowed, $"{allowed.Denial}: {allowed.Explanation}; listed={listedJson}");

        using var foreign = await bob.GetAsync($"/workspaces/{Workspace}/grants", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var revoke = await alice.PostAsJsonAsync(
            $"/workspaces/{Workspace}/grants/revoke",
            new { appId = "app-1", semanticTypeId = "person.birthDate", mode = 2 },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var after = await proxy.Invoke(appRead);
        Assert.False(after.Allowed);
        Assert.Equal("MissingGrant", after.Denial);

        using var emptied = await alice.GetAsync($"/workspaces/{Workspace}/grants", ct);
        Assert.Empty(JsonSerializer.Deserialize<JsonElement[]>(await emptied.Content.ReadAsStringAsync(ct))!);
    }

    private static HttpClient CookieClient(HttpClient origin)
    {
        var handler = new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        return new HttpClient(handler) { BaseAddress = origin.BaseAddress };
    }
}