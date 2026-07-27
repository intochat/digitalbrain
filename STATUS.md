# DigitalBrain — status report

Generated at the end of the 7-slice session that took the repo from `c0e9d696` to `9c11e9f9`.

**This file is deliberately untracked.** `CLAUDE.md` bans progress reports and session logs from the
repository, and you ratified "code is the source of truth". It is written here because you asked for
it directly. Commit it or delete it — it is not part of the product.

---

## 1. Gates

Every number below was read from command output, not estimated.

| Gate | Result |
| --- | --- |
| `dotnet build DigitalBrain.slnx -c Release` | 0 errors, 0 warnings |
| `dotnet test DigitalBrain.slnx -c Release` | **13/13 projects, 239 passed, 0 failed** |
| `dart test` (`digitalbrain_flutter`) | 18/18 |
| `dart test` (`digitalbrain_wire`) | 4/4 |
| `flutter test` (`shell`) | 5/5 |
| `npm --prefix docs test` | 7/7 |
| `npm --prefix docs run build` | build complete |
| `aspire doctor` | 5/5 pass |

Per-project .NET counts: Behaviors 64 · Tests 53 · ModuleTests 21 · Integrations 21 · Time 19 ·
TestingTests 17 · Compositions 10 · Flutter 9 · Ui 8 · Tasks 6 · HostTests 6 · **Os.Bdd 4** ·
Quickstart 1.

---

## 2. Commits

| SHA | Slice |
| --- | --- |
| `1c841564` | Repo cleanup to .NET shape |
| `40c1c03c` | Real BDD (Reqnroll xUnit v3) |
| `e21dac4b` | Capability-tool seam |
| `458f3a52` | Chat module + behaviour program |
| `376c3c39` | Flutter chat + UI edge |
| `9c11e9f9` | AppHost composition + MCP introspection |

---

## 3. What works — verified live

The product AppHost was started and driven end to end. This is the causal chain, read back out of
the durable journals through `digitalbrain-mcp`:

```
chat:dev/main        seq 1 UserMessaged          corr=a9f1a726…
                     seq 2 AssistantResponded    corr=a9f1a726…

assistant:dev/…      seq 1 CapabilityRequested
                     seq 2 CapabilityFailed
                     seq 3 CapabilityToolSelected
```

Note `os-chat-responder:dev/a9f1a726-…` — the behaviour instance is keyed by the same correlation id
as the chat turn, which is the documented broadcast-receiver mechanism doing exactly what it says.

Confirmed working:

- HTTP POST to the UI edge → `ChatNeuron` → `UserMessaged` journaled.
- OS behaviour reacts to the fact (no host code involved).
- Behaviour calls the assistant; the assistant offers its capability schema to llama3.2.
- **The model's tool call reached a real neuron capability** — `CapabilityRequested` is kernel-journaled.
- Failure was journaled (`CapabilityFailed` — Gmail refused without credentials, as expected).
- The answer returned as a directed fact → `AssistantResponded` on the conversation's own journal.
- All of it readable afterwards through MCP.

Resource health at run time: silo Healthy (6 modules selected), Azurite + journal/clustering/reminders
Healthy, Ollama + llama3.2 Healthy, MCP host Healthy on `:5000`, UI 200 on `/health`, website Healthy.

---

## 4. Does llama3.2 support tool calling?

**Mechanically: yes. Behaviourally: it selects badly, and this is now measured rather than predicted.**

The model, served by `ollama/ollama:0.13.0` with tag `llama3.2`, emitted `FunctionCallContent`,
`FunctionInvokingChatClient` executed it, and the model's own argument values arrived at a real
neuron. The seam is proven by the `CapabilityRequested` fact in the assistant journal.

The problem is *when* it calls. With exactly one tool registered, it called that tool on **2 of 2**
prompts, including one that asked for nothing but a greeting. It never chose to simply answer.

Sample size is two. That is enough to say the failure is real and reproducible, not enough to quote a
rate — do not read "100%" into it.

Why this is likely: llama3.2 is a 3B-class model. Deciding *not* to invoke an available tool is a
known weak spot at that size; a single registered tool makes it worse, because "call the tool" is the
only structured action on offer.

This is the risk flagged before implementation, and you chose llama3.2 knowingly. Consequences:

- The seam is model-agnostic. Switching to `IGpt56` is a parameter and a keyed-service change, not a rewrite.
- A mis-selection is a **journaled fact**, not a silent no-op — that is the whole reason
  `CapabilityToolSelected` exists, since kernel facts deliberately exclude arguments.
- Mitigations worth trying before swapping models: add a plain "answer the user" tool so declining
  has a shape; supply a system instruction about when not to call; or register tools only when the
  conversation plausibly needs them.

---

## 5. Prompts used for testing

Be careful with this distinction — **only two prompts ever reached a real model.** Everything else
used `ScriptedChatClient`, a deterministic test double. Those tests prove plumbing, journaling and
behaviour wiring. They prove nothing whatsoever about llama3.2.

### 5.1 Against real llama3.2 (live AppHost)

| # | Prompt | Endpoint | Model's actual behaviour |
| --- | --- | --- | --- |
| 1 | `Say READY and nothing else.` | `POST /chats/main/messages` | Called `enrich_account_from_email`. Reply: *"The function `enrich_account_from_email` encountered an error. Please try again or check the API documentation for this function."* |
| 2 | `Hi there. Just say hello back.` | `POST /chats/probe/messages` | Called `enrich_account_from_email`. Reply: *"It looks like the tool was unable to process your request. Could you please provide more context or clarify which account you would like to enrich?"* |

Both prompts were chosen precisely because **neither implies enrichment** — prompt 1 forbids extra
output, prompt 2 asks only for a greeting. A correctly-selecting model answers both without any tool.

Not yet tested live: a prompt that *should* trigger enrichment (e.g. *"enrich account 001… from my
latest email"*). It would fail at Gmail regardless without credentials, so it measures nothing extra
until you supply them.

### 5.2 Scripted — capability-tool seam (`tests/DigitalBrain.ModuleTests/CapabilityToolSeam.cs`)

User text, with the model's reply scripted:

- `enrich my account from the latest email` — script replies with a tool call carrying
  `accountId=001AAAAAAAAAAAAAAA`, `messageId=msg-42`, then `Account updated.`
- `enrich my account` — same tool call; asserts `CapabilityToolSelected` is journaled.
- `hello` — script replies with text only; asserts **no** `CapabilityToolSelected` is journaled.

### 5.3 Scripted — BDD (`tests/DigitalBrain.Os.Bdd.Tests/Features/Chat.feature`)

- `how is my account?` → scripted reply `Your account is up to date.`
- `first question` → `First answer.`
- `second question` → `Second answer.` (asserts a 4-turn transcript, i.e. memory across turns)

### 5.4 Flutter widget tests (`shell/test/chat_screen_test.dart`)

No model at all — turns are injected into the stream: `how is my account?`,
`Your account is up to date.`, `only once` (duplicate-sequence guard), `enrich my account`, `Done.`

---

## 6. What is NOT built

Stated plainly so nothing here reads as shipped.

- **The Behavior install rail.** No proposal, approval, content-addressed artifact, installer,
  rollback, or capability broker. Chat is *behaviour-shaped, not behaviour-installed*: the program is
  a real `IIntentProgram` composed at build time, and `ChatResponderNeuron` is the pre-rail host that
  the rail will later replace. `ChatResponder.cs` itself will not change.
- **Live Google and Salesforce.** Never exercised. No credentials in this environment, by your
  decision. `CapabilityFailed` in the live run *was* Gmail refusing.
- **Approval UI.** Salesforce proposals correctly stop at `AwaitingApproval`, but nothing in chat yet
  lets you approve one.
- **Recurring/calendar Time**, vector discovery, canonical neuron catalog.
- **Application telemetry to the Aspire dashboard** — see finding 3.

---

## 7. Findings you should act on

**1 — llama3.2 tool selection.** Covered in §4. Decide whether to mitigate or switch model before
demoing.

**2 — `DOTNET_ROOT` is misconfigured on this machine.** It points at a location containing only
.NET 10.0.10, while .NET 11 preview 6 lives under `C:\Program Files\dotnet`. `dotnet build`/`test`
work because they resolve through the CLI; **`aspire run` fails**, because the AppHost executable
resolves through `DOTNET_ROOT`:

```
You must install or update .NET to run this application.
Framework: 'Microsoft.NETCore.App', version '11.0.0-preview.6.26359.118' (x64)
```

I overrode it for one process only and did not change your machine. Fix it before your clean-build run.

**3 — No application traces reach the Aspire dashboard.** `list_traces` returned only `dotnet-cli`
spans; no silo spans. The double-verification path you wanted via Aspire telemetry is therefore not
wired — the journal through `digitalbrain-mcp` was the working route. Consistent with the ratified
rule that telemetry is a projection and never the audit source, but it is a gap if you want it.

**4 — A kernel constraint that shaped the design.** `DrainAsync` awaits `Deliver` *inside the
emitting neuron's turn*, and `NeuronConcurrency.RequireSerializedTurns` forbids reentrancy. So a
handler that calls back synchronously into the neuron that emitted its trigger **deadlocks**. The
first chat design did exactly that and hung indefinitely. Facts now flow one way: `UserMessaged`
carries the transcript, and the answer returns as a directed `AssistantAnswered`. Anyone writing a
future behaviour needs to know this.

**5 — One conversation is single-threaded end to end.** While a turn is being answered, the chat
neuron is occupied for the whole LLM + capability chain. Fine for one owner in dev; it is a real
constraint at scale.

---

## 8. MCP tools now available

Against a running AppHost, `http://localhost:5000/mcp` exposes:

- `list_active_neurons` — grain type, identity, silo for everything currently activated.
- `read_neuron_journal(grainType, name, kind, afterSequence)` — causal facts only: synapse type,
  caller, correlation, timestamp. Never arguments, matching what the kernel journals.
- `read_chat_transcript(chatName)` — the conversation as the owner sees it.
- `ask_llama32` — pre-existing.

---

## 9. Reproducing the live run

```powershell
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'      # until finding 2 is fixed
aspire run --project hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj

Invoke-WebRequest -Uri 'http://localhost:5080/chats/main/messages' `
  -Method Post -ContentType 'application/json' `
  -Body '{"text":"Say READY and nothing else."}'
```

Then read the result back through the MCP tools in §8.

The Flutter desktop client starts automatically once `digitalbrain-ui` is healthy and opens straight
into the conversation named by `DIGITALBRAIN_CHAT` (default `main`).
