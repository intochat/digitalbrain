# J1 — RED+GREEN report
## What changed
- `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` — removed `WantsTimeButton`/`ShowTime` keyword demo path, arming helper, and ShowTime handler; threaded `Author` on every `Responded` emit (responder name on send path; chat instance name for note/timer/sign-in)
- `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/Responded.cs` — additive `Author` field (`Id(6)`, default `""`)
- `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/IChat.cs` — dropped `IHandle<ShowTime>`
- `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ShowTime.cs` — deleted (demo-only contract)
- `src/Modules/UI/DigitalBrain.Modules.UI/Button/ButtonActivatedToShowTime.cs` — deleted (demo-only transform)
- `src/Modules/UI/DigitalBrain.Modules.UI/UiModule.cs` — removed sole demo transform registration; hook class kept (hosting type marker; Configure empty)
- `src/Tests/DigitalBrain.Tests/ChatShowTimeProofs.cs` — deleted (demo-only)
- `src/Tests/DigitalBrain.Tests/ConnectionLifecycleProofs.cs` — removed `OfferedButtonConnectionsCarryAnExpiry` (depended on keyword demo arming)
- `src/Tests/DigitalBrain.Tests/ChatResponderConnectionProofs.cs` — pin `Author` from bound responder name
- `src/Tests/DigitalBrain.Tests/ChatNoteProofs.cs` — pin `Author: "main"` on note and timer-card replies

## Tests
- Extended: `ChatResponderConnectionProofs.EachChatAnswersThroughItsOwnBoundResponder` — asserts `Author: "alpha"` / `"beta"`
- Extended: `ChatResponderConnectionProofs.ReconnectingAResponderReplacesInsteadOfAccumulating` — asserts `Author: "beta"`
- Extended: `ChatNoteProofs.NotePostsALineIntoTheTranscript` — asserts `Author: "main"`
- Extended: `ChatNoteProofs.TimerCardPostsAClockOfferIntoTheTranscript` — asserts `Author: "main"`
- Deleted: `ChatShowTimeProofs.OfferedTimeButtonAnswersIntoItsChatThroughItsArmedBinding`
- Deleted: `ConnectionLifecycleProofs.OfferedButtonConnectionsCarryAnExpiry`
- No PIN-DEFECT flips

## Gate
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:07.19

=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 74, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 36.230s
EXIT_CODE=0
```
(via `dotnet build DigitalBrain.slnx` then `src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe`)

Zero references under `src/` to `WantsTimeButton`, `ShowTime`, or `ButtonActivatedToShowTime`.

## Conflicts & risks
- `UiModule.Configure` is now a no-op; class retained because Aspire hosting uses `DigitalBrainModuleBuilder<UiModule>` / assembly discovery.
- `ChatButtons.OfferedInstanceName` remains (used by `MapOwnerCommands` for click routing). `OfferLifetime` / `ArmingConnectionId` are now unreferenced under `src/` after demo arming died — left as public button vocabulary, not deleted (not demo-only contracts).
- `Author` is a free string (agent/chat instance name), not a full identity system — matches brief; identity seam is later.
- Docs outside `src/` (`CLAUDE.md`, `UNIFIED-ARCHITECTURE.md`, plans) still mention the deleted demo by name; left untouched (out of scope).

## Out of scope
- Message/Reply rename (deferred W2 item)
- Flutter kit/gallery sample `show-time` button fixtures
- Kernel `ChatTools` description text example `'show-time'`
- SSE `MapChatStreams` / MCP result DTOs not extended with `Author` (read-only consumers; not construction sites)
- Timer module (`time.*`) — left intact
- Auth, OAuth, Execution/Tasks, packages, git
