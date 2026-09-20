# Agent Data Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution, or superpowers:subagent-driven-development if the user selects delegation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A user asks the agent for Supabase data in chat; an interactive table window opens in the originating workspace, supports filtering/sorting/paging and survives refresh without duplicate windows.

**Architecture:** Flutter's workspace neuron owns logical windows, Supabase's existing table neuron owns query/view state, and Flutter renders those states using the existing windowing system. An application coordinator joins the production AI tool loop to those neurons; the browser E2E uses a deterministic HTTP model fixture and a real disposable database.

**Tech Stack:** Existing pinned .NET/Orleans, Microsoft.Extensions.AI/Microsoft.Agents.AI, Npgsql, Aspire, Flutter and Playwright. No library upgrades, live-account requirement or new UI framework.

**Spec:** [Approved design](../specs/2026-09-20-code-first-composition-and-agent-data-design.md). Depends on [Part 1: code-first composition](2026-09-20-code-first-composition.md), especially deferred hosting, provider endpoint transport and explicit E2E deployment choices. Read the [research](../../research/2026-09-20-ai-supabase-e2e-options.md) before implementation.

## Global Constraints

- Work on `archv2`; never push. Product implementation begins after the implementation plans are reviewed.
- Reuse Flutter's current windows/table controls and the real Supabase Npgsql provider.
- Use one workspace neuron initially; windows are descriptors rather than independent neurons.
- Persist logical window membership/reference/open state. Keep drag/resize geometry and focus device-local.
- Query, filters, sort and revisions belong to `ISupabaseTable`; do not copy its rows into another authoritative UI neuron.
- E2E starts the actual application and submits through the browser. Test setup seeds only database data and external model behavior, never application windows/results.
- Default CI uses a scripted model HTTP endpoint. A live model is an explicitly selected separate validation lane.
- Preserve the current single-owner BasicAuth/local-open deployment model. Scope by owner/workspace/thread in the application; do not claim this adds multi-tenant authorization.
- No row editing, Supabase REST/Auth/Realtime testing, arbitrary writes, whole legacy-session revival or unrelated UI redesign.
- Use `-p:CodeGraphRefresh=false`. Keep all failed runs and artifacts; do not make the scenario pass through retries.

## Review Focus

1. Table creation succeeds but opening its window fails; replay must resume without re-creating the table — task 3.
2. A result arrives after the user changes workspace, closes its window, or reconnects; no wrong-workspace focus, duplicate or stale reopening — tasks 1, 5, 6.
3. A filter matches only rows beyond the first fetched page; it must query the database and return correct counts — tasks 2 and 6.
4. Cancellation/client disconnect interrupts a tool or stream; no unfinished run claims success, and resources are released — tasks 4 and 6.
5. Module registration succeeds but required tools or model endpoint settings are missing; fail explicitly, never answer without the intended capability — tasks 3–4.

## Contract and file map

### Task 1: Add a durable workspace interface

**Create:** `src/Modules/Flutter/Contracts/Workspace/{IWorkspace,WorkspaceState,WorkspaceWindow,OpenWindow,TableViewReference}.cs`, `Workspace/Signals/WorkspaceChanged.cs`; `src/Modules/Flutter/Flutter/Workspace/WorkspaceNeuron.cs`; `src/Modules/Flutter/Tests.Unit/Workspace/WorkspaceFacts.cs`.

**Interfaces:** All neuron DTOs use the repository's explicit Orleans serialization IDs/aliases. The declarations below show domain shape; add the standard attributes following existing Flutter contracts.

```csharp
public sealed record TableViewReference(string Id);
public sealed record OpenWindow(
    string OperationId, string WindowId, string Title,
    TableViewReference View, long ExpectedRevision);
public sealed record WorkspaceWindow(
    string Id, string Title, TableViewReference View, bool IsOpen);
public sealed record WorkspaceState(
    long Revision, IReadOnlyList<WorkspaceWindow> Windows);
public interface IWorkspace : INeuron
{
    Task<WorkspaceState> Open(OpenWindow request);
    Task<WorkspaceState> Close(string windowId, long expectedRevision);
    Task<WorkspaceState> Read();
}
// Signal payload: WorkspaceId and Revision; clients read authoritative state.
```

`WorkspaceChanged` derives from the existing Signal type using its established record/serialization conventions. Define `WorkspaceRevisionConflictException` in the same contract folder. Window IDs are stable view-result IDs. Close retains the descriptor and duplicate-open receipt. Explicit reopen uses a fresh operation ID and current revision. Replaying a prior successful open returns current state without changing revision or open state, even after Close.

- [x] Write the failing test using the actual Unit host:

```csharp
await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
var workspace = brain.Get<IWorkspace>("owner/workspace-a");
var request = new OpenWindow("run1/call1", "view1", "Active leads", new("view1"), 0);
await workspace.Open(request);
await workspace.Open(request);
Assert.Single((await workspace.Read()).Windows);
await workspace.Close("view1", 1);
await workspace.Open(request);
Assert.False(Assert.Single((await workspace.Read()).Windows).IsOpen);
```

- [x] Add tests for explicit reopen, conflicting reuse of an operation ID, stale revision, empty/overlong identifiers/titles, separate workspace isolation and reactivation persistence. A reused operation ID with a different view is rejected. Name/title limits are 200 characters; operation/window IDs 256. Do not truncate silently.
- [x] Implement persisted workspace state and operation receipts in the existing default grain storage. Check duplicate operation before revision comparison. Persist before publishing the signal. Use revision checks to make stale commands visible. Do not add coordinates or window z-order to the neuron.
- [x] Verify a subscriber sees a change only after a successful mutation and that duplicate replay produces no extra version. Run Flutter Unit tests; commit the new interface/neuron.

### Task 2: Expose query-backed table and workspace contracts to Flutter

**Create:** `src/Applications/IntoChat/IntoChat/Workspace/{WorkspaceEndpoints,WorkspaceScope,WorkspaceTableAdapter}.cs`; `src/Applications/IntoChat/Tests/WorkspaceHttpFacts.cs`; `src/Modules/Flutter/app/core/lib/src/models/workspace_models.dart`, `src/Modules/Flutter/app/shell/test/core/workspace_models_test.dart`.

**Modify:** application `Program.cs` and `IntoChat.csproj` for active module references; tests `IntoChat.Tests.csproj` with an ordinary runtime assembly reference where needed (distinct from generated AppHost resource metadata); Flutter core `lib/src/ui_client.dart` and exports. Preserve `src/Modules/Flutter/app/core/lib/src/models/table_models.dart` where its shape already matches `SupabaseTableSnapshot`. Expose application internals to IntoChat.Tests through InternalsVisibleTo rather than making operation coordinators public solely for tests.

**Interfaces:** Scope-aware routes under `/workspaces/{workspaceId}`:

| Route | Contract |
|---|---|
| `GET /workspaces/{workspaceId}` | WorkspaceState |
| `GET /workspaces/{workspaceId}/events` | versioned change notifications; client reads latest state |
| `POST /workspaces/{workspaceId}/windows/{windowId}/close` | expectedRevision → WorkspaceState |
| `POST /workspaces/{workspaceId}/windows/{windowId}/reopen` | operationId + expectedRevision → WorkspaceState |
| `GET /workspaces/{workspaceId}/tables/{tableId}?offset=0&limit=25` | existing typed table snapshot, adapted to Flutter JSON casing |
| `POST /workspaces/{workspaceId}/tables/{tableId}/view` | existing expectedRevision/filters/sort/visibleColumns request → snapshot |

Scope derives the current configured owner and validated workspace ID; client workspace names never become arbitrary fully qualified grain IDs. A table must belong to a window descriptor in that workspace (open or closed) before read/update/reopen. The workspace interface need not know Supabase; application adapters perform this resolution. Keep simple UI-kit `/ui/tables` separate rather than conflating two payload contracts.

- [x] Write HTTP tests for create/read/filter/sort/page using `ISupabaseTable` and a real disposable PostgreSQL fixture. Verify 404 for an unassociated table and 409 for stale view revision. Add JSON contract tests that decode the response with the real Dart models, including null, number/date/text columns, counts and visible columns.
- [x] Implement the application adapter over `ReadSupabaseTable` and `UpdateSupabaseTableView`. Preserve `SupabaseTablePolicy` query caps/validation. Table error mapping: invalid filter/query → 400; missing association/view → 404; revision conflict → 409; source unavailable → 503. Do not expose connection strings or raw provider exception chains.
- [x] Make workspace event subscription register before reading its initial snapshot; include a revision so read/subscribe races and coalesced updates reconcile correctly. On reconnect read the latest snapshot rather than replaying window-opening commands.
- [x] Add Dart scoped client methods with request cancellation, typed errors and model decoding. On a revision conflict retain the user's visible state, refresh the current snapshot and show the conflict; do not silently overwrite another edit.
- [x] Add `flutter_test: { sdk: flutter }` to shell/pubspec.yaml dev_dependencies and update the workspace lockfile with the existing SDK. Put core contract tests under shell/test/core so the production pure-Dart core acquires no Flutter dependency. No standalone core test runner is currently configured.
- [x] Run HTTP/contract tests and core Dart tests; commit the transport adapter.

### Task 3: Connect real Supabase tools to an idempotent window-opening operation

**Create:** `src/Applications/IntoChat/IntoChat/Agent/{SupabaseWorkspaceTools,QueryWindowOperation}.cs`; `src/Applications/IntoChat/Tests/QueryWindowOperationFacts.cs`. Add the persisted operation's neuron contract/implementation beside QueryWindowOperation in the application assembly and ensure its Orleans code generation/module discovery follows existing app runtime conventions. Do not add a new project for it.

**Modify:** application `Program.cs` registration; AI module tool resolution through task 4's per-run tool factory.

**Interfaces:** Application `QueryWindowOperation.ExecuteAsync(scopeId, runId, callId, title, sql, ct)` returns `QueryWindowResult(WindowId, TableId, Title, WorkspaceRevision)`. Two explicitly registered model tools: `supabase_schema` and `show_supabase_query_table`. Their model-visible arguments contain schema selection or title/SQL only; owner/workspace/run/tool-call identities come from trusted invocation context. `QueryWindowResult` renders as a table result link in chat. No tool lets the model select an arbitrary owner/workspace.

Operation identity is a stable hash of the server-resolved scope, run ID and tool call ID. Persist the input fingerprint, result/table ID and stage before side effects. Existing `CreateFromQuery` rejects duplicates: add a narrow idempotent operation-aware creation method to `ISupabaseTable`/`SupabaseTableNeuron`, preserving current validation. Matching operation/fingerprint returns the existing view; different input fails. This handles a crash after table creation but before the coordinator records success.

- [x] Write a failing recovery test: create the table, inject a failure immediately before workspace Open, resume the same operation, and assert one table identity, one window and the original rows. Repeat after Close and assert no reopening. Use an internal injectable operation-stage hook in tests, not production random failures.

```csharp
// QueryWindowOperationFacts uses a test-owned failure hook and real persisted neurons.
await Assert.ThrowsAsync<IOException>(() => operation.ExecuteAsync(scope, run, call, title, sql, ct));
var result = await operation.ExecuteAsync(scope, run, call, title, sql, ct);
Assert.Equal(result.TableId, Assert.Single((await workspace.Read()).Windows).View.Id);
```

The fixture supplies `operation`, `workspace`, identifiers and seeded SQL; the hook throws once after the table-create checkpoint. Define that fixture in QueryWindowOperationFacts, not a general public fault-injection framework.

- [x] Implement input validation, stable identity, stage persistence, idempotent table creation and workspace Open with bounded revision-conflict reconciliation. A conflicting user Close after this operation opened the window is authoritative. Cancellation marks the operation interrupted; resume is explicit. Retain an already-created result reference for recovery instead of deleting it blindly.
- [x] Add schema/query/empty/error tests using production provider behavior. Verify SQL write attempts are rejected by existing guards and never create a successful table window. Build tool arguments/results from real neuron responses; never hardcode rows in the model fixture or tool adapter.
- [x] Register the tools explicitly for the workspace agent. Keep Supabase/Flutter dependencies in the application integration code, not AI core. Missing selected tools must produce a startup/capability error rather than NativeTools.Resolve silently skipping them.
- [x] Run application operation tests and Supabase Unit/provider Integration cases; commit the tool coordinator.

### Task 4: Restore the production chat path with persisted conversation state

**Create:** AI `Contracts/Conversations/{IConversation,ConversationState,ConversationTurn}.cs`, AI `AI/Conversations/ConversationNeuron.cs`, AI `AI/Agents/{IAgentTurnRunner,AgentTurnRunner,AgentTurnRequest,AgentTurnEvent,AgentToolContext}.cs`; application `Agent/{AgentEndpoints,ConversationCoordinator}.cs`; AI tests `Agents/AgentToolFacts.cs`; application tests `AgentHttpFacts.cs`.

**Modify:** AI `Agents/AgentNeuron.cs` to delegate shared model/tool execution to AgentTurnRunner while preserving `IAgent.Ask`; application Program registration; Flutter `agent_events.dart`/`ui_client.dart` to include workspace identity. Keep the excluded legacy conversation/session files excluded; remove obsolete versions once the new route and callers are verified.

**Interfaces:** The runtime-local runner is the single AI execution implementation used by the neuron and application coordinator:

```csharp
public interface IAgentTurnRunner
{
    IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, CancellationToken ct);
}
public sealed record AgentToolContext(string ScopeId, string RunId, string CallId);
public sealed record AgentTurnRequest(
    string AgentId, string RunId, string ScopeId,
    IReadOnlyList<ConversationTurn> History, string Message,
    AgentModelSelection? Model);
public sealed record ConversationTurn(
    string RunId, string UserText, string AssistantText,
    IReadOnlyList<string> ResultIds);
public sealed record ConversationState(
    long Revision, string? ActiveRunId, IReadOnlyList<ConversationTurn> Turns);
public interface IConversation : INeuron
{
    Task<ConversationState> Begin(string runId, string message, long expectedRevision);
    Task<ConversationState> Complete(ConversationTurn turn);
    Task<ConversationState> Interrupt(string runId);
    Task<ConversationState> Read();
}
```

Define AgentTurnEvent as typed records for Started, Text, ToolStarted, ToolCompleted, Finished and Failed, carrying run ID, tool call ID/name/result where relevant. These are runtime-local streaming values; the coordinator writes the existing AG-UI-compatible event subset. The conversation neuron persists only stable domain state, not opaque SDK sessions or live ChatClient objects. `Begin` accepts one active run, deduplicates the same run ID/message, rejects conflicting reuse and stores pending input; `Complete` persists the final turn/results atomically. `Interrupt` must only affect its matching active run. Runtime restart changes an abandoned active run to interrupted before another run starts.

The application resolves an owner/workspace/thread key and selects the configured workspace agent. Tool context is supplied by the runner per invocation, including the actual call ID. No singleton tool captures a first request's scope. Verify the pinned SDK's tool-invocation hooks before implementation; do not assume a new SDK API or add a second automatic tool loop around an existing one.

- [x] Write AI tests using a scripted `IChatClient` that emits function calls and requires returned function results before producing final text. Assert selected tools execute exactly once, unknown requested tools fail, and two interleaved scopes cannot share context. Preserve AgentFacts' existing Ask behavior through the extracted runner.
- [x] Implement per-run tool factories and selected capabilities. Use the production model client and exactly one function-invocation pipeline. Persist completed conversational turns/result references and supply them to later requests; do not manufacture conversational continuity from browser-only history.
- [x] Implement `POST /agent` using the current request fields plus workspaceId. Subscribe/run under request cancellation. Emit RUN_STARTED, TEXT_MESSAGE_START/CONTENT/END, TOOL_CALL_START/ARGS/END/RESULT and RUN_FINISHED; failures emit RUN_ERROR. Flush valid SSE frames. Write the durable completed turn before RUN_FINISHED so reconnect can recover committed results.
- [x] Keep cancellation in the runtime coordinator/runner's per-run lifetime, not a non-interleaving grain method blocked behind a long model call. The conversation neuron handles short state transitions only. Disconnect aborts the model/tool token and records interruption; completed side effects remain referenced. Explicit retry uses a new run ID unless resuming the same interrupted operation intentionally.
- [x] Add HTTP tests for two successive messages, duplicate run submission, disconnected stream, missing tool, delayed tool error and a stale completion after a newer run. Assert no false RUN_FINISHED and no update to a different workspace. Preserve BasicAuth gate behavior and avoid trusting client-supplied owner IDs.
- [x] Run AI, HTTP and operation suites; commit the new chat path.

### Task 5: Project workspace state into the existing Flutter windows

**Create:** `src/Modules/Flutter/app/shell/lib/workspace/workspace_remote_controller.dart`; shell tests `workspace_remote_controller_test.dart`, `workspace_data_window_test.dart`.

**Modify:** shell `workspace/{workspace_store,workspace_app,workspace_chat,workspace_chat_presentation,artifact_editors}.dart`; core client/models from task 2. Reuse the current UiDataTable controller rather than constructing a second table UI.

**Interfaces:** `WorkspaceRemoteController` owns current workspace snapshot, subscription and request cancellation; it calls the scoped API to close/reopen and exposes snapshots to WorkspaceStore. Store owns geometry/focus only for remotely managed windows. Existing local-only artifacts remain explicitly local. Add a persisted local `remoteManaged` discriminator during migration, so remote open/closed state is never accidentally overwritten by old local presentation JSON.

- [x] Write widget tests driving real WorkspaceApp with a controlled API client: a WorkspaceChanged update opens a floating result window, preserves unrelated windows, and shows the existing filter/sort/page controls. A duplicate update must not add another window. Switching workspace before completion must not steal focus or append the window to the new workspace.
- [x] Implement subscription-before-read with revisions; reconcile from authoritative state after refresh or disconnect. Window geometry stays in local persistence keyed by workspace/window identity. Suppress the old tool-result `store.openArtifact` path for remote-managed results; chat shows a link that invokes explicit reopen instead.
- [x] Bind table controls to the scoped query-table adapter. Cancel superseded filter reads and ignore stale responses by request generation/revision. Show loading, empty rows and typed error states; keep the last successful table visible if refresh fails, marked as stale rather than presenting it as a new result.
- [x] Add widget tests for stale filter response, revision conflict, schema/type decoding, close/reopen and reload with existing local layout. Keep old local artifacts usable; do not migrate coordinates into backend state.
- [x] Add stable accessible labels: `Message`, `Send`, window title, `Filter column`, `Filter value`, `Apply filter`, `Clear filters`, `Next page`, `Previous page`, and column sort buttons. Use visible roles/labels for E2E; retain semantics opt-in from the existing harness.
- [x] Run Flutter core and shell tests, then commit the window projection.

### Task 6: Add deterministic database/model fixtures and the real browser journey

**Create:** `src/Applications/IntoChat/Tests/Fixtures/{ScriptedModelServer,AgentDataFixture}.cs`, `SupabaseWorkspaceE2EFacts.cs`; add the seed SQL alongside the fixture. Use Part 1's `IntoChatTestDeployment` for the whole application.

**Interfaces:** `ScriptedModelServer.StartAsync(ct)` yields an async-disposable fixture with `Endpoint`, observed requests and `AssertCompleted()`. Serve the Chat Completions/streaming subset used by the selected production OpenAI-compatible client. Require the schema tool result before issuing the query-table call, then require the actual table/window result before final response. Fail on extra/missing calls. Never call a neuron or write application state from the fixture.

`AgentDataFixture.StartAsync(ct)` owns the model server, other test deployment fixtures and E2E brain; its `Brain` exposes normal E2EBrain. Start the application using an explicit fixture model/default provider, synthetic API key through PrivateConfiguration, local model endpoint, local databases and Flutter Web. The seeded query table is not created until the user prompt triggers the production tool.

- [x] Seed a fresh database after its health check but before browser submission. Create a read-only application role; use a separate fixture owner connection for schema/seed. Seed 60 active rows, an inactive control row, and a per-run marker in a company beyond the first 25-row page. Explicit ordering makes page assertions reproducible. Do not interpolate the marker into SQL; use Npgsql parameters.
- [x] Implement the model server against the pinned SDK request shape and test it with the actual production client. The second request must include the real schema tool result; the later request must include the real query-window result. Return only the supplied tool result IDs, never invented IDs or database rows.
- [x] Write the main E2E through visible controls:

```csharp
await using var fixture = await AgentDataFixture.StartAsync(ct);
await using var browser = await fixture.Brain.OpenBrowserAsync(ct);
await browser.Page.GetByRole(AriaRole.Textbox, new() { Name = "Message", Exact = true })
    .FillAsync("Show the active leads from Supabase with company and email");
await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
var window = browser.Page.GetByRole(AriaRole.Region, new() { Name = "Active leads", Exact = true });
await Assertions.Expect(window).ToBeVisibleAsync();
await window.GetByLabel("Filter column", new() { Exact = true }).SelectOptionAsync("company");
await window.GetByLabel("Filter value", new() { Exact = true }).FillAsync(fixture.Marker);
await window.GetByRole(AriaRole.Button, new() { Name = "Apply filter", Exact = true }).ClickAsync();
await Assertions.Expect(window.GetByText(fixture.Marker, new() { Exact = true })).ToBeVisibleAsync();
fixture.Model.AssertCompleted();
```

Use the actual accessible widget interaction for Flutter dropdowns; if it exposes a button/listbox rather than native select, select the same labeled option through roles. Keep the label contract stable rather than forcing a native HTML control into Flutter. `AgentDataFixture.Marker` is the generated company string; `Model` is its ScriptedModelServer.

- [x] Assert active row count, excluded control row, selected columns, filter result from beyond page one, clearing, sorting and pagination. Refresh the browser and verify one restored window and preserved view settings. Close/reopen from the chat result link and verify no duplicate table. Switch workspaces during a delayed response and verify the result remains attached to the original workspace.
- [x] Add empty result, invalid query, unavailable database and cancellation scenarios. Assert clear visible state, no fabricated rows or successful completion, and all owned resources dispose. Keep failure injection confined to test-owned providers/resources.
- [x] Run the success scenario three times headless/zero delay and three times headed/250 ms with independent logs. These are separate verification runs, not retry-until-green. Run failure cases headless and inspect trace/screenshots on failures.
- [x] Commit the scenario only after it uses the real browser-to-agent-to-provider-to-workspace path. Record exactly which provider is substituted.

### Task 7: Add opt-in live-model verification and complete acceptance

**Files:** Extend `AgentDataFixture` and `SupabaseWorkspaceE2EFacts`; update `src/Testing/README.md`, approved spec status and a tracked execution ledger beside these plans.

- [x] Add an explicit live mode controlled by `DIGITALBRAIN_E2E_LIVE_MODEL=1`. The test is separately categorized and skipped with a reason unless selected. When selected but required model credentials are absent, fail with an actionable message. Keep the same fresh database and browser actions; use semantic table assertions, not exact assistant prose or exact tool-call sequence.
- [x] Do not execute paid/live calls without an explicit user request to run that lane. Deterministic CI remains the default. Do not label the PostgreSQL-backed provider test a test of Supabase Auth, REST or Realtime.
- [ ] Run the complete solution suite once after affected tests pass, plus Flutter core/shell suites. Check that neither runtime nor UI has duplicate authoritative state or a legacy conversation implementation re-enabled. Investigate any cleanup failure instead of hiding it.
- [ ] Review the final diff against the approved interface and all five Review Focus cases. Record results and limitations. Commit, leave `archv2` unpushed, and report the test guarantees plainly.

## Commands and execution order

Run Part 1 through completion first. Each task above starts with its targeted failing tests, then implementation and green affected suites. The relevant .NET commands are:

```powershell
dotnet test --project src/Modules/Flutter/Tests.Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false -- --filter-class '*WorkspaceFacts'
dotnet test --project src/Modules/AI/Tests/DigitalBrain.Modules.AI.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*SupabaseWorkspaceE2EFacts'
dotnet test --solution DigitalBrain.slnx -p:CodeGraphRefresh=false
```

Run `flutter test test/core` for core protocol/model tests and `flutter test` for the complete shell suite from `src/Modules/Flutter/app/shell`, using the repository's installed/pinned toolchain. Task 2 adds the SDK test dependency because the current packages have no configured test runner. Confirm discovery selects the new tests; capture missing-tool/environment failures separately from test assertions. The live model lane is documented but not part of the default execution request.

## Completion criteria

A user can ask through chat, get a real interactive table window in the intended workspace, filter/sort/page it, refresh and reopen it. The same production implementation runs against live providers when explicitly configured. CI proves the complete application path with a scripted model and real database; it does not replace agent tools, the query provider, the window operation or Flutter rendering.
