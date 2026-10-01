using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider;
using DigitalBrain.Identity;

namespace DigitalBrain.Core.Tests.E2E.Identity;

public sealed class BrainRouteIsolationFacts(ReferenceBrainFixture host)
{

    [Fact(Timeout = 180_000)]
    public async Task ASignedInPrincipalCannotReachAnotherPrincipalsWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var alicePerson = await DigitalBrain.Testing.E2E.Packages.People.SignedIn(brain.HttpClient, "alice-" + suffix, ct);
        using var bobPerson = await DigitalBrain.Testing.E2E.Packages.People.SignedIn(brain.HttpClient, "bob-" + suffix, ct);
        var alice = alicePerson.Client;
        var bob = bobPerson.Client;
        var aliceWorkspace = alicePerson.Workspace;
        var bobWorkspace = bobPerson.Workspace;
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

        await brain.Get<DigitalBrain.Compute.IWallet>(alicePerson.Account).ChargeAsync(new DigitalBrain.Compute.LedgerEntry
        {
            AccountId = alicePerson.Account,
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

}




