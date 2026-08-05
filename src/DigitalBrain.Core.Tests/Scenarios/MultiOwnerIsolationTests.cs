using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MultiOwnerIsolationTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockGmail>()
            .AddModule<RunwayAdvisor>();

    [Fact(DisplayName =
        "Multi-owner isolation (Stage-1: different context Names, not OwnerId in NeuronId): Ada and Beau same kinds never mix mail or runway answers")]
    public async Task ParallelOwnersNeverMixJournalsOrAnswers()
    {
        var ct = Cancellation;
        var ada = "ada";
        var beau = "beau";
        var sessionAda = Brain.Session(ada);
        var sessionBeau = Brain.Session(beau);
        var advisorAda = new NeuronId("runwayadvisor", ada);
        var advisorBeau = new NeuronId("runwayadvisor", beau);

        await sessionAda.EmitAsync(new RunwayCashSnapshot(ada, 1_200_000m), ct);
        await sessionAda.EmitAsync(
            new ObserveEmail("ada-mail", "cfo@ada.example", "ada.example", "Ada board cash", "private"),
            ct);

        await sessionBeau.EmitAsync(new RunwayCashSnapshot(beau, 80_000m), ct);
        await sessionBeau.EmitAsync(
            new ObserveEmail("beau-mail", "cfo@beau.example", "beau.example", "Beau payroll", "private"),
            ct);

        await WaitForJournalAsync(
            advisorAda,
            reading => reading.AllHeard<EmailReceived>().Count == 1
                && reading.AllHeard<RunwayCashSnapshot>().Count == 1,
            "ada advisor heard own cash + mail",
            ct);
        await WaitForJournalAsync(
            advisorBeau,
            reading => reading.AllHeard<EmailReceived>().Count == 1
                && reading.AllHeard<RunwayCashSnapshot>().Count == 1,
            "beau advisor heard own cash + mail",
            ct);

        var adaAnswer = await sessionAda.AskAsync<RunwayAnswer>(new RunwayAsked(ada), ct);
        var beauAnswer = await sessionBeau.AskAsync<RunwayAnswer>(new RunwayAsked(beau), ct);

        Assert.Equal(ada, adaAnswer.Owner);
        Assert.Equal(1_200_000m, adaAnswer.CashUsd);
        Assert.Equal("Ada board cash", adaAnswer.SourceMailSubject);

        Assert.Equal(beau, beauAnswer.Owner);
        Assert.Equal(80_000m, beauAnswer.CashUsd);
        Assert.Equal("Beau payroll", beauAnswer.SourceMailSubject);

        Assert.DoesNotContain("Beau", adaAnswer.SourceMailSubject, StringComparison.Ordinal);
        Assert.DoesNotContain("Ada", beauAnswer.SourceMailSubject, StringComparison.Ordinal);

        var adaJournal = await ReadAsync(advisorAda, ct);
        var beauJournal = await ReadAsync(advisorBeau, ct);
        Assert.DoesNotContain(
            adaJournal.AllHeard<EmailReceived>(),
            h => Assert.IsType<EmailReceived>(h.Body).MessageId == "beau-mail");
        Assert.DoesNotContain(
            beauJournal.AllHeard<EmailReceived>(),
            h => Assert.IsType<EmailReceived>(h.Body).MessageId == "ada-mail");

        var adaObserve = (await ReadAsync(sessionAda.Id, ct)).SaidSingle<ObserveEmail>();
        Assert.Null(adaObserve.DeliveryToOrNull(new NeuronId("mockgmail", beau)));
        Assert.Equal("declared", adaObserve.DeliveryTo(new NeuronId("mockgmail", ada)).Via);
    }
}
