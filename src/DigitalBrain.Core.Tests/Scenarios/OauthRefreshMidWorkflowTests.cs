using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class OauthRefreshMidWorkflowTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ExpenseWorkflow>()
            .AddModule<ExpenseDriveConnector>()
            .AddModule<ExpenseWorkflowLedger>();

    [Fact(DisplayName =
        "OAuth refresh mid-workflow: Drive AuthorizationRequired pauses; AuthorizationGranted resumes without re-fetching Gmail; ExpenseFiled once")]
    public async Task PauseOnAuthResumeWithoutRefetchGmail()
    {
        var ct = Cancellation;
        var context = "expense-19";
        var session = Brain.Session(context);
        var workflowId = new NeuronId("expenseworkflow", context);
        var driveId = new NeuronId("expensedriveconnector", context);
        var ledgerId = new NeuronId("expenseworkflowledger", context);
        var expenseId = "exp-19";

        await session.EmitAsync(new ExpenseWorkflowStart(expenseId, ReceiptMessageId: "msg-receipt"), ct);

        var paused = await WaitForJournalAsync(
            workflowId,
            reading => reading.AllSaid<ExpenseGmailFetched>().Count == 1
                && reading.AllSaid<AuthorizationRequired>().Count == 1
                && reading.AllSaid<ExpenseWorkflowPaused>().Count == 1
                && reading.AllHeard<ExpenseDriveUploadFailed>().Count == 1,
            "gmail fetched then drive auth pause",
            ct);

        Assert.Single(paused.AllSaid<ExpenseGmailFetched>());
        Assert.Empty(paused.AllSaid<ExpenseFiled>());
        Assert.Equal("drive", Assert.IsType<ExpenseWorkflowPaused>(
            paused.SaidSingle<ExpenseWorkflowPaused>().Body).AtStep);
        var authReq = Assert.IsType<AuthorizationRequired>(
            paused.SaidSingle<AuthorizationRequired>().Body);
        Assert.Equal("drive.file", authReq.Scope);
        Assert.Equal($"auth-{expenseId}", authReq.CorrelationId);

        var drivePaused = await WaitForJournalAsync(
            driveId,
            reading => reading.AllSaid<ExpenseDriveUploadFailed>().Count == 1
                && reading.AllHeard<ExpenseDriveUploadAsked>().Count == 1,
            "drive holds pending upload after auth fail",
            ct);
        Assert.Empty(drivePaused.AllSaid<ExpenseDriveUploaded>());

        // Owner completes OAuth — ambient grant reaches drive + workflow + ledger.
        await session.EmitAsync(
            new AuthorizationGranted(authReq.CorrelationId, Scope: "drive.file"),
            ct);

        var driveDone = await WaitForJournalAsync(
            driveId,
            reading => reading.AllHeard<AuthorizationGranted>().Count == 1
                && reading.AllSaid<ExpenseDriveUploaded>().Count == 1,
            "drive heard grant and completed pending upload",
            ct);

        var uploadedSaid = driveDone.SaidSingle<ExpenseDriveUploaded>();
        Assert.Equal("declared", uploadedSaid.DeliveryTo(workflowId).Via);
        Assert.Equal(expenseId, Assert.IsType<ExpenseDriveUploaded>(uploadedSaid.Body).ExpenseId);

        var filed = await WaitForJournalAsync(
            workflowId,
            reading => reading.AllSaid<ExpenseFiled>().Count == 1
                && reading.AllHeard<ExpenseDriveUploaded>().Count == 1
                && reading.AllHeard<AuthorizationGranted>().Count == 1,
            "workflow files expense after drive upload",
            ct);

        // Gmail fetched exactly once — no restart from scratch.
        Assert.Single(filed.AllSaid<ExpenseGmailFetched>());
        Assert.Equal(
            "blob-msg-receipt",
            Assert.IsType<ExpenseGmailFetched>(filed.SaidSingle<ExpenseGmailFetched>().Body).BlobRef);
        Assert.Single(filed.AllSaid<ExpenseDriveUploadAsked>());

        var filedSaid = filed.SaidSingle<ExpenseFiled>();
        Assert.Equal("declared", filedSaid.DeliveryTo(ledgerId).Via);
        Assert.Equal($"fin-{expenseId}", Assert.IsType<ExpenseFiled>(filedSaid.Body).FinanceId);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<ExpenseFiled>().Count == 1
                && reading.AllHeard<AuthorizationRequired>().Count == 1
                && reading.AllHeard<AuthorizationGranted>().Count == 1
                && reading.AllHeard<ExpenseGmailFetched>().Count == 1,
            "ledger heard pause + grant + complete",
            ct);
        Assert.Equal(workflowId, ledgerReading.HeardSingle<ExpenseFiled>().Metadata.Source);
    }
}
