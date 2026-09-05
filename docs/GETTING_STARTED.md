# Use DigitalBrain from Flutter

From the repository root, start the local application:

```powershell
aspire start --apphost src/Aspire/DigitalBrain.AppHost --non-interactive
```

The AppHost starts the kernel, Flutter Windows, and the separate C# scripting worker.
Docker must be running for the local storage services. Configure the AppHost's
`Parameters:openai-api-key` user secret for its selected model. The current AppHost
already selects the model; Flutter uses the kernel at `http://localhost:5080`.

## Chat beside the brain

Open **Graph** for a chat pane on the left and the module graph on the right.
It uses the same UI-kit chat surface and conversation as **Chat**. Use the example
buttons to play an illustrative chat reply, code review, or subscription sequence.
Pause or reset the playback to inspect a step; drag the 3D scene to orbit and scroll
to zoom. The subscription example creates a Bound synapse on the source, broadcasts
along it, then removes it on unsubscribe.

Examples are local simulations. They do not call the model, run a review, or
change your real subscriptions. Messages sent in the chat pane use the real
assistant and its configured tools.

## Review your code now

In Flutter, ask:

> Review my local repository diff. Focus on correctness, concurrency and durable state.
> Give actionable findings with file and line references. Skip cosmetic comments.

Development startup enables `read_repository_diff` for this checkout. It reads
staged and unstaged tracked changes against HEAD. Ask for "staged changes only"
to review the index instead. Its output identifies the repository, lists untracked
filenames without reading their contents, and reports truncation. It does not edit
files, commit, or post anything remotely.

To select another checkout, set the AppHost configuration
`DigitalBrain:Workspace:RepositoryPath` before starting. Outside development,
repository access is disabled unless the host explicitly configures both the path
and its `DigitalBrain:Workspace:Owner`. The model cannot supply a filesystem path
or a shell command to this tool.

## Save your own review routine

Ask in Flutter:

> Create a C# behavior called personal-review. Review the configured repository for
> concurrency bugs, missed cancellation, and violations of subscribe/unsubscribe.
> Use a separate named reviewer chat and post the findings into this chat. Show me the C#.

The assistant writes ordinary C# and calls `admit_behavior`. The worker compiles
and runs that source outside the silo. Use "show personal-review" to inspect the
exact saved source and its status/diagnostics. Use "run personal-review again" to
read and re-admit the same source as a new revision. "Remove personal-review"
removes the definition and requests cancellation if it is still running.

The complete [personal review script](examples/personal-code-review.csx) is checked
by an integration test. It submits a normal chat turn:

```csharp
var reviewer = Brain.Get<IChat>($"{chatName}.code-review");
var accepted = await reviewer.RequestAsync(
    new SendMessage(command, myReviewInstructions, actor), CancellationToken);
// Watch this reviewer's outgoing journal for Responded matching command.
// Forward answer.Text to the original chat with SendAsync(new Note(answer.Text)).
```

The assistant fills in the current chat's exact instance name, including its
principal prefix. `myReviewInstructions` contains your preferences and asks the
reviewer to read the configured repository. Chat accepts the turn before the model
finishes, so the owner can keep sending messages. The example also handles a failed
turn, a lost journal window, and cancellation. This finite script completes after
posting its result; completed scripts are not automatically rerun on restart.

## Custom triggers and logic

A long-running behavior can use loops, conditions, files, HTTP, typed signals and
journal watches. `Brain` and `CancellationToken` are supplied globals; AI, Chat,
Time, UI and kernel contracts are referenced by the compiler. For example, the body
of a script can observe new timer deliveries:

```csharp
var timer = Brain.Get<DigitalBrain.Time.ITimer>(timerName);
var cursor = (await timer.ReadJournalAsync(
    JournalKind.Outgoing, long.MaxValue, CancellationToken)).ResumeSequence;

await foreach (var page in timer.WatchJournalAsync(
    JournalKind.Outgoing, cursor, CancellationToken))
{
    if (page.ResetSnapshot is not null)
        throw new InvalidOperationException("Timer traffic was missed; inspect before resuming.");

    foreach (var delivery in page.Delta)
        if (delivery.Signal is TimerElapsed elapsed)
        {
            // Your C# condition, review request, or entity update goes here.
        }
}
```

This observes an existing timer. Arm it separately with `StartTimer`; the script
decides what to do when it elapses. Journal watching supplies custom script logic.
`SubscribeToAsync` and `UnsubscribeFromAsync` instead wire existing typed neurons
that can handle the chosen signal; Broadcast follows those source-owned synapses.
Scripts do not generate new Orleans neuron types.

Current definitions are durable state on the existing `BehaviorsNeuron`; journal
retention cannot erase them. A running revision is resumed when the worker restarts.
Scripts should tolerate replay/restart and use idempotent writes where appropriate.
Pass `CancellationToken` to waits and requests, and undo subscriptions created by
your script in `finally` when their lifetime should match the script. Cancellation
is cooperative: the worker cannot forcibly stop arbitrary C# that ignores its token.

Behavior status distinguishes Admitted, Running, Completed and Failed. Admitted
means saved; compilation errors and runtime failures appear in the saved status.
The current worker serves the configured owner and runs trusted owner code with
the scripting process's permissions.

Definitions admitted before current-definition storage was introduced must be
re-admitted from their C# source. The worker does not revive historical source
from old traffic entries.
