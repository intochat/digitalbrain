using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class CrossModuleCorrelationTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ThreadCoordinator>()
            .AddModule<CrmLinker>()
            .AddModule<MailLinker>()
            .AddModule<ThreadTimeline>()
            .AddModule<ThreadTimelineLedger>();

    [Fact(DisplayName = "Cross-module correlation on one owner thread")]
    public async Task OneEmitFormsSingleCauseThreadAcrossThreeKinds()
    {
        var ct = Cancellation;
        var context = "northwind-renewal";
        var session = Brain.Session(context);
        var coordinatorId = new NeuronId("threadcoordinator", context);
        var crmId = new NeuronId("crmlinker", context);
        var mailId = new NeuronId("maillinker", context);
        var timelineId = new NeuronId("threadtimeline", context);
        var threadKey = "northwind-renewal";
        var title = "Close the Northwind renewal this week.";

        await session.EmitAsync(new OpenWorkThread(threadKey, title), ct);

        var timelineReading = await WaitForJournalAsync(
            timelineId,
            reading => reading.AllHeard<EmailThreadAttached>().Count == 1
                && reading.AllSaid<ThreadTimelineReady>().Count == 1,
            "timeline heard EmailThreadAttached and said ThreadTimelineReady",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var openSaid = sessionReading.SaidSingle<OpenWorkThread>();
        Assert.Null(openSaid.Cause);
        Assert.Equal("declared", openSaid.DeliveryTo(coordinatorId).Via);

        var coordinatorReading = await ReadAsync(coordinatorId, ct);
        var openHeard = coordinatorReading.HeardSingle<OpenWorkThread>();
        Assert.Equal(session.Id, openHeard.Metadata.Source);
        Assert.Equal(openSaid.Position, openHeard.Metadata.Sequence);

        var openedSaid = coordinatorReading.SaidSingle<WorkThreadOpened>();
        Assert.Equal(new SynapseRef(session.Id, openSaid.Position), openedSaid.Cause);
        Assert.Equal("declared", openedSaid.DeliveryTo(crmId).Via);
        Assert.Equal(threadKey, Assert.IsType<WorkThreadOpened>(openedSaid.Body).ThreadKey);

        var crmReading = await ReadAsync(crmId, ct);
        var openedHeard = crmReading.HeardSingle<WorkThreadOpened>();
        Assert.Equal(coordinatorId, openedHeard.Metadata.Source);
        Assert.Equal(openedSaid.Position, openedHeard.Metadata.Sequence);

        var opportunitySaid = crmReading.SaidSingle<OpportunityLinked>();
        Assert.Equal(new SynapseRef(coordinatorId, openedSaid.Position), opportunitySaid.Cause);
        Assert.Equal("declared", opportunitySaid.DeliveryTo(mailId).Via);
        var opportunity = Assert.IsType<OpportunityLinked>(opportunitySaid.Body);
        Assert.Equal(threadKey, opportunity.ThreadKey);
        Assert.Equal($"opp-{threadKey}", opportunity.OpportunityId);

        var mailReading = await ReadAsync(mailId, ct);
        var opportunityHeard = mailReading.HeardSingle<OpportunityLinked>();
        Assert.Equal(crmId, opportunityHeard.Metadata.Source);
        Assert.Equal(opportunitySaid.Position, opportunityHeard.Metadata.Sequence);

        var emailSaid = mailReading.SaidSingle<EmailThreadAttached>();
        Assert.Equal(new SynapseRef(crmId, opportunitySaid.Position), emailSaid.Cause);
        Assert.Equal("declared", emailSaid.DeliveryTo(timelineId).Via);
        var email = Assert.IsType<EmailThreadAttached>(emailSaid.Body);
        Assert.Equal(threadKey, email.ThreadKey);
        Assert.Equal($"mail-opp-{threadKey}", email.MessageId);

        var emailHeard = timelineReading.HeardSingle<EmailThreadAttached>();
        Assert.Equal(mailId, emailHeard.Metadata.Source);
        Assert.Equal(emailSaid.Position, emailHeard.Metadata.Sequence);

        var readySaid = timelineReading.SaidSingle<ThreadTimelineReady>();
        Assert.Equal(new SynapseRef(mailId, emailSaid.Position), readySaid.Cause);

        // One causal thread: walk Cause parents; positions/sources must match said rows, not three orphans.
        Assert.Equal(session.Id, openedSaid.Cause!.Value.Source);
        Assert.Equal(openSaid.Position, openedSaid.Cause.Value.Sequence);

        Assert.Equal(coordinatorId, opportunitySaid.Cause!.Value.Source);
        Assert.Equal(openedSaid.Position, opportunitySaid.Cause.Value.Sequence);

        Assert.Equal(crmId, emailSaid.Cause!.Value.Source);
        Assert.Equal(opportunitySaid.Position, emailSaid.Cause.Value.Sequence);

        Assert.Equal(mailId, readySaid.Cause!.Value.Source);
        Assert.Equal(emailSaid.Position, readySaid.Cause.Value.Sequence);

        // Cross-check Source stamps on the heard legs equal the emitting kind, not session re-emits.
        Assert.Equal(coordinatorId.Kind, openedHeard.Metadata.Source.Kind);
        Assert.Equal(crmId.Kind, opportunityHeard.Metadata.Source.Kind);
        Assert.Equal(mailId.Kind, emailHeard.Metadata.Source.Kind);
        Assert.Equal(context, openedHeard.Metadata.Source.Name);
        Assert.Equal(context, opportunityHeard.Metadata.Source.Name);
        Assert.Equal(context, emailHeard.Metadata.Source.Name);
    }
}
