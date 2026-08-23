using System.Net;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// The first live run of the whole BrainAppHostFixture pipeline -- a real AppHost boot
// (Azurite, silos, kernel, mcp) driving the facade across process boundaries, unlike the
// in-process simulation DigitalBrainTests.
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
        var brain = fixture.BrainFor($"e2e-{Guid.NewGuid().ToString("N")[..8]}");
        await brain.ActivateAsync(TestContext.Current.CancellationToken);

        // Observe the activation's journal footprint using the same subject/kind the simulation
        // suite's JournalSmokeTests.ActivationLandsInTheSessionJournal pins: DigitalBrainActivated
        // lands in the owner session's OWN Outgoing journal.
        var subject = ISessionNeuron.ForOwner(brain.Owner);
        var delivery = await JournalWait.ForAsync(
            brain,
            subject,
            JournalKind.Outgoing,
            static delivery => delivery.Synapse is DigitalBrainActivated,
            TimeSpan.FromSeconds(60));

        Assert.IsType<DigitalBrainActivated>(delivery.Synapse);
    }
}
