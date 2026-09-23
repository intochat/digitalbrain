# 0002 — One live-table contract

- Status: accepted
- Date: 2026-09-23
- Deciders: P1.3 (autonomous, on highlevel v0.2 recommended working assumptions)
- Supersedes: none
- Related: plan P1.3 ("Live tables the assistant can read and refine"); highlevel C09
  ("Deletes: three of the four table mechanisms", line 838) and §16 trash register line 1453
  ("Four table mechanisms | keep one live table"); D6 (data location and assistant read rules);
  current-state §4.

## Context

Four overlapping table mechanisms shipped at baseline:

1. **`ISupabaseTable`** (`src/Modules/Supabase/`) — rows live in Supabase, served read-only with
   server-side paging; the saved view (columns, filters, sort, visible columns, revision) compiles
   to SQL on every read. It is the only mechanism the product app reaches
   (`GET /workspaces/{id}/tables/{id}`), and the only one the assistant opens.
2. **`ITable`** (`src/Modules/Google/Flutter/`) — the UI-kit "Table" component: rows and columns are
   stored in grain state and served by `GET /ui/tables/{id}` via `UiKitEndpoints`. It is one of 28
   UI-kit kinds (siblings: `IText`, `IButton`, `ICard`, `IChart`, …), reached through the shared
   UI-kit surface, not a data source.
3. **`IClickHouseTable`** (`src/Modules/ClickHouse/`) — a duplicate live-table mechanism over
   ClickHouse with its own `QueryPlanCompiler`, `ClickHouseTablePolicy`, `ClickHouseTableState` and
   a `TableRendered` signal. `TableRendered` has **zero consumers**, and nothing in the application
   or Flutter client references `IClickHouseTable`; only its own unit tests do.
4. **`QueryWindowOperation` / `QueryWindowOperationNeuron`** (`src/Applications/IntoChat/`) — a
   durable journal grain that recorded a tool call's fingerprint before the table and workspace
   side effects. P0.5 made the workspace `Open` receipt the durable replay source, so the journal's
   only remaining job (idempotency + changed-input rejection) is already guaranteed by
   `ISupabaseTable.CreateFromQueryOnce` (same operation id and request replays; a changed request is
   refused) and by `IWorkspace.Open` (the operation log refuses an operation id reassigned to a
   different window request).

The plan requires one live-table contract and a **required ADR before removing `ITable`**, because
`GET /ui/tables/{id}` is still served.

## Decision

Keep **`ISupabaseTable`** as the single live-table contract, generalized over a pluggable query
source (`ILiveTableSource`); delete the ClickHouse table mechanism and the query-window journal;
**retain `ITable` unchanged as a declared UI-kit adapter** (ADR-fenced), not a live data source.

### Exact deletions

- `src/Modules/ClickHouse/ClickHouse/Tables/` — `ClickHouseTableNeuron.cs`, `ClickHouseTablePolicy.cs`,
  `QueryPlanCompiler.cs`, `ClickHouseTableState.cs`.
- `src/Modules/ClickHouse/Contracts/Tables/` — `IClickHouseTable.cs`, `ClickHouseTableModels.cs`,
  `ClickHouseTableView.cs`, `ClickHouseTableExceptions.cs`, `CreateQueryTable.cs`,
  `UpdateClickHouseTableView.cs`, `Signals/TableRendered.cs`.
- The table-only members those files forced on the query path: `IClickHouseProvider.ExecutePlanAsync`,
  the `QueryPlan`/`QueryPage` records, `ClickHouseDriverProvider.ExecutePlanAsync`, and the
  `ClickHouseNames.TableType`/`TableIdPrefix` constants.
- `src/Modules/ClickHouse/Tests/Unit/Tables/ClickHouseTableFacts.cs`.
- `src/Applications/IntoChat/IntoChat/Workspace/Queries/QueryWindowOperation.cs`,
  `QueryWindowOperationNeuron.cs`, `QueryWindowContracts.cs`; its DI registration; and
  `Tests/E2E/Workspace/QueryWindowOperationFacts.cs`, `Tests/Unit/Workspace/QueryWindowJournalFacts.cs`.

### Exact survivor

- `ITable` and `TableNeuron` stay. Evidence: `GET /ui/tables` / `GET /ui/tables/{id}` and the
  `/replace` and `/view` posts are live in `UiKitEndpoints.cs`; the Flutter client calls them from
  `ui_client.dart` (`listTables`, `readTable`, `createTable`, `updateTableView`) and
  `Flutter.Tests.Unit.Table.TableFacts` / `Flutter.Tests.E2E.Table.TableHttpFacts` cover them.
  Removing `ITable` would delete one of the 28 UI-kit kinds ahead of the C02/P1.9 renderer registry
  that owns UI-kind lifecycles, and would touch files outside P1.3's file map
  (`UiKitEndpoints.cs`, `TableNeuron.cs`, the Dart client, two test projects). It is therefore kept
  as a **declared adapter**: the UI-kit table renders rows the *caller* supplies, and it is not a
  source the assistant reads. The one assistant-facing live-table source remains `ISupabaseTable`.

## Consequences

- One live-table cluster remains: `ISupabaseTable` over `ILiveTableSource`, with the single
  `QueryPlanCompiler`, `SupabaseTablePolicy`, `SupabaseQueryGuard` and `SupabaseTypeMap`. The guard
  is enforced by `LiveTableArchitectureFacts` (a source-scan test) so a second live-table mechanism
  cannot return unnoticed; the retained `ITable` adapter is asserted to have no compiler/policy/
  guard/type-map of its own.
- The ClickHouse *query* connector (`IClickHouse`, `ClickHouseNeuron`, `ClickHouseQueryGuard`,
  `ClickHouseTypeMap`) is unchanged and remains part of the product profile; only its live-table
  surface is removed.
- The assistant gains `table_read` and `table_refine`, bound to the window it opened: `table_read`
  returns schema, counts and aggregates and row values only for `Public` columns (D6; no
  per-connection grant store exists yet), and `table_refine` mutates the same window through
  `ISupabaseTable.UpdateView` instead of opening a new one.
- Replay safety previously documented on the journal now lives entirely on the existing
  `CreateFromQueryOnce` operation id and the workspace `Open` receipt; no persisted query-window
  journal state is migrated, and the clean break is acceptable pre-design-partner (T1).