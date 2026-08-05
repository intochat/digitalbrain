using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class SpeechRoleTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<Greeter>()
            .AddModule<QuestionOverhearer>();

    [Fact(DisplayName =
        "L-R3 Session Send of a catalog question type is directed fact delivery: via directed, greeter is not invoked, overhearer hears as INeuron")]
    public async Task SessionSendOfQuestionTypeIsDirectedFactNotAnswererDispatch()
    {
        var ct = Cancellation;
        var context = "speech-role";
        var session = Brain.Session(context);
        var overhearId = new NeuronId("questionoverhearer", context);
        var greeterId = new NeuronId("greeter", context);
        var question = new Greet("should-not-answer");

        await session.SendAsync(overhearId, question, ct);

        var overhear = await WaitForJournalAsync(
            overhearId,
            reading => reading.AllHeard<Greet>().Count == 1,
            "overhearer heard Greet as ordinary delivery",
            ct);
        Assert.Equal("should-not-answer", Assert.IsType<Greet>(overhear.HeardSingle<Greet>().Body).Who);
        Assert.Empty(overhear.AllSaid<Greeted>());

        var sessionReading = await WaitForJournalAsync(
            session.Id,
            reading => reading.AllSaid<Greet>().Count == 1,
            "session said the directed Greet",
            ct);
        var sent = sessionReading.SaidSingle<Greet>();
        Assert.Equal("directed", sent.DeliveryTo(overhearId).Via);
        Assert.Null(sent.DeliveryToOrNull(greeterId));
        Assert.Single(sent.To!);

        var greeter = await ReadAsync(greeterId, ct);
        Assert.Empty(greeter.AllHeard<Greet>());
        Assert.Empty(greeter.AllSaid<Greeted>());
    }

    [Fact(DisplayName =
        "L-R3 Session Ask routes the answerer with via ask and stamps a typed reply Answers match")]
    public async Task SessionAskStillUsesRequestRouteToAnswerer()
    {
        var ct = Cancellation;
        var context = "speech-role-ask";
        var session = Brain.Session(context);
        var greeterId = new NeuronId("greeter", context);

        var greeted = await session.AskAsync<Greeted>(new Greet("Ada"), ct);
        Assert.Equal("Hello, Ada!", greeted.Message);

        var sessionReading = await ReadAsync(session.Id, ct);
        var askSaid = sessionReading.SaidSingle<Greet>();
        Assert.Equal("ask", askSaid.DeliveryTo(greeterId).Via);
    }
}

public sealed class QuestionOverhearer : Neuron, INeuron<Greet>
{
    public Task HandleAsync(Greet fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
