using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Identity;

namespace IntoChat.Tests.E2E.Security;

public sealed class CrossWorkspaceFacts(IntoChatHostFixture host)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact(Timeout = 180_000)]
    public async Task SecondWorkspaceCannotReadTheFirstsValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var firstWorkspace = brain.WorkspaceId;
        var secondWorkspace = "workspace-" + Guid.NewGuid().ToString("N");
        var firstSlider = brain.Get<ISlider>(UiScope.Key(BrainScope.Create("owner", firstWorkspace).Id, "volume"));
        var secondSlider = brain.Get<ISlider>(UiScope.Key(BrainScope.Create("owner", secondWorkspace).Id, "volume"));
        await firstSlider.Configure(0, 10, 1);
        await firstSlider.SetValue(7);

        var first = await brain.HttpClient.GetFromJsonAsync<SliderState>($"/brains/{firstWorkspace}/ui/sliders/volume", Json, ct);
        Assert.Equal(7, first!.Value);

        var second = await brain.HttpClient.GetFromJsonAsync<SliderState>($"/brains/{secondWorkspace}/ui/sliders/volume", Json, ct);
        Assert.Equal(0, second!.Value);

        await secondSlider.Configure(0, 10, 1);
        await secondSlider.SetValue(3);
        var secondAfterWrite = await brain.HttpClient.GetFromJsonAsync<SliderState>($"/brains/{secondWorkspace}/ui/sliders/volume", Json, ct);
        var firstAfterSecondWrite = await brain.HttpClient.GetFromJsonAsync<SliderState>($"/brains/{firstWorkspace}/ui/sliders/volume", Json, ct);
        Assert.Equal(3, secondAfterWrite!.Value);
        Assert.Equal(7, firstAfterSecondWrite!.Value);
    }

    [Fact(Timeout = 180_000)]
    public async Task ASignedInPrincipalCannotReachAnotherPrincipalsWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var alice = CookieClient(brain.HttpClient);
        using var bob = CookieClient(brain.HttpClient);

        using var aliceLogin = await alice.PostAsJsonAsync(
            "/identity/register",
            new { principalId = "alice-" + suffix, displayName = "Alice", password = "alice-password-123" }, ct);
        Assert.Equal(HttpStatusCode.OK, aliceLogin.StatusCode);

        using var bobLogin = await bob.PostAsJsonAsync(
            "/identity/register",
            new { principalId = "bob-" + suffix, displayName = "Bob", password = "bob-password-123" }, ct);
        Assert.Equal(HttpStatusCode.OK, bobLogin.StatusCode);

        var aliceMember = await aliceLogin.Content.ReadFromJsonAsync<DigitalBrain.Identity.Member>(Json, ct);
        var bobMember = await bobLogin.Content.ReadFromJsonAsync<DigitalBrain.Identity.Member>(Json, ct);
        var aliceWorkspace = aliceMember!.BrainId;
        var bobWorkspace = bobMember!.BrainId;

        // Bob reaches his own workspace but is forbidden from Alice's scoped value and app node.
        using var ownUi = await bob.GetAsync($"/brains/{bobWorkspace}/ui/sliders/volume", ct);
        Assert.Equal(HttpStatusCode.OK, ownUi.StatusCode);

        using var foreignWorkspace = await bob.GetAsync($"/brains/{aliceWorkspace}", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignWorkspace.StatusCode);
        using var foreignSources = await bob.PostAsJsonAsync($"/brains/{aliceWorkspace}/connected-sources", new { sources = new[] { "stolen" } }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignSources.StatusCode);

        using var foreignUi = await bob.GetAsync($"/brains/{aliceWorkspace}/ui/sliders/volume", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignUi.StatusCode);

        using var foreignNode = await bob.GetAsync($"/brains/{aliceWorkspace}/apps/node?kind=text&name={aliceWorkspace}/apps/forms/intake", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignNode.StatusCode);

        using var foreignCompute = await bob.GetAsync($"/brains/{aliceWorkspace}/compute/usage", ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreignCompute.StatusCode);
        using var ownCompute = await bob.GetAsync($"/brains/{bobWorkspace}/compute/usage", ct);
        Assert.Equal(HttpStatusCode.OK, ownCompute.StatusCode);

        await brain.Get<DigitalBrain.Compute.IWallet>(aliceMember.AccountId).ChargeAsync(new DigitalBrain.Compute.LedgerEntry
        {
            AccountId = aliceMember.AccountId,
            IdempotencyKey = "private-charge",
            Kind = DigitalBrain.Compute.LedgerKind.WalletCharge,
            Amount = 7m,
            OccurredAt = DateTimeOffset.UtcNow,
        }, ct);
        using var aliceSummary = JsonDocument.Parse(await alice.GetStringAsync("/compute/summary", ct));
        using var bobSummary = JsonDocument.Parse(await bob.GetStringAsync("/compute/summary", ct));
        Assert.Equal(7m, aliceSummary.RootElement.GetProperty("chargedCompute").GetDecimal());
        Assert.Equal(0m, bobSummary.RootElement.GetProperty("chargedCompute").GetDecimal());
    }

    private static HttpClient CookieClient(HttpClient origin)
    {
        var handler = new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        return new HttpClient(handler) { BaseAddress = origin.BaseAddress };
    }
}
