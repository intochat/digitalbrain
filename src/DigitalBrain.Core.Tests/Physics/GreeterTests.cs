using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class GreeterTests(BrainTestClusters clusters) : NeuronTest<Greeter>(clusters)
{
    [Fact(DisplayName = "A session ask reaches the greeter and the typed reply returns with the round trip in both journals")]
    public async Task GreetRoundTrip()
    {
        var ct = Cancellation;
        var session = Brain.Session("chat-1");
        var greeterId = Neuron("chat-1");

        var greeted = await session.AskAsync<Greeted>(new Greet("Ada"), ct);

        Assert.Equal("Hello, Ada!", greeted.Message);

        var sessionReading = await ReadAsync(session.Id, ct);
        var askSaid = sessionReading.SaidSingle<Greet>();
        Assert.Equal("ask", askSaid.DeliveryTo(greeterId).Via);

        var replyHeard = sessionReading.HeardSingle<Greeted>();
        Assert.Equal(greeterId, replyHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), replyHeard.Answers);
        Assert.Equal("Hello, Ada!", Assert.IsType<Greeted>(replyHeard.Body).Message);

        var greeterReading = await ReadAsync(greeterId, ct);
        var questionHeard = greeterReading.HeardSingle<Greet>();
        Assert.Equal(session.Id, questionHeard.Metadata.Source);
        Assert.Equal(askSaid.Position, questionHeard.Metadata.Sequence);
        Assert.Equal("Ada", Assert.IsType<Greet>(questionHeard.Body).Who);

        var replySaid = greeterReading.SaidSingle<Greeted>();
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), replySaid.Answers);
        Assert.Equal("ask", replySaid.DeliveryTo(session.Id).Via);
    }
}
