using DigitalBrain.Mocks;
using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class InstallCsharpBehaviorScriptTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockGmail>()
            .AddModule<BehaviorCatalog>()
            .AddModule<BehaviorActivationLedger>()
            .AddModule<VipEmailToTask>()
            .AddModule<TaskStore>()
            .AddModule<UiProjector>();

    [Fact(DisplayName =
        "Install C# behavior (composition includes script neuron, not ALC hot-load): BehaviorInstallProposed → BehaviorActivated, then VIP EmailReceived → TaskCreated")]
    public async Task CompositionIncludesScriptNeuronActivationThenVipEmailCreatesTask()
    {
        var ct = Cancellation;
        var context = "owner-behavior-studio";
        var session = Brain.Session(context);
        var catalogId = new NeuronId("behaviorcatalog", context);
        var ledgerId = new NeuronId("behavioractivationledger", context);
        var scriptId = new NeuronId("vipemailtotask", context);
        var gmailId = new NeuronId("mockgmail", context);
        var taskStoreId = new NeuronId("taskstore", context);
        var uiId = new NeuronId("uiprojector", context);
        var behaviorId = VipEmailToTask.BehaviorId;
        var messageId = "msg-vip-1";
        var subject = "Board decision needed";

        await session.EmitAsync(
            new BehaviorInstallProposed(
                behaviorId,
                ScriptKind: "vipemailtotask",
                Listens: ["emailreceived"]),
            ct);

        var catalogReading = await WaitForJournalAsync(
            catalogId,
            reading => reading.AllSaid<BehaviorActivated>().Count == 1,
            "BehaviorActivated after install propose",
            ct);

        var installSaid = (await ReadAsync(session.Id, ct)).SaidSingle<BehaviorInstallProposed>();
        Assert.Equal("declared", installSaid.DeliveryTo(catalogId).Via);

        var installHeard = catalogReading.HeardSingle<BehaviorInstallProposed>();
        Assert.Equal(session.Id, installHeard.Metadata.Source);
        Assert.Equal(installSaid.Position, installHeard.Metadata.Sequence);

        var activatedSaid = catalogReading.SaidSingle<BehaviorActivated>();
        Assert.Equal(new SynapseRef(session.Id, installSaid.Position), activatedSaid.Cause);
        Assert.Equal("declared", activatedSaid.DeliveryTo(ledgerId).Via);
        var activated = Assert.IsType<BehaviorActivated>(activatedSaid.Body);
        Assert.Equal(behaviorId, activated.BehaviorId);
        Assert.Equal("vipemailtotask", activated.ScriptKind);
        Assert.Equal(["emailreceived"], activated.Listens);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<BehaviorActivated>().Count == 1,
            "activation ledger heard BehaviorActivated",
            ct);
        Assert.Equal(catalogId, ledgerReading.HeardSingle<BehaviorActivated>().Metadata.Source);
        Assert.Equal(activatedSaid.Position, ledgerReading.HeardSingle<BehaviorActivated>().Metadata.Sequence);

        // Script kind is already in the composition catalog; live proof after journaled activation.
        await session.EmitAsync(
            new ObserveEmail(
                messageId,
                From: "ceo@board.example",
                Domain: VipEmailToTask.VipDomain,
                Subject: subject,
                Snippet: "Please track this."),
            ct);

        var scriptReading = await WaitForJournalAsync(
            scriptId,
            reading => reading.AllSaid<TaskCreated>().Count == 1
                && reading.AllSaid<BehaviorNudge>().Count == 1,
            "VipEmailToTask said TaskCreated and BehaviorNudge",
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

        var gmailReading = await ReadAsync(gmailId, ct);
        var emailSaid = gmailReading.SaidSingle<EmailReceived>();
        Assert.Equal("declared", emailSaid.DeliveryTo(scriptId).Via);
        Assert.Equal(messageId, Assert.IsType<EmailReceived>(emailSaid.Body).MessageId);

        var emailHeard = scriptReading.HeardSingle<EmailReceived>();
        Assert.Equal(gmailId, emailHeard.Metadata.Source);
        Assert.Equal(emailSaid.Position, emailHeard.Metadata.Sequence);

        var taskSaid = scriptReading.SaidSingle<TaskCreated>();
        Assert.Equal(new SynapseRef(gmailId, emailSaid.Position), taskSaid.Cause);
        Assert.Equal("declared", taskSaid.DeliveryTo(taskStoreId).Via);
        var task = Assert.IsType<TaskCreated>(taskSaid.Body);
        Assert.Equal($"task-{messageId}", task.TaskId);
        Assert.Equal(subject, task.Title);
        Assert.Equal("vip", task.Tag);

        var nudgeSaid = scriptReading.SaidSingle<BehaviorNudge>();
        Assert.Equal(new SynapseRef(gmailId, emailSaid.Position), nudgeSaid.Cause);
        Assert.Equal("declared", nudgeSaid.DeliveryTo(uiId).Via);
        Assert.Equal(behaviorId, Assert.IsType<BehaviorNudge>(nudgeSaid.Body).BehaviorId);

        Assert.Equal(scriptId, taskStoreReading.HeardSingle<TaskCreated>().Metadata.Source);
        Assert.Equal(scriptId, uiReading.HeardSingle<BehaviorNudge>().Metadata.Source);

        // Session order: install propose commits before the live VIP ObserveEmail proof.
        var sessionJournal = await ReadAsync(session.Id, ct);
        Assert.True(
            sessionJournal.SaidSingle<BehaviorInstallProposed>().Position
            < sessionJournal.SaidSingle<ObserveEmail>().Position);
    }

    [Fact(DisplayName =
        "Install C# behavior (composition includes script neuron): non-VIP EmailReceived stays silent")]
    public async Task NonVipEmailDoesNotCreateTask()
    {
        var ct = Cancellation;
        var context = "owner-behavior-noise";
        var session = Brain.Session(context);
        var scriptId = new NeuronId("vipemailtotask", context);

        await session.EmitAsync(
            new BehaviorInstallProposed(
                VipEmailToTask.BehaviorId,
                ScriptKind: "vipemailtotask",
                Listens: ["emailreceived"]),
            ct);

        await WaitForJournalAsync(
            new NeuronId("behaviorcatalog", context),
            reading => reading.AllSaid<BehaviorActivated>().Count == 1,
            "BehaviorActivated",
            ct);

        await session.EmitAsync(
            new ObserveEmail("msg-noise", "friend@x.com", "x.com", "Hello", "hi"),
            ct);

        var scriptReading = await WaitForJournalAsync(
            scriptId,
            reading => reading.AllHeard<EmailReceived>().Count == 1,
            "script heard non-VIP email",
            ct);

        Assert.Empty(scriptReading.AllSaid<TaskCreated>());
        Assert.Empty(scriptReading.AllSaid<BehaviorNudge>());
    }
}
