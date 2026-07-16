# Multiagent OS M1 — Connections + Specialists + Approval Spine

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the Milestone 1 multi-specialist OS spine: real Connections (list/health/connect), Features catalog as specialist roster, and Activity approval trust UX — without Agent runtime or raw MCP tools.

**Architecture:** Features remain the only programmable unit. Connectors unlock Capabilities. Installed Features appear as specialists in the capability/catalog surface. Mutations stay on the Effect rail. Chat/Ask is resolve-only. Product nouns: Feature · Capability · Connector · Effect · Activity.

**Tech Stack:** .NET Aspire + Orleans, gRPC UI (`src/DigitalBrain.Mcp`), Flutter (`app/`), FeatureHost, Google/Salesforce/Web connectors.

**Locked decisions:**
- No Agent runtime as product core
- No raw MCP as tenant tools
- No free scripting Features
- UI copy never sells “Agent” as installable unit (use Feature / specialist / Connections)

---

## Repo reality (do not invent)

| Surface | State |
|---------|--------|
| Feature install/authoring/run | Exists (Studio, Catalog, FeatureHost, invoker) |
| Effect rail | Exists (`InoEffectPlan*`, Salesforce propose) |
| `ListConnections` / `GetConnection` RPCs | **Proto stubs only** — empty messages, no UiGrpcService override |
| OAuth start | HTTP `GET /oauth/start/{provider}` on Edge (`AuthorizationFlowStartProxy`) |
| Activity list/get | Exists; `FeatureRunSnapshot` has no effect field-diff payload |
| Flutter nav | Chat / Features / Activity only — **no Connections** |
| EnrichSalesforce template seed | Exists (`ConstrainedFeaturePackTemplates`) |

---

## File map (M1)

| Area | Create / Modify |
|------|-----------------|
| Proto | `src/DigitalBrain.Mcp/Protos/ui.proto` |
| Edge gRPC | `src/DigitalBrain.Mcp/UiGrpcService.cs`, `DigitalBrainUiEndpoints.cs` (or new `ConnectionQueryService.cs`) |
| Dart wire | `app/lib/grpc/ui.pb.dart`, `ui.pbgrpc.dart`, `ui.pbenum.dart` as needed |
| Flutter Connections | `app/lib/features/connections/*`, `main_destination.dart`, `router.dart` |
| Flutter Activity approval | `activity_run_detail.dart`, models if extended |
| Flutter catalog | `feature_catalog_page.dart`, gateway if grants/health added |
| Tests | Orleans/unit for connection projection; Flutter widget/golden tests |

---

## PR Plan

### PR 1: Connections gRPC contract + Edge projection

- **Description:** Flesh out `ListConnections` / `GetConnection` proto messages; implement authenticated Edge handlers that project registered `IConnector` descriptors, config validity, health probe, and capability IDs unlocked by that provider. OAuth remains HTTP `/oauth/start/{provider}` — return `connect_path` string for clients. Zero silent healthy when probe fails.
- **Files/components affected:** `src/DigitalBrain.Mcp/Protos/ui.proto`, `src/DigitalBrain.Mcp/UiGrpcService.cs`, `src/DigitalBrain.Mcp/DigitalBrainUiEndpoints.cs` or new connection service, `tests/DigitalBrain.UnitTests` or IntegrationContract tests, regenerate C# protos via build
- **Dependencies:** None

### PR 2: Flutter Connections screen + shell destination

- **Description:** Add `MainDestination.connections` at `/connections`. Gateway over gRPC list/get. Empty states, health chips, Connect opens OAuth start URL (Edge base + `connect_path`). No MCP URL field. Nouns: Connector, Capability.
- **Files/components affected:** `app/lib/shell/main_destination.dart`, `app/lib/router.dart`, `app/lib/features/connections/*`, `app/lib/grpc/*`, tests under `app/test/features/connections/`, shell tests
- **Dependencies:** PR 1

### PR 3: Activity approval trust UX (MVP)

- **Description:** When run status is `waitingForApproval`, Activity run detail becomes a first-class review surface: clear status, safe guidance, primary **Review change** CTA that deep-links to conversation/request (existing origin refs) where `SubmitAction` approve/decline already works. Do not invent a second approval system. If effect safe summary is not yet on `FeatureRunSnapshot`, show honest copy: “A change is waiting for your approval in the originating conversation” — never fake field diffs.
- **Files/components affected:** `app/lib/features/activity/activity_run_detail.dart`, `activity_page.dart` (badge/filter emphasis), tests
- **Dependencies:** None (can parallel PR 1 in theory; stack after PR 2 for linear UX)

### PR 4: Features catalog as specialist roster

- **Description:** Catalog cards show specialist framing: Installed vs Draft, status chips, empty state “Install Enrich Salesforce account from Gmail”, create CTA still opens Ask/Chat. Optional: surface “needs connection” only if connection list available (depends PR 1–2). Copy: Features/specialists, never Agents.
- **Files/components affected:** `app/lib/features/catalog/feature_catalog_page.dart`, tests
- **Dependencies:** PR 2 (for optional connection badge; card redesign can land without it)

---

### Task 1: Connections proto + Edge list/get

**Files:**
- Modify: `src/DigitalBrain.Mcp/Protos/ui.proto`
- Modify: `src/DigitalBrain.Mcp/UiGrpcService.cs`
- Create or modify: connection projection in `DigitalBrainUiEndpoints.cs` or `ConnectionCatalogService.cs`
- Test: unit/integration tests for list projection fail-closed health

**Proto shape (required):**

```protobuf
enum ConnectionHealthStatus {
  CONNECTION_HEALTH_STATUS_UNSPECIFIED = 0;
  CONNECTION_HEALTH_STATUS_HEALTHY = 1;
  CONNECTION_HEALTH_STATUS_NEEDS_REAUTH = 2;
  CONNECTION_HEALTH_STATUS_DISCONNECTED = 3;
  CONNECTION_HEALTH_STATUS_MISCONFIGURED = 4;
}

message ConnectionSnapshot {
  string provider = 1;
  string connection_id = 2;
  string display_name = 3;
  ConnectionHealthStatus health = 4;
  optional string health_detail = 5;
  repeated string unlocked_capability_ids = 6;
  optional string connect_path = 7;
}

message ListConnectionsReply {
  reserved "owner_id", "actor_id";
  repeated ConnectionSnapshot connections = 1;
}

message ConnectionReply {
  reserved "owner_id", "actor_id";
  ConnectionSnapshot connection = 1;
}
```

- [ ] **Step 1:** Write failing unit test: listing connections returns google/salesforce/web providers with health and capability ids from descriptor sources; unhealthy when `TestConnectionAsync` returns Healthy=false.
- [ ] **Step 2:** Extend proto; `dotnet build` Edge project so C# stubs regenerate.
- [ ] **Step 3:** Implement projection: discover keyed `IConnector` + `ICapabilityDescriptorSource` RequiredConnections; probe with short deadline (match `OwnerConnectionHealth` 3s); map status; set `connect_path` to `/oauth/start/{provider}` when not healthy or always for reconnect.
- [ ] **Step 4:** Wire `UiGrpcService.ListConnections` / `GetConnection` through auth like `ListActivity`.
- [ ] **Step 5:** Tests green for owning project; no `--filter` at root suite before claim complete.
- [ ] **Step 6:** Commit: `feat(edge): project connector health on ListConnections`

**Acceptance:** Authenticated ListConnections returns real providers; never reports healthy on failed probe; GetConnection 404s unknown id.

---

### Task 2: Flutter Connections gateway + page + nav

**Files:**
- Create: `app/lib/features/connections/connection_models.dart`
- Create: `app/lib/features/connections/connection_gateway.dart`
- Create: `app/lib/features/connections/connections_page.dart`
- Modify: `app/lib/shell/main_destination.dart`, `router.dart`, dart grpc stubs
- Test: `app/test/features/connections/`

- [ ] **Step 1:** Update dart protobuf stubs to match proto (manual or generate; match repo pattern in `app/lib/grpc/`).
- [ ] **Step 2:** Failing widget test: empty load failure, healthy Google card, Connect button present when disconnected.
- [ ] **Step 3:** Implement gateway + page + `MainDestination.connections` (`/connections`, icon link/link_outlined).
- [ ] **Step 4:** Router ShellRoute entry; open connect_path against session Edge base URL (follow existing client base URL patterns in `DigitalBrainClient` / session).
- [ ] **Step 5:** `flutter test` for connections + shell/router tests.
- [ ] **Step 6:** Commit: `feat(ui): Connections destination and health roster`

**Acceptance:** User can open Connections from shell; sees providers; Connect does not accept MCP URLs.

---

### Task 3: Activity waiting-for-approval trust UX

**Files:**
- Modify: `app/lib/features/activity/activity_run_detail.dart`
- Modify tests under `app/test/features/activity/`

- [ ] **Step 1:** Failing test: run with `waitingForApproval` shows review CTA and trust copy; no fabricated field diff.
- [ ] **Step 2:** Implement approval card: status emphasis, link to conversation/request when origin refs present, disable fake approve buttons without server contract.
- [ ] **Step 3:** Flutter tests pass.
- [ ] **Step 4:** Commit: `feat(ui): Activity approval waiting state as trust surface`

**Acceptance:** Waiting runs never look “completed”; user has clear next step.

---

### Task 4: Specialist catalog framing

**Files:**
- Modify: `app/lib/features/catalog/feature_catalog_page.dart`
- Tests: `app/test/features/catalog/` (create if missing)

- [ ] **Step 1:** Failing test: empty state mentions template goal “Enrich Salesforce account from Gmail”; card labels Draft/Installed (not Agent).
- [ ] **Step 2:** Implement roster UX + empty/create copy aligned with nouns.
- [ ] **Step 3:** Optional: if Connections gateway injectable, show “Connect apps” secondary CTA.
- [ ] **Step 4:** Tests pass; commit: `feat(ui): Feature catalog specialist roster copy and empty states`

**Acceptance:** Catalog reads as specialist roster for Features; zero Agent product copy.

---

### Task 5: Integration gate

- [ ] **Step 1:** From repo root: `dotnet build`
- [ ] **Step 2:** `dotnet test --logger "console;verbosity=minimal"` (no filter)
- [ ] **Step 3:** `flutter test` in `app/`
- [ ] **Step 4:** `aspire doctor` if CLI available
- [ ] **Step 5:** Fix any failures; no silent skips

---

## Out of scope for this plan (M2+)

- Second Feature templates gallery
- Full field-diff Effect decision RPCs on Activity (extend when `FeatureRunSnapshot` carries effect intents)
- Flutter full IA rewrite (delete RFW/Live)
- Package renames Mcp→Edge, integrations→connectors
- Multi-agent planner / IAW packages

## Success metrics

- ListConnections useful in UI ≤ demo setup time
- Time-to-connect path visible in UI (OAuth start)
- Waiting approval is obvious in Activity
- Zero silent fakes (healthy/connected/approved without evidence)

## North-star demo after M1

1. Connections → providers listed → Connect Google  
2. Features → install template path still via Studio/Chat  
3. Run Feature → Activity waiting → Review change via conversation  
4. No Agent/MCP surfaces  
