# Edge, Salesforce Reader, and Flutter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a durable approval workspace inbox and opaque Edge action bridge, a workspace-bound Salesforce revenue reader, and a Flutter Base UI Kit renderer without changing Core or exposing provider bindings.

**Architecture:** Approvals owns the redacted workspace inbox read model and directs safe state changes to it. Presentation turns that model and existing sales surfaces into renderer-neutral facts. Edge reads only those facts through a supplied scope-bound channel, assembles a closed Base UI Kit schema, and translates an opaque action reference into the existing approval decision ingress. A Salesforce adapter is a provider-side project implementing the existing narrow reader interface. Flutter renders the Edge schema through fixed widgets and a client abstraction.

**Tech Stack:** .NET 11, C#, Orleans Hosting only, System.Net.Http, System.Text.Json, xUnit v3, Flutter 3.44/Dart 3.12, Material widgets, `flutter_test`.

## Global Constraints

- Preserve frozen whole-proposal semantics, exact action binding, provenance, direct-delivery durability, and workspace isolation.
- Do not add approval orchestration, UI state, provider SDKs, HTTP, or Flutter to Core or Hosting's Kernel responsibilities.
- Product facts and Edge JSON contain no workspace key, credential, token, raw SOQL, raw provider error, or Salesforce mutation binding.
- UI actions carry only opaque action references; the Edge resolves them using a scope-bound `WorkspaceChannel` and leaves an authorization seam for Access.
- Keep roles, marketplace packaging, arbitrary custom UI, and browser renderer work out of scope.
- Use a failing focused test before every production behavior change; use composed-host journals for durable guarantees and controlled HTTP handlers for provider behavior.
- Preserve the pre-existing dirty edits in `CORE-ARCHITECTURE.md`, `src/DigitalBrain.Mocks.Tests/Smoke/MockXSmokeTests.cs`, and `src/Directory.Packages.props`.
- Make focused commits that stage only files belonging to the completed task.

---

### Task 1: Durable approval workspace inbox and outcome states

**Files:**
- Create approval inbox state/facts/behavior under `src/DigitalBrain.Product.Approvals/`.
- Modify `ApprovalNeuron.cs`, `ApprovalState.cs`, and `DigitalBrain.Product.Salesforce/{SalesforceMutationNeuron.cs,SalesforceMutationState.cs}`.
- Create renderer-neutral workspace surface types and projection behavior under `src/DigitalBrain.Product.Presentation/`.
- Modify product test composition and add `src/DigitalBrain.Product.Tests/Presentation/ApprovalWorkspaceInboxTests.cs`.

**Consumes:** frozen `ApprovalPending`, `ApprovalStatusChanged`, and a Salesforce mutation's exact approved proposal association.

**Produces:** a fixed-name, workspace-scoped durable inbox whose items contain redacted frozen review data, opaque context, lifecycle status, and deterministic opaque approve/reject references; a presentation workspace snapshot supports replay and reconnect.

**Interfaces:**

```csharp
public enum ApprovalWorkspaceItemStatus
{
    Pending, Approved, Rejected, Expired, MutationUncertain,
}

public sealed class ApprovalWorkspaceInboxNeuron : Neuron<ApprovalWorkspaceInboxState>,
    INeuron<ApprovalWorkspaceInboxItemChanged>
{
    public const string Kind = "approval-workspace-inbox";
    public const string Name = "pending-approvals";
}

public sealed record ApprovalWorkspaceInboxSnapshot(
    long Revision,
    IReadOnlyList<ApprovalWorkspaceInboxItem> Items) : Synapse;

public sealed record ApprovalWorkspaceSurfaceRequested(
    long Revision,
    IReadOnlyList<ApprovalWorkspaceSurfaceItem> Items) : Synapse;
```

- [ ] **Step 1: Write the failing durable acceptance tests**

Create tests proving: a chat proposal emits a workspace snapshot with the exact review content and `Chat`/`ContextDrawer`/`Inbox` placement; a webhook proposal emits the same frozen content with `Inbox` only; after activation recovery the snapshot is reproduced; approved, rejected, expired, and mutation-uncertain transitions are distinct; same relative proposal/inbox identities in two workspaces remain isolated.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj --no-restore --filter-class DigitalBrain.Product.Tests.Presentation.ApprovalWorkspaceInboxTests`

Expected: compilation fails because the durable workspace inbox and presentation snapshot do not exist.

- [ ] **Step 3: Implement only the redacted direct fan-out and projections**

Add an Approvals-owned fixed inbox behavior. On first proposal and lifecycle/result transitions, `ApprovalNeuron` directs safe facts to it; it never broadcasts arbitrary proposal IDs or exports `ApprovalActionBinding`. Store immutable copies. Make Salesforce retain the accepted frozen proposal identity only long enough to report its uncertain terminal effect back to the matching approval authority. Presentation maps the trusted inbox snapshot to explicit status/placement/action-reference values. No Edge, HTTP, or Flutter type belongs in these modules.

- [ ] **Step 4: Verify GREEN and commit**

Run the focused class, `dotnet test src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj --no-restore`, and `git diff --check`. Commit only Task 1 files with `feat: add durable approval workspace inbox`.

### Task 2: Closed Edge UI contract and opaque approval action bridge

**Files:**
- Create `src/DigitalBrain.Edge/DigitalBrain.Edge.csproj` and focused schema, assembler, journal-reader, and action-bridge files.
- Create `src/DigitalBrain.Edge.Tests/DigitalBrain.Edge.Tests.csproj` and test fixtures.
- Modify `DigitalBrain.slnx`.

**Consumes:** `WorkspaceChannel`, Presentation workspace approval snapshots, per-proposal approval review facts, and Sales Insights presentation facts.

**Produces:** JSON-serializable Base UI Kit surfaces for chat, inbox, drawer, status, chart/table, and unavailable states; a bridge whose sole public action input is `OpaqueUiActionReference`.

**Interfaces:**

```csharp
public readonly record struct OpaqueUiActionReference(string Value);

public sealed record UiSurface(
    string SurfaceId,
    long Revision,
    IReadOnlyList<BaseUiKitComponent> Components);

public sealed record UiWorkspaceSnapshot(long Revision, IReadOnlyList<UiSurface> Surfaces);

public sealed record UiActionReceipt(bool Accepted);

public interface IWorkspaceUiSurfaceSource
{
    Task<ApprovalWorkspaceSurfaceRequested?> ReadApprovalsAsync(CancellationToken cancellationToken);
    Task<UiSurface?> ReadSalesAsync(string queryId, CancellationToken cancellationToken);
}

public interface IUiActionAuthorizer
{
    Task<bool> AuthorizeAsync(OpaqueUiActionReference action, CancellationToken cancellationToken);
}

public interface IApprovalUiActionBridge
{
    Task<UiActionReceipt> InvokeAsync(OpaqueUiActionReference action, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing Edge tests**

Test that the Edge assembles approval card/drawer/dashboard components from a replayed presentation snapshot, preserves a chat route versus inbox-only route, creates chart/table and unavailable surfaces, rejects malformed/unknown cross-workspace action refs, emits only `ApprovalDecisionSubmitted` for a valid action, keeps a duplicate action to one decision identity, and invokes the authorization seam before publication. Assert serialized UI JSON contains no action binding, Salesforce mutation, credential, token, scope, SOQL, or provider error.

- [ ] **Step 2: Run RED**

Run: `dotnet test src/DigitalBrain.Edge.Tests/DigitalBrain.Edge.Tests.csproj --no-restore --filter-class DigitalBrain.Edge.Tests.EdgeWorkspaceTests`

Expected: project/test failure because no Edge contract exists.

- [ ] **Step 3: Implement the smallest closed contract**

Use a closed component discriminator set: `Chat`, `Inbox`, `Drawer`, `Card`, `Status`, `Evidence`, `Changes`, `Action`, `BarChart`, `Table`, and `Unavailable`. Implement a journal adapter over known presentation identities; it exposes snapshots/revisions, not raw journals. Resolve an opaque action only by matching the current workspace snapshot, call `IUiActionAuthorizer`, derive its stable decision id, and publish the existing decision ingress through the supplied source-bound channel. The Edge assembly must reference Access and product contracts but neither Hosting nor Orleans.

- [ ] **Step 4: Verify GREEN and commit**

Run the Edge class, product presentation class, `dotnet build DigitalBrain.slnx --no-restore`, and `git diff --check`. Commit only Task 2 files with `feat: add opaque approval edge contract`.

### Task 3: Typed reader failure contract and Salesforce adapter

**Files:**
- Modify Sales Insights reader failure vocabulary/effect and presentation unavailable mapping under `src/DigitalBrain.Product.SalesInsights/` and `src/DigitalBrain.Product.Presentation/`.
- Create `src/DigitalBrain.Adapters.Salesforce/DigitalBrain.Adapters.Salesforce.csproj` plus connection, factory, HTTP reader, query, and error-mapping files.
- Create `src/DigitalBrain.Adapters.Salesforce.Tests/DigitalBrain.Adapters.Salesforce.Tests.csproj` plus controlled-handler tests.
- Modify `DigitalBrain.slnx` and product test project references when required.

**Consumes:** typed `SalesQuery` and Hosting-provided workspace connection lookup.

**Produces:** `ISalesRevenueReader` instances scoped by trusted workspace construction, with typed safe failures for unavailable, authentication required, reauthorization required, timeout, and invalid provider data.

**Interfaces:**

```csharp
public sealed class SalesRevenueReaderFailureException : Exception
{
    public SalesRevenueReaderFailureException(SalesInsightUnavailableReason reason);
    public SalesInsightUnavailableReason Reason { get; }
}

public interface ISalesforceWorkspaceConnectionStore
{
    Task<SalesforceWorkspaceConnection?> FindAsync(string workspaceId, CancellationToken cancellationToken);
    Task MarkReauthorizationRequiredAsync(string workspaceId, CancellationToken cancellationToken);
}

public sealed class SalesforceRevenueReaderFactory
{
    public ISalesRevenueReader CreateForWorkspace(string workspaceId);
}
```

- [ ] **Step 1: Write failing tests**

Add tests that inspect a real generated query for `IsWon = true`, half-open `CloseDate`, currency filter, stable ordering, and no caller workspace input. Test multi-page results, early record-limit rejection, malformed record rejection, expired query token followed by one successful refresh/retry, refresh rejection mapped to reauthorization required, missing connection mapped to authentication required, timeout mapped to timed out, and arbitrary provider text absent from semantic unavailable facts. Add a composition test that two workspace-bound readers use different fake connections.

- [ ] **Step 2: Run RED**

Run: `dotnet test src/DigitalBrain.Adapters.Salesforce.Tests/DigitalBrain.Adapters.Salesforce.Tests.csproj --no-restore --filter-class DigitalBrain.Adapters.Salesforce.Tests.SalesforceRevenueReaderTests`

Expected: project/test failure because the adapter and typed failure classification do not exist.

- [ ] **Step 3: Implement the narrow adapter**

Keep the list-returning reader method. A provider-specific exception/value maps raw HTTP/OAuth failures before the Sales Insights effect sees them. The adapter accepts only validated connection configuration from its workspace connection store, uses bearer authentication, treats a query 401 as one refresh/retry, follows only same-instance relative next-page links, stops at the existing maximum, and never includes response bodies in exceptions or facts. The store has a reauthorization marker callback but no UI dependency or secret serialization.

- [ ] **Step 4: Verify GREEN and commit**

Run adapter tests, Sales Insights tests, product tests, `dotnet build DigitalBrain.slnx --no-restore`, and `git diff --check`. Commit only Task 3 files with `feat: add workspace Salesforce revenue reader`.

### Task 4: Composed Edge-to-approval and Salesforce reader integration

**Files:**
- Extend `src/DigitalBrain.Edge.Tests/` composed-host fixture/tests.
- Extend `src/DigitalBrain.Product.Tests/` only for a real edge bridge/projection composition assertion.

**Consumes:** completed Tasks 1–3.

**Produces:** end-to-end evidence that an Edge action cannot escape its workspace and sales safe states traverse the same semantic UI pipeline.

- [ ] **Step 1: Write failing cross-boundary tests**

Using two composed workspace channels, create matching relative approval ids, read two Edge snapshots, submit the workspace A action through its bridge twice, and prove exactly one A approval/mutation result and no B action. Inject each typed reader failure and assert only its safe Edge unavailable component is returned. Force projection reactivation and prove the reconstructed snapshot is byte-equivalent in semantic content and placement.

- [ ] **Step 2: Run RED, implement the minimal composition wiring, and verify GREEN**

Run: `dotnet test src/DigitalBrain.Edge.Tests/DigitalBrain.Edge.Tests.csproj --no-restore --filter-class DigitalBrain.Edge.Tests.EdgeCompositionTests`

Then run the Edge and product test projects. Commit only Task 4 files with `test: cover edge workspace recovery and isolation`.

### Task 5: Flutter Base UI Kit renderer

**Files:**
- Create `clients/flutter/renderer/pubspec.yaml`.
- Create typed Edge-schema models, client abstraction, restoration state, widgets, and test helpers under `clients/flutter/renderer/lib/`.
- Create widget and action-round-trip tests under `clients/flutter/renderer/test/`.

**Consumes:** Edge JSON contract only.

**Produces:** a chat-first Material renderer with dashboard/inbox, context drawer, action state, sales chart/table/unavailable rendering, and reconnect restoration.

**Dart contract:**

```dart
abstract interface class UiSurfaceClient {
  Future<UiWorkspaceSnapshot> loadSnapshot();
  Future<void> invokeAction(OpaqueUiActionReference action);
}

class WorkspaceRenderer extends StatefulWidget {
  const WorkspaceRenderer({required this.client, super.key});
  final UiSurfaceClient client;
}
```

- [ ] **Step 1: Write failing widget tests**

Create a fake `UiSurfaceClient` and tests that parse an approval Edge snapshot, open its chat review drawer, render evidence/changes and explicit approve/reject controls, retain selection across restoration, show inbox-only webhook work without a chat route, display each approval state including mutation uncertain, render bar/table and unavailable sales cards, and send only the opaque reference to the fake client.

- [ ] **Step 2: Run RED**

Run: `flutter test test/workspace_renderer_test.dart`

Expected: failure because the V2 renderer package and widgets do not exist.

- [ ] **Step 3: Implement fixed Material/Base UI Kit widgets**

Use schema parsing with a closed `component` kind switch and reject unknown kinds. Keep all renderer data in `UiSurfaceClient` snapshots; use `RestorationMixin` to retain active workspace tab, selected drawer route, and revision. Buttons pass a single opaque action reference to the client. Do not add a journal, provider, Core, or product-package dependency.

- [ ] **Step 4: Verify GREEN and commit**

Run `flutter analyze` and `flutter test` from `clients/flutter/renderer`. Commit only renderer files with `feat: add Flutter semantic surface renderer`.

### Task 6: Documentation, full verification, and final review

**Files:**
- Modify `CONTEXT.md`, the V2 product architecture, first enrichment, and sales-insights design specs to replace now-completed next-iteration statements.
- Update this plan's task checkboxes and add exact verification results.

- [ ] **Step 1: Document the implemented boundaries and external credential dependency**

Describe the durable workspace inbox, Edge opaque-action seam, Salesforce connection adapter, and Flutter renderer without implying that live credentials exist.

- [ ] **Step 2: Run the final verification gate**

Run:

```powershell
dotnet build DigitalBrain.slnx --no-restore
dotnet test DigitalBrain.slnx --no-restore -- --timeout 120s
git diff --check
flutter analyze
flutter test
```

from the appropriate repository/package directories. Record pass/fail/skip counts exactly and preserve the unrelated dirty files.

- [ ] **Step 3: Commit documentation and review the full diff**

Commit only documentation/plan files with `docs: record edge and sales reader boundaries`. Create a whole-branch review package from `c4ef00c1` through `HEAD`, resolve any Critical/Important findings through the task review loop, then report the final evidence and any missing live Salesforce credential.
