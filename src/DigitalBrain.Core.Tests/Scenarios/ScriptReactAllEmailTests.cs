using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ScriptReactAllEmailTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockGmail>()
            .AddModule<InvoiceCatcher>()
            .AddModule<TaskStore>()
            .AddModule<UiProjector>();

    [Fact(DisplayName = "Scripting: behavior neuron reacts to all Invoice EmailReceived")]
    public async Task InvoiceEmailCreatesTaskAndBehaviorNudge()
    {
        var ct = Cancellation;
        var context = "owner-inbox";
        var session = Brain.Session(context);
        var gmailId = new NeuronId("mockgmail", context);
        var catcherId = new NeuronId("invoicecatcher", context);
        var taskStoreId = new NeuronId("taskstore", context);
        var uiId = new NeuronId("uiprojector", context);
        var messageId = "msg-inv-7";
        var subject = "Invoice #991 — Net 15";

        await session.EmitAsync(
            new ObserveEmail(messageId, "ap@vendor.com", "vendor.com", subject, "Amount due $420"),
            ct);

        var catcherReading = await WaitForJournalAsync(
            catcherId,
            reading => reading.AllSaid<TaskCreated>().Count == 1
                && reading.AllSaid<BehaviorNudge>().Count == 1,
            "TaskCreated and BehaviorNudge from InvoiceCatcher",
            ct);

        var taskStoreReading = await WaitForJournalAsync(
            taskStoreId,
            reading => reading.AllHeard<TaskCreated>().Count == 1,
            "TaskStore heard TaskCreated",
            ct);

        var uiReading = await WaitForJournalAsync(
            uiId,
            reading => reading.AllHeard<BehaviorNudge>().Count == 1,
            "UiProjector heard BehaviorNudge",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var observeSaid = sessionReading.SaidSingle<ObserveEmail>();
        Assert.Equal("declared", observeSaid.DeliveryTo(gmailId).Via);

        var gmailReading = await ReadAsync(gmailId, ct);
        var observeHeard = gmailReading.HeardSingle<ObserveEmail>();
        Assert.Equal(session.Id, observeHeard.Metadata.Source);
        Assert.Equal(observeSaid.Position, observeHeard.Metadata.Sequence);

        var emailSaid = gmailReading.SaidSingle<EmailReceived>();
        Assert.Equal(new SynapseRef(session.Id, observeSaid.Position), emailSaid.Cause);
        Assert.Equal("declared", emailSaid.DeliveryTo(catcherId).Via);
        var email = Assert.IsType<EmailReceived>(emailSaid.Body);
        Assert.Equal(messageId, email.MessageId);
        Assert.Equal(subject, email.Subject);

        var emailHeard = catcherReading.HeardSingle<EmailReceived>();
        Assert.Equal(gmailId, emailHeard.Metadata.Source);
        Assert.Equal(emailSaid.Position, emailHeard.Metadata.Sequence);

        var taskSaid = catcherReading.SaidSingle<TaskCreated>();
        Assert.Equal(new SynapseRef(gmailId, emailSaid.Position), taskSaid.Cause);
        Assert.Equal("declared", taskSaid.DeliveryTo(taskStoreId).Via);
        var task = Assert.IsType<TaskCreated>(taskSaid.Body);
        Assert.Equal($"task-{messageId}", task.TaskId);
        Assert.Equal(subject, task.Title);
        Assert.Equal(messageId, task.SourceMessageId);
        Assert.Equal("finance", task.Tag);

        var nudgeSaid = catcherReading.SaidSingle<BehaviorNudge>();
        Assert.Equal(new SynapseRef(gmailId, emailSaid.Position), nudgeSaid.Cause);
        Assert.Equal("declared", nudgeSaid.DeliveryTo(uiId).Via);
        var nudge = Assert.IsType<BehaviorNudge>(nudgeSaid.Body);
        Assert.Equal(InvoiceCatcher.BehaviorId, nudge.BehaviorId);
        Assert.Equal(messageId, nudge.MessageId);
        Assert.Equal("Invoice", nudge.ChipLabel);

        Assert.Equal(catcherId, taskStoreReading.HeardSingle<TaskCreated>().Metadata.Source);
        Assert.Equal(taskSaid.Position, taskStoreReading.HeardSingle<TaskCreated>().Metadata.Sequence);
        Assert.Equal(catcherId, uiReading.HeardSingle<BehaviorNudge>().Metadata.Source);
        Assert.Equal(nudgeSaid.Position, uiReading.HeardSingle<BehaviorNudge>().Metadata.Sequence);
    }

    [Fact(DisplayName = "Scripting: non-invoice email does not create task or nudge")]
    public async Task NonInvoiceEmailIsIgnoredByCatcher()
    {
        var ct = Cancellation;
        var context = "owner-inbox-noise";
        var session = Brain.Session(context);
        var catcherId = new NeuronId("invoicecatcher", context);

        await session.EmitAsync(
            new ObserveEmail("msg-hi", "friend@x.com", "x.com", "Hello", "just saying hi"),
            ct);

        var catcherReading = await WaitForJournalAsync(
            catcherId,
            reading => reading.AllHeard<EmailReceived>().Count == 1,
            "catcher heard the non-invoice email",
            ct);

        Assert.Empty(catcherReading.AllSaid<TaskCreated>());
        Assert.Empty(catcherReading.AllSaid<BehaviorNudge>());
    }
}
