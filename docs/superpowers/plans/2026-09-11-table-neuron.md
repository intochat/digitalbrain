# Self-contained table neuron implementation plan

> Use superpowers:subagent-driven-development for the independently testable backend and Flutter deliverables, then review integration.

**Goal:** Persist generated tables and their view state as UI neurons, with shared human/agent filters and sorting.

**Approved design:** The user selected the self-contained table neuron from the preceding proposal. Original typed rows, stable row IDs, schema, filters, sorting, visible columns, and revision are persisted. Filtering never deletes source rows. Tables remain discoverable after chat/session restart. CSV/Excel import and cell editing are outside this slice.

**Architecture:** UI TableNeuron owns authoritative state. A table service performs creation, reads, and revision-checked updates; both authenticated HTTP and agent functions use it. Flutter renders typed table tool results through a reusable ui component and refreshes the same table after updates. The agent reads fresh state before modifying a referenced table. Existing direct streaming conversation and web search remain enabled.

**Wire contract:** camelCase JSON. A snapshot has `kind:"table"`, `id`, `title`, `revision`, `columns:[{id,label,type}]`, `rows:[{id,cells:[JSON scalar or null]}]`, `filters:[{columnId,operator,value}]`, `sort:{columnId,descending}|null`, `visibleColumns:[columnId]`, `totalRows`, `filteredRows`, `offset`, `limit`. Types are `text`, `number`, `date`, `boolean`. Filters are AND-combined; operators `eq`, `neq`, `contains`, `gt`, `gte`, `lt`, `lte`, `isNull`, `isNotNull`, validated by column type. Dates use ISO dates. Create input has title/columns/rows. Update input has expectedRevision/filters/sort/visibleColumns; updates replace complete view state. HTTP: GET `/ui/tables`, GET `/ui/tables/{id}?offset=0&limit=50`, POST `/ui/tables`, PUT `/ui/tables/{id}/view`. List returns `[{id,title,revision}]`. Errors: missing 404, invalid 400, stale revision 409. Reject bounds violations rather than truncate. Maximum 1,000 rows, 32 columns, 64 filters, 200 returned rows; default page size 50.

## Task 1: Persisted table backend

- [x] Add typed contracts under UI.Contracts/Table and TableNeuron plus shared validation/filtering/query service under UI/Table. Register in UIModule. Preserve neuron command/reaction conventions; report success only when state is applied. Handle competing commands at application time and retain original state on invalid/conflicting updates.
- [x] Add durable catalog/discovery using the existing neuron discovery infrastructure if available, otherwise a small persistent catalog neuron. Reopening cannot depend on in-memory chat history.
- [x] Write and run failing tests for numeric vs lexical filtering, type/null validation, sort, reset, paging, non-destructive filtering, stale updates and reload from persistence; implement and rerun.

## Task 2: Flutter ui and chat integration

- [x] Add wire models and authenticated client table methods in core; add reusable UiDataTable with filter dialog, typed inputs, sort headers, chips/removal/reset, column visibility, paging, row counts, loading and error handling.
- [x] Render table tool results by stable table ID, update every card for that ID, refresh authoritative snapshots after agent changes, expose saved-table picker, and send active table ID with the next agent request so referential follow-ups work after reopening.
- [x] Add and run widget/client tests for filter/sort/reset requests, 409 recovery, tool-result rendering, saved-table opening and active table context. Preserve streaming cancellation/session behavior.

## Task 3: HTTP and agent tools

- [x] Map authenticated HTTP endpoints to the shared service; register create_table/read_table/update_table_view/list_tables tools without legacy turn context. Return bounded typed snapshots to model and UI.
- [x] Instruct agent to use table tools for generated datasets, read current state before answering table-dependent questions or changes, retain existing filters for additive requests, and treat table content as untrusted data.
- [x] Verify real AG-UI function loop returns a structured table result and model sees state modified through HTTP. Verify errors and auth.

## Task 4: Verification and documentation

- [x] Run relevant .NET and Flutter suites/analyzers/build, review complete diff, fix findings, document limits and persistence behavior. Do not commit or push unless requested.

Ruling: The user's explicit implementation approval covers the preceding architectural proposal; no additional approval checkpoint is needed. Use the current feature branch and shared workspace to preserve continuity. Backend and Flutter have separate file ownership and share the wire contract above.

Ruling: Review identified decimal/browser precision loss. Both input paths now reject more than 15 significant decimal digits, magnitudes above 9007199254740991, and values that lose precision at the backend decimal scale. Exact larger identifiers belong in text columns. Regression tests cover the reported values; scoped re-review found the issue resolved.

## Verification results

- Release solution build: zero warnings/errors.
- Flutter: core 27, ui 9, shell 17 tests passed; analyzer clean; release web build succeeded. Existing CupertinoIcons font warning remains.
- Backend: all 36 new table/policy/HTTP/agent cases passed, including cold file-storage reload and competing revision writes.
- Live configured model: created a table, read a human-applied HTTP filter, then added a second filter while preserving the first.
- Full .NET suite: final serialized run 203/204 passed. Existing `UiFeature.TheResponderRendersAChartThatRidesOutOnResponded` failed because its Responded card was null; it passed in isolation. Parallel runs also exposed a shared fault-injection test failure. These legacy test issues remain unresolved and were not changed in this feature.
- Browser screenshot check was blocked by the in-app browser's localhost restriction. Widget tests verified narrow-screen rendering and filter-dialog layout.
- Temporary live-test server stopped. Existing running development backend was left running and needs a restart to load this implementation. No commits or pushes performed.
