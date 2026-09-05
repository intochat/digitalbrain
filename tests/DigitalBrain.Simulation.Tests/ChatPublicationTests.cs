using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ChatPublicationTests(SimulationFixture fixture)
{
    [Fact]
    public async Task ApplicationPublicationIsIdempotentEvenAfterItsTranscriptEntryIsTrimmed()
    {
        var name = fixture.Sim.UniqueId("publication");
        var chat = fixture.Sim.Brain.Get<IChat>(name);
        var token = TestContext.Current.CancellationToken;
        var publication = new PublishNote(Guid.NewGuid(), "Architecture and quality review completed.");
        Assert.False((await chat.RequestAsync(publication, token)).Duplicate);
        Assert.True((await chat.RequestAsync(publication, token)).Duplicate);
        var transcript = await chat.RequestAsync(new ReadTranscriptRequest(name), token);
        Assert.Single(transcript.Transcript.Turns, turn => turn.Text == publication.Text);

        for (var index = 0; index < 65; index++)
        {
            await chat.SendAsync(new Note($"Later note {index}"), token);
        }

        Assert.True((await chat.RequestAsync(publication, token)).Duplicate);
        transcript = await chat.RequestAsync(new ReadTranscriptRequest(name), token);
        Assert.DoesNotContain(transcript.Transcript.Turns, turn => turn.Text == publication.Text);
        await Assert.ThrowsAnyAsync<Exception>(() => chat.RequestAsync(publication with { Text = "Different review" }, token));
    }
}
