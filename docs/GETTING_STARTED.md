# Use DigitalBrain from Flutter

From the repository root, start the local application:

```powershell
aspire start --apphost src/Aspire/DigitalBrain.AppHost --non-interactive
```

The AppHost starts the kernel, Flutter Windows, and the separate C# scripting worker.
Docker must be running for the local storage services. Configure the AppHost's
`Parameters:openai-api-key` user secret for its selected model. The current AppHost
already selects the model; Flutter uses the kernel at `http://localhost:5080`.

## Your brain, with Ino

Flutter opens **My brain**, the Lumen workspace built on the shared Forui kit.
Use the composer at the bottom to talk to Ino. The latest answer stays beside the
composer; **Full conversation** opens the complete history. **Conversation** in
the navigation uses the same conversation and pending request.

The graph groups actual observed neurons by module. Select an icon to inspect its
identity, recent signals, and connections. Select an arrow on a synapse to inspect
its source, subscriber, signal, kind, and recorded delivery count. The directory
provides a list alternative to the canvas. Pan and zoom to explore; use the reset
view control to return to the initial framing.

Choose **Create subscription** on a neuron, then select an eligible subscriber
and signal. The source owns the resulting Bound synapse. **Unsubscribe** removes
it entirely; the interface waits for the kernel and a fresh snapshot before
confirming either change. Learned connections represent handled direct delivery.

The graph refreshes approximately every two seconds after each completed read.
It shows the current conversation, known runtime participants, reachable stored
synapses, and bounded recent journal activity. The observation time, limited-view
status, and connection failures are visible. Direct tool calls that do not emit
journal signals are not represented as complete live traces. Provider icons appear
when those neurons are actually observed.

**Play an example** opens the labeled 3D simulations with pause/reset controls.
These demonstrations do not change real subscriptions or submit model requests.

## Ask Aspire about the running application

Ask Ino from the bottom composer:

> How many Aspire resources are healthy? List their names and distinguish health from process state.

Or investigate a failure:

> Ask Aspire to investigate recent kernel errors using the relevant traces and logs.

Ino delegates an ordinary `AgentRequest` to the Microsoft module's `IAspire` neuron.
That agent discovers the CLI's MCP tools and uses their published schemas. The
default local AppHost binds it to this DigitalBrain application under the instance
`<current-principal>.digitalbrain-local`. Selecting the Aspire icon shows actual
delegation/tool activity, timings, and bounded screened evidence. A dotted active
request is temporary; a handled request creates the normal Learned connection.
No Bound subscription or background monitor is created by asking a question.

The connection supports resources, logs and traces. It discovers running AppHosts
before selecting the explicitly configured project; MCP sessions are isolated by
agent identity. Stopping the AppHost makes the connection unavailable; the next
request reconnects after it is running. Failed operations are not automatically replayed.

An admitted C# behavior can use the same inherited contract:

```csharp
var aspire = Brain.Get<IAspire>("<current-principal>.digitalbrain-local");
var reply = await aspire.RequestAsync(
    new AgentRequest("How many resources are healthy? Use current evidence."),
    CancellationToken);
return reply.Text;
```

Replace the placeholder with the actual principal prefix shown in the neuron's
identity. Behaviors admitted from chat retain that principal's execution context.
Microsoft and AI contracts are already referenced by the script compiler. Use
ordinary C# conditions or existing signals for custom behavior; there is no
separate Aspire status DTO or per-tool signal.

## Inspect AI conversations in Aspire

The local Development AppHost enables AI content recording through
`ai.EnableSensitiveData`. The AI client reads the projected configuration key
`DigitalBrain:AI:Telemetry:EnableSensitiveData`; its default outside that explicit
opt-in is false. The hosting projection also sets
`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` consistently.

Open **Traces**, select a recent chat request, and open the GenAI view. The trace
contains `invoke_agent Ino`, delegated `invoke_agent Aspire · DigitalBrain`,
`chat <model>` calls, and `execute_tool <name>` calls. Agent spans carry
`gen_ai.conversation.id`, `gen_ai.agent.id`, and `db.command.id`. Model spans carry
the model, token counts, and—when enabled—input/output content. Tool spans capture
arguments and screened results under the same opt-in. Per-model metrics use
`DigitalBrain.AI.<model marker>` instrumentation.

Changing the setting requires restarting the processes; content omitted from an
older trace cannot be reconstructed by enabling it later. To inspect from the CLI,
use the actual telemetry service name `DigitalBrain.Silo`, or query across resources:

```powershell
aspire otel spans --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --search ask_aspire --limit 10 --format Json --non-interactive
```

See [Microsoft Learn's observability configuration](https://learn.microsoft.com/en-us/agent-framework/agents/observability)
and [Aspire's GenAI content configuration](https://aspire.dev/dashboard/explore/#configure-message-content-recording).

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
