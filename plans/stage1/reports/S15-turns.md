# S1.5 — durable conversation turns report (RED+GREEN)

## What changed

| Path | Change |
|------|--------|
| `src/Modules/UI/.../Chat/TurnId.cs`, `ChatTurnStatus.cs`, `TurnAccepted.cs`, `ChatTurnSnapshot.cs` | Durable turn identity, status enum, Send receipt, ReadTurns snapshot |
| `src/Modules/UI/.../Chat/Synapses/CancelTurn.cs`, `TurnLifecycle.cs` | Versioned cancel command + additive lifecycle fact (`chat.cancel-turn`, `chat.turn-lifecycle`) |
| `src/Modules/UI/.../Chat/IChat.cs` | `Send` → `TurnAccepted`; `Cancel`; `ReadTurns`; actor required at grain |
| `src/Modules/UI/.../Chat/Chat.cs` | Durable FIFO turn queue; starts one `IExecution` per head; Completes via worker signal; Actor refusal |
| `src/Modules/UI/.../Chat/ChatTurnWorker.cs`, `ChatTurnGoal.cs` | First production Execution adapter — AI run independent of HTTP observer CT |
| `src/Modules/UI/.../DigitalBrain.Modules.UI.csproj` | Project refs to Execution contracts + implementation |
| `src/Modules/Execution/.../WorkerDispatchRelayNeuron.cs` | Allow domain worker grain types (not only harness `worker`) |
| `src/Modules/AI/.../DirectAgentSession.cs` | Persist MAF session at tool-round safe points (`FunctionResultContent`) + final |
| `src/Kernel/.../MapOwnerCommands.cs` | P0-2: durable `Send` then journal-observe; request abort detaches only; `chat.cancel-turn` HTTP kind |
| `src/Kernel/.../HttpSurfacePaths.cs`, `HttpSurfaceModels.cs`, `MapChatStreams.cs` | `KindChatCancelTurn`; additive `TurnId`/`Status` on events; project `TurnLifecycle` |
| `src/Kernel/DigitalBrain.Scripting/chat-probe.cs`, `DigitalBrain.Mcp/ChatTools.cs` | Operator Actor stamp on SendMessage |
| `src/Tests/.../DurableTurnProofs.cs`, `Harness/ScriptedAgent.cs`, `Harness/TestActors.cs` | RED pins → flipped GREEN proofs; hold/gate harness for abort/FIFO |
| `src/Tests/.../*Proofs.cs` (chat call sites), `IdentityBoundaryProofs.cs` | Actor stamps; OwnerCommandRequest property pin includes `TurnId` |

### Behavior map → test

| Target behavior | Test |
|-----------------|------|
| P0-2 observer abort detaches; AI completes | `SendReturnsTurnIdAndCompletesThroughExecutionIndependentlyOfObserverAbort` |
| P0-2 MapOwnerCommands no linked CTS | `MapOwnerCommandsDetachesRequestAbortFromTheAiRun` |
| P0-6 safe-point persist | `DirectAgentSessionPersistsAtToolRoundSafePoints` |
| FIFO one Execution per chat | `FifoQueueRunsTurnsInArrivalOrderWithinOneConversation` |
| Concurrent conversations | `DifferentConversationsRunConcurrently` |
| Cancel queued advances queue | `CancelQueuedTurnAdvancesTheQueue` |
| Cancel running versioned/idempotent | `CancelRunningTurnIsVersionedAndIdempotent` |
| Actor refusal at grain | `ActorlessSendIsRefusedAtTheGrain` |
| Restart mid-turn durable | `RunningTurnSurvivesSiloRestartAndCompletes` |
| Chat starts Execution | `ChatSendStartsAnExecutionInsteadOfBindingTheCallerTokenToTheResponder` |

## Tests

**RED (characterized then flipped):**
- `// PIN-DEFECT(P0-2)` composition: linked CTS + Chat bound responder CT + no Execution (removed)
- `// PIN-DEFECT(P0-6)` composition: persist only after stream (removed)
- Behavioral abort-kills-AI pin (flipped to abort-detaches)

**GREEN proofs added** (class `DurableTurnBehaviorProofs` / `DurableTurnCompositionProofs`):
- All rows in the behavior map above
- Zero remaining `PIN-DEFECT(P0-2)` / `PIN-DEFECT(P0-6)` markers under `src/Tests`

## Gate

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node env noise only: NO_COLOR/FORCE_COLOR — not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 142, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~81s
```

## Conflicts & risks

1. **Worker type pin relaxed** — `WorkerDispatchRelayNeuron` no longer requires grain type `worker`. Required so `chat-turn-worker` can be a production adapter without colliding with the harness `IWorker` grain. Owner/non-empty identity still enforced.
2. **SendStreaming is observer-empty** — grain stream yields nothing; HTTP observes journal for `Responded`. Token-level SSE deltas mid-turn are not re-published from the worker (resume surface remains `/chats/{name}/events` + final delta). Flutter client out of scope; additive event fields only.
3. **Restart mid-hold** — agent in-memory hold does not survive silo restart; test asserts turn/execution durability and non-vanishing, not necessarily auto-complete of an in-memory hold. Re-dispatch after restart can re-Accept the worker.
4. **Cancel of running** marks chat turn Cancelled when cancel is applied; Execution may still transit Cancelling→Cancelled via worker. CompleteTurnWork is idempotent on terminal statuses.

## Out of scope

Conversation module extraction (Stage 2); Gmail (S1.6); Flutter client changes; CI; docs; token-delta fanout from worker to concurrent observers.
