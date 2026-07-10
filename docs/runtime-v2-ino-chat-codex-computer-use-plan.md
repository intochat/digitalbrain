# Runtime V2 INO Chat — Codex Implementation and Computer Use Plan

Date: 2026-07-10  
Status: Ready to execute  
Scope: Minimal principal-private INO chat vertical slice in the Flutter Runtime V2 application

## Objective

Implement the smallest Runtime V2 slice in which an authenticated user can send a text prompt to INO, immediately see the user turn, follow durable queued/running/responding state, receive a non-canned answer, recover across reconnect/restart, and safely retry when the server declares retry appropriate.

Use two complementary verification lanes:

1. Deterministic C# and Flutter tests as the release gate.
2. Codex Computer Use for human-like QA of the running native Windows Flutter application.

Computer Use discovers realistic UI problems. Every repeatable defect must become an automated regression test before it is fixed.

## Non-negotiable constraints

- Keep Runtime V2 server-driven and workspace-scoped.
- Keep INO conversations principal-private by default.
- Sharing a result to the workspace must be an explicit durable action.
- Use authenticated V2 command admission and `WatchSurfaceFeed`.
- Do not introduce the legacy gateway, `WatchHomeFeed`, HomeFeed buses, or V1 routes.
- Treat each INO command as a durable operation with visible effect state.
- Never render or record credentials, tokens, grants, opaque identifiers, feed metadata, endpoints, or infrastructure details.
- Do not let clients provide tenant, workspace, principal, grant, or operation authority.

## Minimal product slice

Include:

- One authenticated workspace and principal.
- One principal-private conversation.
- Text prompts only.
- A first-run explanation and INO composer.
- Durable `queued -> running -> responding -> succeeded/failed` state.
- Safe retry and ambiguous-outcome reconciliation.
- Reconnect and restart recovery.
- Basic keyboard, semantics, and desktop layout behavior.

Exclude initially:

- Connectors and external authorization.
- Attachments and voice input.
- Conversation search, archive, or rename.
- Multi-workspace switching UI.
- Broad Home/Connectors/Settings implementation.
- Destructive or externally consequential actions.

The initial INO conversation surface may double as the first-run Home surface. This avoids building a complete navigation system before chat works.

## Current baseline

At plan creation:

- The Aspire AppHost and native Windows Flutter resource were running.
- Runtime V2 rendered a generic authenticated workspace surface.
- Flutter had V2 unit/widget tests but no SDK `integration_test` dependency or integration-test directory.
- INO returned a canned completion instead of executing a real conversation.
- Flutter rendered only the latest surface and discarded returned operation/idempotency information.
- INO results were published to the workspace audience while Flutter watched the principal audience.

Re-check all runtime state at execution time. Never assume the AppHost is still running.

## Execution checkpoints

| Checkpoint | Implementation actions | Exit gate |
|---|---|---|
| **0. Baseline** | Attach Computer Use to the existing Flutter window. Record the current authenticated surface and absence of chat. Run existing V2 tests. | Sanitized baseline report and clean current tests. |
| **1. Scope safety** | Principal-scope operations and idempotency. Atomically clear surfaces, current surface, actions, drafts, operation cache, notifications, and feed cursors on identity change. Make exact replay return the original operation. | Same-workspace/different-principal isolation tests and exact replay tests pass. |
| **2. Durable conversation** | Replace the canned INO handler. Persist the user turn, effect state, assistant result, and safe failure. Execute through the V2 conversation owner. | One non-canned answer; restart cannot duplicate either turn. |
| **3. Principal chat surface** | Add a principal `inoConversation` surface and bound `ino.send` action. Publish queued/running/responding/terminal revisions through `WatchSurfaceFeed`. | Initiating principal receives the conversation; another principal receives nothing; reconnect restores it. |
| **4. Flutter chat UI** | Add typed conversation rendering, transcript, composer, inline operation/effect state, retry, offline banner, focus preservation, and semantics. | Widget test covers type, send, queued, running, and final response. |
| **5. Retry and failure recovery** | Reconcile lost acceptance responses, distinguish safe failure from unknown outcome, and recover operations stranded in `Applying`. | One logical operation and one assistant turn after retry/reconnect. |
| **6. Windows integration test** | Add Flutter SDK `integration_test`, stable widget keys, deterministic test scenarios, and a Windows integration test. | Windows integration test passes without coordinate selectors or arbitrary long sleeps. |
| **7. Live Computer Use QA** | Relaunch safely, replay the same user journey, capture sanitized evidence, and turn discoveries into regression tests. | Two consecutive clean Computer Use replays plus all automated tests. |

## Checkpoint 1 — Scope and idempotency foundations

Primary files:

- `src/DigitalBrain.Core/V2ApplicationService.cs`
- `src/DigitalBrain.Core/V2UiRuntime.cs`
- `src/DigitalBrain.Mcp/V2UiGrpcService.cs`
- `tests/DigitalBrain.Tests/V2/V2ContractsTests.cs`
- `tests/DigitalBrain.Tests/V2/V2UiTransportTests.cs`

Actions:

1. Scope operation ownership and operation queries by tenant, workspace, principal kind, and principal value.
2. Scope idempotency by the same identity plus command type and an input fingerprint.
3. Persist enough submission receipt information to reconcile a lost acceptance response.
4. Return the original operation/status for exact replay of the same request.
5. Reject reuse of the same key with changed input as a conflict.
6. Define recovery for operations found in `Applying` after restart. Never blindly repeat an external effect.
7. Prevent any operation, idempotency, principal, or scope value from entering renderable product copy.

Required tests:

- Same principal and same input converge on one operation.
- Changed input cannot alias the original operation.
- Different principals in the same workspace cannot read or reconcile one another's operations.
- The same principal value with a different principal kind remains isolated.
- Identity rebinding immediately clears every prior-scope Flutter surface and action.

## Checkpoint 2 — Durable conversation execution

Primary files:

- `src/DigitalBrain.Core/V2Conversation.cs`
- `src/DigitalBrain.Mcp/V2McpIno.cs`
- `src/DigitalBrain.Mcp/V2CommandExecutionWorker.cs`
- `src/DigitalBrain.Core/V2CommandDispatcher.cs`
- Optionally, a focused `src/DigitalBrain.Core/V2ConversationProjection.cs`

Persist, under authenticated scope:

- User turn accepted.
- Operation/effect queued.
- Effect applying.
- Assistant response in progress.
- Assistant result persisted.
- Effect succeeded.
- Safe retryable or permanent failure.
- Outcome unknown/manual intervention.

The public action input remains limited to:

```json
{
  "prompt": "What can you help me with?"
}
```

Conversation identity is server-derived for this slice.

Handler sequence:

1. Validate the prompt.
2. Append the user turn once.
3. Publish queued state.
4. Invoke `V2ConversationOwner.ExecuteAsync` through registered production V2 ports.
5. Persist/project running and responding state.
6. Persist the assistant turn before marking the operation successful.
7. Project a safe failure state when execution fails.
8. Store final surface intent durably so replay can repair a missing publication.

Tools may remain disabled for the first slice while the configured model supplies a real response. Connector/tool effects can later extend the same durable operation model.

## Checkpoint 3 — Principal-private server surface

Primary file:

- `src/DigitalBrain.Core/V2SurfaceProjection.cs`

Replace workspace-audience INO publication with a stable principal conversation surface.

Suggested semantic payload:

```text
kind: inoConversation
workspaceLabel: Personal workspace
intro: Ask INO about this workspace
messages:
  - role: user | assistant
    text: safe display text
    state: sending | queued | running | responding | done | failed
operation:
  state: queued | running | responding | succeeded | failed | outcomeUnknown
  safeReason: optional
  retryable: true | false
```

Add a principal-bound action:

- Binding: `ino.send`
- Action type: `ino.interact`
- Input schema: prompt only
- Surface: stable principal conversation slot
- Uses: one logical submission, with exact replay reconciliation

Publish coalesced full-surface revisions for queued, running, responding, and terminal state. Do not durably write one revision per model token.

Required behavior:

- The initiating principal receives the conversation.
- Another principal in the workspace receives no private turn or operation.
- A reconnect snapshot restores the recent transcript and current operation state.
- A workspace update cannot replace the chat viewport.
- Explicit future sharing produces a separate workspace-audience summary, not a change to the private chat audience.

## Checkpoint 4 — Flutter conversation UI

Primary files:

- `app/lib/v2/protocol/surface_protocol.dart`
- `app/lib/v2/v2_runtime.dart`
- `app/lib/v2/widgets/v2_runtime_shell.dart`
- `app/lib/v2/widgets/v2_surface_view.dart`
- New `app/lib/v2/widgets/v2_ino_conversation_view.dart`
- New `app/lib/v2/widgets/v2_ino_composer.dart`

Actions:

1. Decode `inoConversation` into a typed presentation model.
2. Route it to a dedicated compiled Flutter renderer.
3. Render welcome copy, transcript, inline operation/effect state, composer, Retry when safe, and human-readable errors.
4. Immediately show a submitted user turn as `Sending`.
5. Use the accepted action result to advance it to `Queued` without displaying returned identifiers.
6. Preserve the draft, focus, and scroll position across feed revisions.
7. Disable only the action currently being submitted.
8. Retain exact ambiguous submissions for reconciliation.
9. Clear every rendered and actionable value atomically when authenticated scope changes.
10. Keep a draft during reconnect, disable Send, and show a persistent offline/reconnecting banner.
11. Remove protected content when authentication expires and show a clear sign-in recovery state.
12. Add stable keys and semantics for the workspace context, transcript, turn author, turn status, composer, Send, Retry, navigation, and connection banner.
13. Do not announce every streamed fragment to assistive technologies.

## Test strategy

| Layer | Responsibility |
|---|---|
| **C# contract tests** | Principal isolation, durable turns/effects, idempotency, restart recovery, feed audience, and absence of legacy composition. |
| **Dart controller tests** | Scope teardown, reconnect/reset, action receipt, ambiguous retry, feed slots, and authentication expiry. |
| **Flutter widget tests** | Composer, transcript, operation states, errors, focus, semantics, empty state, and compact/expanded layouts. |
| **Windows `integration_test`** | Complete in-app user journey on a real Flutter desktop build. |
| **Computer Use** | Native window behavior, resizing, keyboard navigation, visual hierarchy, and live assembled-stack behavior. |

### Backend tests

- Update the existing canned INO test to expect a principal conversation surface.
- Verify the handler uses the submitted prompt and the V2 conversation owner.
- Verify queued, applying, responding, and terminal revisions arrive in order.
- Verify exact retry returns the original operation and creates one user/assistant turn.
- Verify restart restores transcript and operation state.
- Verify retryable failure offers Retry while permanent or unknown outcome does not.
- Verify same-workspace/different-principal isolation.
- Verify the same principal value with a different principal kind is isolated.
- Verify authenticated V2 action -> durable operation -> handler -> principal `WatchSurfaceFeed`.
- Assert that no legacy gateway, V1 route, HomeFeed bus, or `WatchHomeFeed` dependency is introduced.

### Flutter tests

- `app/test/v2/v2_surface_protocol_test.dart`
  - Decode valid conversation payloads.
  - Reject malformed or sensitive payloads.
- `app/test/v2/v2_surface_view_test.dart`
  - Render transcript, composer, states, safe errors, Retry, and semantics.
- `app/test/v2/v2_runtime_controller_test.dart`
  - Track accepted action receipt, reconnect, exact retry, and atomic identity clearing.
- `app/test/v2/v2_runtime_shell_test.dart`
  - Cover first-run, offline, authentication expiry, and restored conversation.
- `app/test/v2/v2_grpc_transport_test.dart`
  - Map exact replay to the original operation rather than a generic error.

### Windows integration test

Add to `app/pubspec.yaml`:

```yaml
dev_dependencies:
  integration_test:
    sdk: flutter
```

Create:

```text
app/integration_test/runtime_v2_chat_test.dart
```

Use:

- `IntegrationTestWidgetsFlutterBinding.ensureInitialized()`.
- Stable widget keys and semantic labels.
- Bounded polling for state transitions.
- Fresh isolated state per test.
- Deterministic server scenarios for success, retryable failure, lost acknowledgement, reconnect, authentication expiry, and scope change.
- Assertions on user-visible state and counts, never opaque values.

Do not use coordinate selectors or arbitrary long sleeps.

The deterministic scenario must preserve authenticated V2 command admission, durable operations, and `WatchSurfaceFeed`. Do not add a UI-only success bypass.

## Safe Aspire and test lifecycle

### Baseline while the existing app is running

```powershell
aspire wait flutter-ui --non-interactive

Push-Location app
flutter test test/v2
Pop-Location
```

### Backend implementation cycle

Core/MCP changes can affect assemblies held by running Aspire resources. Stop Aspire cleanly before building or testing those changes.

```powershell
aspire stop --non-interactive

dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj `
  --filter "FullyQualifiedName~DigitalBrain.Tests.V2"

Push-Location app
flutter analyze lib/v2 test/v2 integration_test
flutter test test/v2
flutter test integration_test/runtime_v2_chat_test.dart -d windows
Pop-Location

aspire start --non-interactive
aspire wait mcp --non-interactive
aspire wait flutter-ui --non-interactive
```

Run the full relevant .NET and Flutter suites before declaring the slice complete.

### Flutter-only iteration

When backend/AppHost code has not changed:

```powershell
Push-Location app
flutter analyze lib/v2 test/v2 integration_test
flutter test test/v2
Pop-Location

aspire resource flutter-ui restart --non-interactive
aspire wait flutter-ui --non-interactive
```

Rules:

- Use `aspire start`, never `dotnet run` on the AppHost.
- Use `aspire wait`, never manual HTTP polling.
- Do not kill Aspire or Flutter processes manually.
- Do not delete `bin` or `obj` to work around locks.
- Do not start a second AppHost while one is running.
- Do not paste raw Aspire status, dashboard URLs, or environment values into QA evidence.

## Codex Computer Use setup

1. In the Codex desktop app, install and enable the Computer Use plugin.
2. Enable its server and skill toggles.
3. Keep the native Flutter window visible on the active Windows desktop.
4. Keep the computer unlocked and connected.
5. Close unrelated applications containing sensitive information.
6. Approve only the target Flutter executable reported by Computer Use.
7. Do not use the desktop concurrently while Computer Use is active; Windows Computer Use controls the foreground pointer and keyboard.
8. Do not silently substitute Flutter Web or the built-in browser if Computer Use cannot see the native app. Desktop focus, window resizing, and keyboard behavior are part of this test.

If Computer Use is unavailable, continue deterministic implementation/testing and pause native visual QA.

## Copy-paste Computer Use prompt

```text
@Computer

Test the currently running DigitalBrain Flutter Windows app as a real user.

Scope:
- Use only the visible native Flutter app.
- Do not edit code, open developer dashboards, inspect network payloads, or restart services.
- Never reveal, copy, record, or screenshot credentials, tokens, grants, opaque identifiers,
  feed metadata, endpoints, or infrastructure details.
- If authentication requires a secret, pause and ask me to enter it. Resume only when the
  secret is no longer visible.
- Do not authorize connectors or perform actions against real external services.
- Do not retry an ambiguous result unless the UI explicitly presents a safe Retry action.

Before testing:
1. Focus the Flutter app.
2. Record the visible window size.
3. Take a safe baseline screenshot.
4. Classify the visible state as authentication, loading, workspace, offline, or error.

Run these journeys in order:

1. First run
- Confirm the friendly workspace context is visible.
- Confirm the screen explains what the workspace and INO do.
- Confirm no protocol or infrastructure terminology is visible.
- Confirm there is a clear INO composer or Ask INO action.

2. Happy-path chat
- Type exactly: “What can you help me with in this workspace?”
- Submit once.
- Verify the user message appears immediately.
- Observe queued, working/responding, and final states in order.
- Verify one final assistant response and no duplicate message.
- Verify focus returns to the composer.
- Take safe screenshots of accepted, working, and final states.

3. Composer behavior
- Verify an empty message cannot be submitted.
- Enter two lines using Shift+Enter.
- Navigate away and back before sending; verify draft behavior is understandable.
- Submit once and rapidly activate Send again; verify one logical request.

4. Navigation and background work
- Start: “Summarize the recent activity in this workspace.”
- While it is running, visit Home and Activity, then return to INO.
- Verify progress continues without forced navigation.
- Verify Activity represents the same operation in user language.
- Inspect Connectors and Settings without starting an authorization flow.

5. Safe retry
- Only if the configured QA scenario produces a retryable failure, verify a plain-language
  reason and one Retry action.
- Activate Retry once.
- Verify one logical request resumes and no duplicate assistant turn appears.
- If the result is ambiguous or says it is checking status, do not retry.

6. Keyboard and layout
- Navigate with Tab, Shift+Tab, Enter, Space, and Escape.
- Verify visible focus, logical order, accessible names, and focus returning to the composer.
- Resize to approximately 800x600, 1024x768, and 1440x900.
- Check for clipping, overlap, lost scroll position, or inaccessible composer/actions.

For every checkpoint report:
- scenario and step
- expected visible result
- actual visible result
- elapsed time
- pass, fail, or block
- severity: P0, P1, or P2
- safe screenshot name
- recommended automated regression-test layer

Continue past non-blocking issues. Stop immediately under the supplied stop rules.
```

## Computer Use flow matrix

| ID | Flow | Required visible outcome | Automated handoff |
|---|---|---|---|
| `BOOT-01` | Authenticate and hydrate | Friendly workspace and INO entry; no technical metadata. | Shell/widget integration test. |
| `CHAT-01` | Send the first prompt | User turn -> queued -> working/responding -> final. | First vertical-slice integration test. |
| `COMPOSER-01` | Empty, multiline, and repeated Send | Correct validation and exactly one submission. | Widget and integration tests. |
| `ACTION-01` | Start work and visit Activity | Same durable work appears in chat and Activity. | Full-stack integration test. |
| `RETRY-01` | Scripted safe failure | One Retry action and one logical result. | Backend idempotency plus integration test. |
| `UNKNOWN-01` | Lost acknowledgement | Checking status; no blind retry. | Transport/controller test. |
| `RECONNECT-01` | Controlled feed interruption | Draft/transcript retained and no duplicate result. | Full-stack integration test. |
| `AUTH-01` | Scripted session expiry | Protected content removed; clear sign-in recovery. | Integration test. |
| `NAV-01` | Home -> INO -> Activity | Stable workspace context and preserved conversation. | Integration test. |
| `SCOPE-01` | Synthetic scope A -> B -> A | Immediate teardown; no A content visible in B. | P0 backend and Flutter isolation tests. |
| `EMPTY-01` | Fresh synthetic workspace | Helpful empty state and primary next action. | Widget test. |
| `A11Y-01` | Keyboard traversal | Visible focus, logical order, useful names, no color-only state. | Widget semantics plus Computer Use. |
| `LAYOUT-01` | Resize the desktop window | No clipping or inaccessible controls. | Widget viewport matrix. |

A single Computer Use session cannot prove principal isolation. Use two isolated synthetic sessions in backend/full-stack tests.

## Evidence and bug reports

Before execution, add the QA artifact directory to `.gitignore` if it is not already ignored.

Suggested transient structure:

```text
qa-artifacts/runtime-v2/
  report.md
  BOOT-01_01_authenticated.png
  CHAT-01_01_accepted.png
  CHAT-01_02_working.png
  CHAT-01_03_complete.png
```

Report template:

```markdown
### [P0] CHAT-01 — No actionable INO composer after authentication

Build: <commit only>
Platform: Windows / Flutter version / window size / display scale
Fixture: synthetic first-run workspace
Reproduction: 3/3

Steps:
1. Authenticate.
2. Wait for workspace hydration.
3. Look for the primary INO entry point.

Expected:
A composer or clear Ask INO action.

Actual:
Only a generic workspace surface is visible.

Observed transition:
authenticated -> generic surface; no drafting state

Evidence:
- CHAT-01_01_authenticated.png
- Safe log excerpt by timestamp, with every opaque value removed

Security/privacy impact:
None observed.

Regression handoff:
app/integration_test/runtime_v2_chat_test.dart — first authenticated surface supports an INO turn
```

Never place credentials, endpoints, grants, tenant/workspace/principal values, operation values, trace values, feed metadata, or screenshots of secret entry into QA artifacts.

## Stop and continue rules

Stop the entire Computer Use run immediately when:

- Content from another workspace or principal appears.
- A credential, token, grant, opaque identifier, or infrastructure detail becomes visible.
- Authentication can be bypassed.
- An action might affect a real connector or external system.
- The app repeatedly crashes or loses a protected-state boundary.
- Computer Use cannot attach to the native Windows Flutter window.
- Computer Use begins interacting with the wrong application.

Do not screenshot an active secret or privacy leak. Record only a sanitized textual description.

Stop only the current scenario and continue independent checks when:

- The expected screen does not appear within 30 seconds after authentication.
- Queued state exceeds 10 seconds without explanation.
- Running/responding exceeds 90 seconds without progress or cancellation guidance.
- Reconnection exceeds 30 seconds.
- A required feature is absent. A missing composer blocks chat scenarios but not shell, layout, or privacy review.

Never activate Retry after an ambiguous outcome. Retry only when the server-driven UI explicitly presents a safe Retry action.

## Defect loop

For every Computer Use defect:

1. Save only sanitized, transient evidence.
2. Reproduce the issue twice.
3. Add the smallest failing backend, controller, widget, or integration test.
4. Implement the smallest fix.
5. Run targeted tests.
6. Run the complete V2 Flutter suite.
7. Restart only the affected Aspire resources, unless Core/AppHost changes require a full clean stop/start.
8. Replay the identical Computer Use scenario twice.
9. Mark the issue complete only after automated and visual verification agree.

## Definition of done

- A user can submit a prompt from the authenticated workspace.
- The user message appears immediately.
- Queued and running/responding states are visible.
- INO returns a non-canned response.
- The composer remains usable and focus behaves predictably.
- Restart and reconnect restore the conversation without duplication.
- Exact submission retry resolves to the same operation.
- Failures use safe user language and offer Retry only when allowed.
- Another principal cannot see the chat or private operation.
- Authentication expiry removes protected content.
- No token, grant, identifier, feed metadata, endpoint, or internal action type is displayed.
- No V1 or legacy feed path participates.
- Automated tests pass.
- Two consecutive Computer Use replays pass.

## References

- [OpenAI Computer Use documentation](https://learn.chatgpt.com/docs/computer-use)
- [OpenAI: QA your app with Computer Use](https://learn.chatgpt.com/use-cases/qa-your-app-with-computer-use)
- [Flutter testing overview](https://docs.flutter.dev/testing/overview)
- [Flutter integration testing](https://docs.flutter.dev/testing/integration-tests)
- `docs/adr-v2-runtime.md`
- `docs/v2-completion-audit.md`

