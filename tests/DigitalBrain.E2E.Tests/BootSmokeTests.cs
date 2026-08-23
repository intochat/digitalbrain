using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Neurons;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// Tier 3: the first live run of the whole BrainAppHostFixture pipeline -- a real AppHost boot
// (Azurite, silos, kernel, mcp) driving the facade across process boundaries, not the in-memory
// simulation Tiers 1/2 exercise.
[Collection(E2ECollection.Name)]
public sealed class BootSmokeTests(AppHostFixture fixture)
{
    [Fact]
    public async Task KernelServesHealth()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FacadeFiresAcrossProcessesAndJournals()
    {
        await using var session = await fixture.OpenSessionAsync();

        // Activation already fired during OpenSessionAsync; observe its journal footprint using
        // the same subject/kind Tier 2's JournalSmokeTests.ActivationLandsInTheSessionJournal
        // pinned: DigitalBrainActivated lands in the owner session's OWN Outgoing journal.
        var subject = ISessionNeuron.ForOwner(session.Owner);
        var delivery = await session.WaitForJournalAsync(
            subject,
            JournalKind.Outgoing,
            static d => d.Synapse is DigitalBrainActivated,
            TimeSpan.FromSeconds(60));

        Assert.NotNull(delivery.Synapse);
    }

}
