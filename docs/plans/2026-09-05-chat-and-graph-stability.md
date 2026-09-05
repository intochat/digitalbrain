# Chat recovery and modular graph

## Intended result

The existing Windows chat must answer using its saved conversation. The Graph
page places the same UI-kit chat surface on the left and DigitalBrain on the
right. The graph groups neurons into modules and offers explicitly simulated
examples showing signal delivery, source-owned synapses, subscribe and unsubscribe.

## Plan

1. Reproduce a native chat send and compare Aspire traces with DigitalBrain MCP.
2. Repair the concrete failure without deleting saved conversations. Carry failed
   and cancelled turn outcomes through HTTP/SSE into a visible chat message.
3. Stabilize client stream reconciliation and disposal, including later messages
   in a conversation and navigation while a response is pending.
4. Extract the common chat view into the UI kit. Build a desktop split Graph page
   with module groups, a 3D scene, and deterministic example playback controls.
5. Verify focused backend and Flutter tests; rebuild and exercise Chat, Graph chat,
   example playback, pause/reset, and repeated page navigation in the native app.

## Initial evidence

- Native `main` chat: clicking **My behaviors** produced another user message,
  then an empty assistant bubble after the thinking indicator ended.
- Aspire records command `4cf18570ea2542bbb7b7c670aa5a15c3` failing on
  2026-09-04 at 22:21:46 UTC. Its saved ExecutionContext refers to the old
  `DigitalBrain.Abstractions.Execution.ExecutionId` type. Failure precedes the
  model call. The earlier user command failed for the same reason.
- A fresh DigitalBrain MCP chat returned `DIGITALBRAIN_MCP_OK` in about three
  seconds; Aspire trace `824cdc7336a48ba80907eec7e940f5dd` corroborates it.
- HTTP currently closes failed/cancelled streams without an error frame; MCP
  already exposes the terminal failure. These paths need equivalent outcomes.
- The chat SSE record emits `signal`, while Flutter requires `synapse`. Its
  parser catches the resulting type error and drops every chat event. Raw HTTP
  includes the full history, but switching to Graph displays no messages. Rename
  the client field to the correct domain term and test current server-shaped JSON.
- The existing 3D view loads its graph once; playback requires scene updates when
  graph topology changes. Simulated topology remains presentation data, not a new
  runtime or an alternative synapse store.

## Backend investigation and repair

Aspire and DigitalBrain MCP agree that the configured OpenAI connection is healthy.
The fresh MCP command `4e119212-d6cf-4ce0-b95e-e87601756d4d` in `codex-diagnosis`
returned `DIGITALBRAIN_MCP_OK`; trace `824cdc7336a48ba80907eec7e940f5dd` completed
in 2,970 ms with no trace error. Its `chat gpt-5.6-luna` span took 1,058 ms and
the OpenAI HTTP spans returned 200. MCP uses its own principal partition, so this
successful fresh conversation did not exercise the saved HTTP owner's `main` context.

The HTTP owner's outgoing journal confirms native command
`4cf18570ea2542bbb7b7c670aa5a15c3` reached Pending, Running, then Failed at sequence
51. Its execution tried to inherit context `c6cb97aac99447f084b4c47172731dcd`,
whose JSON metadata still named execution types from `DigitalBrain.Abstractions`.
The earlier commands `3d89969f9eee4718a318e5a38ceaa77c` and
`5253d91037ee4a19b3686fd45330f3b1` failed for the same reason. This path failed
before calling OpenAI. Aspire storage logs 645/731 expose the same serialization
failure. Fresh-chat-only smoke tests therefore missed the existing-conversation bug.

The Execution module now translates only the six known historical context types
in JSON type metadata, including slot collections. Saved data is preserved; new
writes use the current module names. Typed chat and voice share one SSE observer
that emits `chat-error` with `message`, `status`, `turnId`, and `commandId` for a
failed/cancelled or answerless turn. It captures the outgoing cursor before the
send, and reports journal gaps or observer timeouts instead of silently ending.
The request still owns only its observer; disconnecting does not cancel durable work.
The stream first emits `chat-accepted` with the server's `commandId` and `turnId`,
so the client can reconcile optimistic rows with the exact persisted command.

The focused execution-context storage regressions pass (3/3): historical list and
array snapshots, nested value types, unchanged payload text, current writes, and
unknown-type rejection. The tests use the newly built test executable directly;
the `dotnet test` launcher had a handshake failure before running any tests.
The SSE wire contract regression also passes (1/1), checking acceptance IDs,
polymorphic text content, explicit failure data, and exclusion of internal stack
details from the chat error frame. The test project and all referenced service
projects build with zero warnings and errors.

After rebuilding and restarting AppHost, MCP command
`2e7b3666-6610-43ac-a85a-a2f9f408a1cc` in `codex-diagnosis` returned
`MCP-RECOVERED`. Aspire trace `dafec40937064e385e83af01dcf2be87` took 29,994 ms
overall with no trace error; its model span took 4,630 ms and both OpenAI HTTP
spans returned 200. This remains a different principal from native `main` and
is evidence of the MCP/provider path, not proof of the historical-context repair.

The decisive native check succeeded in the original HTTP owner's preserved `main`
conversation. Command `3862f8668b6240e1a7a44e44d2f341c4`, sent from the Windows Chat
page at 22:47:27 UTC, asked to list admitted C# behaviors. Its outgoing journal
records `Responded` at sequence 56 (22:47:31.778 UTC), with the correct reply that
no behaviors are admitted, followed by `Completed` at sequence 57. No historical
`JsonSerializationException` appears in the new AppHost's logs. This validates
the saved-context repair without deleting the conversation or its prior context.
Its Aspire trace `a53db302949380cbe14f97583be6eb2e` is the 5,444 ms
`POST /owner/commands` beginning at 22:47:26.851 UTC, with no error. All three
OpenAI HTTP calls returned 200, including the behavior-tool round trip.

The native Graph page's **Personal code review** shortcut also completed through
the real assistant. Command `bf0ac9ac4aac4e99bf97cf0ed692f4fd` began at
23:03:17.586 UTC, produced `Responded` at journal sequence 68, and reached
`Completed` at sequence 69 (23:03:40.503 UTC). Aspire trace
`acde0b82c28558832032a64bdfd1dbf1` lasted 23,112 ms with no error and contains an
actual `execute_tool read_repository_diff` span (`1e6f80a27b3b686c`, 4,179 ms).
All OpenAI HTTP calls returned 200. The answer identified the configured checkout,
branch and HEAD, and disclosed the truncated patch and omitted untracked contents.
This verifies tool invocation and message delivery; its generated code findings
were not independently validated, and included speculation about omitted code.

## Architecture constraints

Modules contain neurons. A synapse belongs to its source. Subscribe creates a
Bound synapse; unsubscribe removes it. Simulation broadcasts only through existing
edges, and clearly labels illustrative traffic. Real chat continues through the
existing queued turn path. No catalog, second execution runtime, or synapse grain.

## Native Graph verification

The rebuilt Windows app retains the original conversation across Chat, Graph,
and a client restart. A manually typed `hi`, submitted with the Graph composer,
received the visible answer `Hi! What would you like me to help with?`.

Computer Use verified the final scene with four labeled modules and seven neurons.
The subscription example played, paused at step 4/5, remained at that step during
a later observation, resumed to step 5/5, and reset to `Play an example`. The
unsubscribed observer had no outgoing delivery edge from Timer. The canvas uses
the kit background and exposes the three local examples beside real chat.

The final Flutter process has no exception logs. The earlier custom Aspire
hot-reload command was rejected by the Flutter VM; restarting the Flutter resource
successfully compiled and loaded the final UI. That hosting-command issue is
separate from chat delivery and remains outside this repair.

The final native orbit check exposed competing gesture recognizers: ThreeJS's
inner scale detector consumed drags before the kit's pan handler. The renderer
is now wrapped in `IgnorePointer`; the kit alone owns orbit, zoom, and selection.
A renderer with a competing scale handler is included in the passing regression.
The native input also coalesced a drag into one pointer move. The default pan
start discarded that displacement; `DragStartBehavior.down` preserves it. An
explicit one-move mouse test failed before this change and passed afterward.
Computer Use then verified the same 94-pixel drag rotates the real Windows graph.
Wheel zoom, the three examples, and the original conversation remain available.

For the final Dart-only change, a compiler-backed Flutter attach avoided another
Windows build. From the shell directory, attach to the current VM URL with
`flutter attach -d windows --debug-url=<current-vm-url> --no-dds --no-devtools -t lib/main.dart`.
After attaching to an already-running process, `r` reported zero changed libraries;
`R` loaded the new kernel successfully. `d` detached and left the app running.
Use the current Aspire resource's VM URL, not a saved port from an earlier run.

Focused Flutter checks passed: core 29, kit graph view 9, onboarding catalog 7,
Graph page 4, Activity 3, and combined chat/workspace 13 (9 chat, 4 workspace).
The new chat fixtures register controller closure in `addTearDown`; awaiting raw
close futures at the end of a fake-async widget callback caused a test harness
hang. Production cancellation remains explicitly verified by the disposal test.
The full Flutter workspace analyzer completed with no errors or warnings. Its
single redundant-import notice was fixed; the targeted recheck reported
`No issues found!`. `git diff --check` also passed. No Flutter caught/unhandled
exceptions appeared after the final native checks.
