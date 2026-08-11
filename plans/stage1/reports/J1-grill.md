# J1-GRILL — adversarial review report

## What was reviewed
Uncommitted working-tree change implementing `plans/stage1/briefs/J1-godswitch.md`, against worker report `plans/stage1/reports/J1-godswitch.md`.

Touched (tracked) paths:
- `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/IChat.cs`
- `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/Responded.cs`
- `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ShowTime.cs` (deleted)
- `src/Modules/UI/DigitalBrain.Modules.UI/Button/ButtonActivatedToShowTime.cs` (deleted)
- `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs`
- `src/Modules/UI/DigitalBrain.Modules.UI/UiModule.cs`
- `src/Tests/DigitalBrain.Tests/ChatNoteProofs.cs`
- `src/Tests/DigitalBrain.Tests/ChatResponderConnectionProofs.cs`
- `src/Tests/DigitalBrain.Tests/ChatShowTimeProofs.cs` (deleted)
- `src/Tests/DigitalBrain.Tests/ConnectionLifecycleProofs.cs`

## Attack results

### Scope
**Pass.** Every tracked source edit is inside the brief's in-scope set (UI Chat + contracts + module DI hook, demo tests, `Responded` + construction sites). No Message/Reply rename, no Flutter, no packages, no auth/OAuth/Execution, no git. No drive-by refactors smuggled in.

### Kernel traps
- **Trap 9 (god-switch):** `WantsTimeButton` branch, `ShowTime` handler, constants, and arming helper are gone from `Chat.cs`. Pass.
- **Trap 8 (broadcast catalog):** `IHandle<ShowTime>` removed from `IChat` together with the `ShowTime` type — fewer ghosts, not more. Pass.
- **Trap 2 (zero-receiver emissions):** The deleted path was the *producer* of arming connections + button→chat transforms. Nothing still emits `chat.show-time`. Sign-in buttons still use URL actions + `ChatButtons.OfferedInstanceName` click routing (`MapOwnerCommands`), not the demo arming path. Pass.
- **Trap 4 (settled refusals):** Note/timer-card/sign-in still refuse via `NeuronAuthorizationException`. No new retried-failure footguns. Pass.

### Survivors (timer / note / responder)
| Behavior | Proof that still exists |
|---|---|
| `ui.note` → transcript line + `Author` | `ChatNoteProofs.NotePostsALineIntoTheTranscript` |
| `ui.timer-card` → clock offer + `Author` | `ChatNoteProofs.TimerCardPostsAClockOfferIntoTheTranscript` |
| Bound responder reply + `Author` | `ChatResponderConnectionProofs.EachChatAnswersThroughItsOwnBoundResponder`, `…ReconnectingAResponder…` |
| Real timer → graph → chat clock card | `TimerProofs.ScheduledTimerPostsAClockCardIntoChatThroughTheGraph` |
| Real timer elapse → graph → note in chat | `TimerProofs.ElapsedTimerPostsItsNoteIntoChatThroughTheGraph` |
| Timer module itself | `TimerProofs` suite + capability/manifest proofs (untouched) |

Timer feature is intact; only the keyword demo shortcut died. Pass.

### Completeness (`WantsTimeButton` / `ShowTime` / demo transform)
Strict scan under `src/` for `WantsTimeButton`, `ShowTime`, `ButtonActivatedToShowTime`, and `chat.show-time`: **zero type/symbol hits**.

Deleted demo-only artifacts match the brief:
- `ShowTime` contract
- `ButtonActivatedToShowTime` transform
- DI registration in `UiModule`
- `ChatShowTimeProofs` + the demo-dependent `OfferedButtonConnectionsCarryAnExpiry` test

Graph-level connection expiry remains covered by `ConnectionLifecycleProofs.AnExpiredConnectionLeavesTheGraph` (probe path, not chat-offer path).

### `Responded.Author`
- Wire alias unchanged: `[Alias("chat.responded")]`.
- Additive field: `[property: Id(6)] string Author = ""`.
- **Every** construction site sets Author explicitly:
  1. `Chat.cs:77` send path — `bound.Target.Name` or `"assistant"`
  2. `Chat.cs:109` note — `Id.Name`
  3. `Chat.cs:125` timer-card — `Id.Name`
  4. `Chat.cs:152` AuthorizationRequired sign-in — `Id.Name`
- Representation is the minimal string the brief asked for (agent/chat instance name), not an identity system.

### Quality
No leftover commented-out code, no TODO litter, no dead usings introduced. `UiModule` retained as empty `IModule` host marker (reasonable; Aspire/discovery).

### Gate (verified by this session, not trusted from worker alone)
```
Build succeeded.
    0 Error(s)
(Time Elapsed ~5s; only non-C# node NO_COLOR noise from AppHost CodeGraph sync)

=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 74, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 36.057s
EXIT_CODE=0
```
Commands: `dotnet build DigitalBrain.slnx` then `& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe`.

## Conflicts & risks noted (none blocked approval)
Worker correctly left docs outside `src/`, Flutter gallery fixtures, and MCP description text out of scope. Dead `ChatButtons.OfferLifetime` / `ArmingConnectionId` are residual public helpers with no remaining callers under `src/` — not demo contracts, not a brief DoD miss.

## Out of scope for this grill
Did not fix anything. Did not run git write commands. Did not run AppHost/aspire.

---

## VERDICT: APPROVE

Findings (no BLOCKERs):

1. **MINOR** — `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Button/ChatButtons.cs:11,21`: `OfferLifetime` and `ArmingConnectionId` have zero remaining callers under `src/` after demo arming deletion. Dead public helpers, not banned demo contracts; safe to leave for a later cleanup.

2. **MINOR** — `src/Kernel/DigitalBrain.Mcp/ChatTools.cs:71`: Description string still cites example action `'show-time'`. Not a `ShowTime`/`WantsTimeButton`/`ButtonActivatedToShowTime` reference; kernel text is outside the worker's granted scope, but demo vocabulary residue remains.

3. **MINOR** — `src/Tests/DigitalBrain.Tests/ChatSignInOfferProofs.cs:30-34`: Sign-in construction path sets `Author: Id.Name` in code but tests do not pin `Author` (only buttons). Coverage hole, not a correctness hole.

4. **MINOR** — No characterization pin that default (unbound) responder replies carry `Author: "assistant"`; only bound responder names (`alpha`/`beta`) are asserted.

5. **MINOR** — Flutter kit fixtures still use `show-time` as sample button id/action (`kit_gallery_screen.dart`, `kit_part_test.dart`). Explicitly out of brief scope; not the C# keyword god-switch.

6. **MINOR** — Repo docs outside `src/` (`CLAUDE.md`, `UNIFIED-ARCHITECTURE.md`, plans) still name the deleted demo. Out of scope for J1; orchestrator/docs pass later.
