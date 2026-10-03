using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Platform.Contracts.Identity;

namespace DigitalBrain.Kernel.Tests.E2E.Identity;

// The grants list and revoke routes at the HTTP edge are scoped to the caller's workspace.
// Enforcement of a grant on a call is covered by Identity's GrantFacts.
public sealed class GrantRevokeFacts(ReferenceBrainFixture host)
{
    [Fact(Timeout = 300_000)]
    public async Task GrantsAreListedAndRevokedOnlyInTheOwnersWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var alicePerson = await DigitalBrain.Testing.E2E.Packages.People.SignedIn(brain.HttpClient, "alice-" + suffix, ct);
        using var bobPerson = await DigitalBrain.Testing.E2E.Packages.People.SignedIn(brain.HttpClient, "bob-" + suffix, ct);
        var alice = alicePerson.Client;
        var bob = bobPerson.Client;
        var workspace = alicePerson.Workspace;
        using var create = await alice.PostAsJsonAsync(
            $"/brains/{workspace}/grants",
            new { appId = "app-1", semanticTypeId = "person.birthDate", mode = 2, workspaceId = workspace },
            ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var listed = await alice.GetAsync($"/brains/{workspace}/grants", ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedJson = await listed.Content.ReadAsStringAsync(ct);
        var grants = JsonSerializer.Deserialize<JsonElement[]>(listedJson)!;
        var grantJson = Assert.Single(grants);
        Assert.Equal("app-1", grantJson.GetProperty("appId").GetString());

        using var foreign = await bob.GetAsync($"/brains/{workspace}/grants", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var revoke = await alice.PostAsJsonAsync(
            $"/brains/{workspace}/grants/revoke",
            new { appId = "app-1", semanticTypeId = "person.birthDate", mode = 2 },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using var emptied = await alice.GetAsync($"/brains/{workspace}/grants", ct);
        Assert.Empty(JsonSerializer.Deserialize<JsonElement[]>(await emptied.Content.ReadAsStringAsync(ct))!);
    }

}
