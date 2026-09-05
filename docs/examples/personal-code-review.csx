// Replace this with the current chat's exact instance name from its conversation context.
// Keep the principal prefix. The assistant fills this in when saving the behavior.
var chatName = "__CHAT_INSTANCE__";

if (!PrincipalPartition.TryParse(chatName, out var principal, out _))
    throw new InvalidOperationException("Use the current chat's principal-qualified instance name.");

var actor = new ActorContext(principal, "_behavior");
var reviewer = Brain.Get<IChat>($"{chatName}.code-review");
var cursor = (await reviewer.ReadJournalAsync(
    JournalKind.Outgoing, long.MaxValue, CancellationToken)).ResumeSequence;
var command = CommandId.New();
// Chat accepts the turn quickly and runs the model on its existing worker.
// Waiting for a direct AgentRequest would hold the owner's send path until the model finishes.
var accepted = await reviewer.RequestAsync(new SendMessage(command, """
        Review the configured local repository's working-tree diff using read_repository_diff.
        My review preferences:
        - Prioritize correctness, concurrency, cancellation and durable-state mistakes.
        - Check that Broadcast follows existing synapses, and Unsubscribe removes the connection.
        - Flag nested IDigitalBrain calls from an active neuron turn, which can deadlock.
        - Prefer deleting obsolete paths over adding wrappers. Skip cosmetic style comments.
        Give actionable findings with file/line references and explain the failure they cause.
        If no concrete bugs are found, say so. Identify any truncated or unreviewed input.
        Do not modify files or post the review anywhere outside this chat.
        """, actor), CancellationToken);

try
{
    await foreach (var page in reviewer.WatchJournalAsync(JournalKind.Outgoing, cursor, CancellationToken))
    {
        if (page.ResetSnapshot is not null)
            throw new InvalidOperationException("Review traffic was missed; inspect the reviewer chat before retrying.");

        foreach (var delivery in page.Delta)
        {
            if (delivery.Signal is Responded answer && answer.CommandId == command)
            {
                await Brain.Get<IChat>(chatName).SendAsync(new Note(answer.Text), CancellationToken);
                return "Personal review posted to chat.";
            }
            if (delivery.Signal is TurnLifecycle turn && turn.TurnId == accepted.TurnId
                && turn.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
                throw new InvalidOperationException($"Review {turn.Status}: {turn.Detail}");
        }
    }
    throw new InvalidOperationException("The review journal ended before a result arrived.");
}
finally
{
    if (CancellationToken.IsCancellationRequested)
    {
        using var cleanup = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        await reviewer.SendAsync(new CancelTurn(CommandId.New(), accepted.TurnId, actor), cleanup.Token);
    }
}
