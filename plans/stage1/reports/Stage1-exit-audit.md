# Stage 1 exit audit — production-source report

> **Status:** source audit and local AppHost smoke complete. The northbound MCP boundary and the
> external Salesforce callback registration remain before Stage 1 can be declared exited. No
> automated tests were created or run.

## What changed

| Area | Production change |
|------|-------------------|
| `ChatTurnWorker` | Removed the deleted suite's static fault-injection dictionary and fake external-operation branch. |
| Execution worker registry | Removed the nonexistent harness `"worker"` and duplicate chat-worker seeds; `UiModule` is the sole registration owner for `chat-turn-worker`. |
| Execution operation retention | Made the 64-row bound absolute: evict a completed/failed row or refuse a 65th operation when every retained effect is unresolved. Live effects are never dropped. |
| Project visibility | Removed three `InternalsVisibleTo DigitalBrain.Tests` grants left by the deleted central suite. |
| OAuth hub | Removed test-only global reset/count methods; retained bounded runtime waiter/session behavior. |
| Gmail definition | Replaced obsolete fake-test commentary with the real operator-credential requirement. |
| Backlog | Reconciled every janitor item as resolved, deliberately kept, or carried with a reason. Salesforce Contracts remains permanent. |
| Execution liveness | Added a worker-only lease protocol outside the public Apply/Read surface. A live chat worker renews the 15-second lease every 5 seconds; only the attributed active worker can renew it. |
| Authorization SSE | Filters the installation journal by the authenticated principal so one user cannot receive another user's OAuth state or sign-in URL. |

## P0 source evidence

| P0 | Verdict | Current production path |
|----|---------|-------------------------|
| **1 — OAuth state/PKCE/replay** | PASS | `McpAuthorizationRail.BeginNewAsync` is the sole state mint and calls `OAuthPkce.CreateS256Pair`; `McpAuthorizationNeuron` durably caps open states at 64, expires them after 15 minutes, and consumes completed code/verifier once through host-only `IMcpAuthorizationCodes`. Unknown hub states are dropped and completions are capped. |
| **2 — HTTP abort cancels work** | PASS | `MapOwnerCommands.StreamDeltasAsync` durably sends `SendMessage` before observing the journal. Only the observer uses `RequestAborted`; `Chat.SendStreaming` explicitly enqueues independently of its observer token. |
| **3 — unauthenticated/singleton caller** | PARTIAL / MCP BLOCKER | Kernel uses cookie Identity plus a require-authenticated fallback policy, Development-only loopback bypass, and HTTPS beyond loopback. Required anonymous exceptions are bootstrap/login/logout/me, OAuth callback, health, and liveness. Authorization SSE is principal-filtered. The separate northbound `DigitalBrain.Mcp` HTTP app still maps `/mcp` without auth. |
| **4 — client-trusted chat identity** | PARTIAL / MCP BLOCKER | Kernel maps local chat/surface names through `PrincipalScoped.InstanceName(principal, localName)` and derives `ActorContext` from claims. Northbound `ChatTools` and `ReadChatTranscript` still accept a bare chat name and stamp a fixed operator actor. |
| **5 — neuron-keyed OAuth tokens** | PASS for integration rail | `McpTokenPresence.SubjectKey(actor)` uses verified `PrincipalId`; `UserIntegration` creates `integration/user/{provider}/{principal:N}` protection purposes; begin/claim/code exchange verify the bound actor. No cross-user fallback exists. |
| **6 — MAF session lost mid-stream** | PASS with recorded residual | `DirectAgentSession` restores a protected versioned envelope, persists after every `FunctionResultContent` safe point and at stream completion, and refuses fingerprint drift without explicit migration/reset. A crash between an external effect and the following result safe point remains carried debt. |
| **7 — destructive-tool blanket block** | PASS | `McpServerNeuron` verifies the requested name exists in `tools/list` and calls it regardless of `DestructiveHint`; actor-scoped OAuth remains the boundary. Result-row fan-out is capped at 200. |
| **8 — Execution defects** | PASS after this diff | Apply/Read is the client surface; receipts and operation rows are capped at 64; operation identity is the caller's stable key rather than AttemptId; Dispatched-without-outcome becomes `OutcomeUncertain`; only explicit `ResolveOperation` can complete, fail, or permit retry; worker grain types are allow-listed. No `NotImplementedException` remains under `src/`. |
| **9 — rebuild locks** | PASS by gate discipline | `scripts/gate.ps1` refuses to build while any `DigitalBrain*` process is running. AppHost is stopped before every source gate. |

## Ratified spike paths through chat

| Scenario | Source trace | Live evidence |
|----------|--------------|---------------|
| Restart/worker death | `ExecutionNeuron.OnNeuronActivatedAsync` re-arms pending/cancel/retry work and liveness; `Chat.OnNeuronActivatedAsync` re-Reads the active Execution, cancels an orphaned in-memory worker, and advances FIFO only from the authoritative terminal snapshot. The chat worker now renews a worker-only lease rather than being declared dead merely because a healthy LLM call exceeds 15 seconds. | A live OAuth-triggering turn ran for 28.8 seconds and reached `Responded` + `Completed`; before the fix the same path reached `worker-abandoned` at about 15 seconds. Process-death recovery remains source-audited. |
| OAuth wait/resume | MCP call mints one actor-bound state and throws `AuthorizationRequired`; the authenticated principal's authorization stream projects the sign-in offer; anonymous provider callback durably completes that state; retry with the same command claims completion and exchanges/stores the token once. | Google produced a stateful PKCE-S256 URL and reached an `accounts.google.com` login redirect. Salesforce produced a stateful PKCE-S256 URL but the provider rejected the local callback with `redirect_uri_mismatch`; provider registration must be corrected by the owner. Real consent/token exchange was not fabricated. |
| Cancel | Chat requires an Actor, applies versioned `CancelExecution`, keeps the running turn as FIFO head, and advances only after the Execution terminal bridge. Dead cancelling workers are failed by durable liveness. | Pending AppHost smoke. |
| Reconnect | Chat, authorization, graph, and surface SSE routes accept `afterSequence`; journal reads return `ResumeSequence`; POST abort only detaches its watch. Authorization projection requires a matching durable actor stamp. | Real HTTP/SSE replay from sequence 0 returned each principal's own chat marker and never the other principal's marker. A Viewer initially reproduced the Owner OAuth-state leak; after the fix it received only the connection preamble while the Owner still received its event. |
| Duplicate submission | Chat retains CommandIds and refuses payload drift; Execution returns retained command receipts; terminal application is idempotent by Execution revision. | Source-audited. |
| Uncertain write | Execution marks any Dispatched operation Uncertain on cancellation, reminder retry, failed/abandoned attempt, and refuses further prepare/retry until explicit reconciliation. Chat surfaces Waiting and keeps FIFO head until its policy deadline. | Kernel path source-audited. Chat's generic MAF tool-effect safe-point window remains explicit debt and never auto-retries under its one-attempt policy. |

## Banned-pattern and route scans

- Zero production matches for `WantsTimeButton`, `ShowTime`, `show-time`, provider/action-specific
  synapses, `NotImplementedException`, or central `DigitalBrain.Tests` visibility.
- Zero Orleans Streams consumers (`GetStreamProvider`, `IAsyncStream`, `SubscribeAsync`, implicit
  subscriptions); provisioning-only references remain deliberately parked for Stage 2.
- Kernel anonymous routes are only the necessary auth bootstrap/session probes, OAuth callback,
  health, and liveness. `/orleans` inherits fallback authentication.
- One unresolved remote boundary is explicit: northbound `/mcp` is unauthenticated and its chat
  tools use unscoped names. Stage 1 cannot be signed off until that seam is fixed or the owner
  explicitly changes the exit criterion.

## Adversarial self-review

- The first operation-cap implementation only stopped pruning at a live oldest row and could grow
  beyond 64. This diff instead searches for any terminal row and refuses new work if all 64 rows
  are live, preserving both bounded state and effect history.
- Removing the duplicate Execution-module chat worker seed is safe because `UiModule` registers
  `ChatTurnWorker.GrainTypeName` before the DI registry is materialized. The real public
  `WorkerNeuron` adapter boundary remains available to future modules.
- No wire alias, package reference, Salesforce contract, webhook rail, or Behavior preview fixture
  changed.

## Gate

`pwsh scripts/gate.ps1 -Flutter`:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
core:  No issues found!
kit:   No issues found!
shell: No issues found!
GATE PASS
```

The build log includes `DigitalBrain.Modules.Salesforce.Contracts.dll`, proving the retained
contract boundary remains in the solution and compiles. No test command ran.

## Live AppHost smoke — 2026-08-11

- Aspire CLI started the AppHost; Kernel, northbound MCP, Flutter, Azurite, Qdrant, Ollama,
  `llama3.2`, and `gemma4:12b` all reported healthy.
- Bootstrap, Owner-created Viewer, Viewer login, and two distinct cookie principals returned 200.
- The two principals used the same local chat name but resolved to private transcripts: each SSE
  replay contained its own unique marker and not the other user's marker.
- The first pass exposed a false liveness failure: a healthy slow turn was marked
  `worker-abandoned` at the fixed 15-second reminder. After the worker-only lease change, a
  28.8-second turn reached `Responded` and `Completed`.
- The first pass also proved a Viewer could read the Owner's Salesforce and Google authorization
  events. After principal filtering, the Owner still received its Google event and the Viewer
  received no foreign event.
- Google authorization reached the real login redirect with state + PKCE S256. Salesforce reached
  the real authorize endpoint but returned `redirect_uri_mismatch`; this is external app
  registration, not a fabricated success.
- An unauthenticated MCP `initialize` POST returned 200 with server info and no
  `WWW-Authenticate` challenge, confirming the remaining northbound blocker.
- Aspire was stopped before the following source build/gate.

## Conflicts & risks

1. Exit criterion “zero unauthenticated endpoints” cannot literally include health/liveness,
   login/bootstrap, or an OAuth callback. The enforceable reading is zero *unintentionally*
   anonymous product/data endpoints, with the required exceptions enumerated above.
2. `DigitalBrain.Mcp` is currently an unintentionally anonymous product/data endpoint and also
   bypasses principal chat scoping. This is a real blocker, not a documentation exception.
3. Salesforce's registered callback does not match the generated local callback
   `http://localhost:5080/oauth/callback`. Code correctly projects the composed callback; the
   Salesforce External Client App registration must be updated before consent can complete.
4. Real Salesforce and Gmail completion cannot be manufactured headlessly; local smoke proved
   definition discovery and actor-bound sign-in reachability, while provider consent/token use
   requires owner participation.

## Out of scope

- No automated-test work; module-owned testing architecture is final-hardening scope.
- J4/J5 consolidation and the `"dev"` installation key rename are Stage 2.
- No graph/wire rename, project consolidation, new package, or Behavior build-out.
