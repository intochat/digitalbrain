# ClickHouse module

Gives the brain a read-only door into a ClickHouse database and turns query results into live,
pageable tables in the chat. It mirrors the Memory (Qdrant) and Salesforce modules: three
projects, a provider seam with an in-memory fake, native tools for the assistant, and an Aspire
projection that hosts the server.

| Project | Namespace | What it holds |
|---|---|---|
| `src/Modules/ClickHouse/Contracts` | `DigitalBrain.ClickHouse` | `IClickHouse`, `IClickHouseTable : ITable`, the records, `ClickHouseJson`, `ClickHouseNames` |
| `src/Modules/ClickHouse/ClickHouse` | `DigitalBrain.ClickHouse` | `ClickHouseModule`, the two neurons, `ClickHouseDriverProvider`, `FakeClickHouseProvider`, the guard, the plan compiler, the native tools |
| `src/Modules/ClickHouse/Aspire.Hosting` | `DigitalBrain.ClickHouse.Aspire.Hosting` | `WithClickHouse()` projection and the embedded `Seeds/leads.sql` |

## Neurons

- **`clickhouse:default`** (`IClickHouse`) answers `query` (one read-only SELECT, at most 1 000
  rows), `schema` (tables and columns of the configured database) and `connection` (ping). It
  keeps no state; every read is served live by the provider.
- **`clickhouse-table:chtable-…`** (`IClickHouseTable`, an `ITable`) is a table whose rows live in
  ClickHouse. `create-query` describes the SELECT with `LIMIT 0`, saves the columns as the view and
  fires the signal `TableRendered` along its synapse to the chat, which shows a table card. `read`
  compiles the saved view (filters, sort, page) into parameterised SQL and returns a page with live
  counts; `update` bumps the revision and fires `TableRendered` again so the card refreshes. Rows are
  never persisted.

`TableService` routes ids by prefix through `ITableSource`: `table-` stays in memory, `chtable-`
goes to this module. The `/ui/tables` endpoints, the `read_table` / `update_table_view` /
`list_tables` tools and the Flutter `UiDataTable` therefore work on both kinds unchanged. Listing
reads each table's saved summary, never its rows, so a table whose ClickHouse is down still lists;
reading it reports the failure as a `TableSourceException` (HTTP 502, tool `source_failed`).

## Native tools

| Tool | Does |
|---|---|
| `clickhouse_schema(table?)` | Reads tables, engines, columns, types and up to 12 sample values per low-cardinality or enum column. Call it before writing SQL. |
| `clickhouse_query(sql, maxRows=200)` | Runs one read-only SELECT and returns typed rows; the server's error text comes back on failure so the model can correct the query. |
| `show_query_table(title, sql, chatName?)` | Creates a `chtable-` neuron, lists it, waits for the chat card when a `uichat` is named, and returns the first page as a `kind: "table"` result. |

Refinement and charts reuse the existing tools: "from these, which have more than 50 employees"
becomes `update_table_view` with an added `employee_count gte 50` filter; "chart them by country"
is `clickhouse_query` with a `GROUP BY` followed by `render_chart` (`bar` or `line`).

Two chats exist today and they see different tool sets. The workspace chat (AG-UI `/agent`) has
the table tools, the three ClickHouse tools and `render_chart`; it renders `show_query_table`'s
`kind: "table"` result as a live table and `render_chart`'s `kind: "chart"` result as a chart in the
working area (both carry their data, so the workspace never re-reads them from the server). A
`uichat` agent gets whatever its `Instruct` lists: the table tools, `render_chart` and the ClickHouse
tools are all native tools, so listing `read_table`, `update_table_view`, `show_query_table` and
`render_chart` gives it the full flow with `table` and `chart` cards. `render_chart` takes an optional
`chatName`: with a uichat it connects the chart neuron to the chat and waits for the card, without one
it only renders the neuron and hands the points back. The smoke transcripts in `NOTES.md` cover both.

## AppHost

```csharp
.AddModule<ClickHouseModule>(clickhouse => clickhouse.WithClickHouse(options => options.WithSeed("leads")))
```

`WithClickHouse` adds the `clickhouse` container under the brain with a data volume
(`/var/lib/clickhouse`), a persistent lifetime, a **ClickHouse Play** dashboard link (`/play`) and
the `clickhouse-db` database `digitalbrain`. Options:

- `WithSeed(name)` copies `Seeds/<name>.sql` into `/docker-entrypoint-initdb.d` with
  `WithContainerFiles` (no bind mounts). Seeds run alphabetically on an empty data dir only.
- `AlwaysRunInitScripts = true` sets `CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS=1` so seeds re-run on
  every start. Keep seeds idempotent; changing a seed's contents recreates the persistent container.

The kernel receives `ConnectionStrings__clickhouse`, waits until the database is healthy and gets
`DigitalBrain__ClickHouse__Provider=ClickHouse` and `DigitalBrain__ClickHouse__ConnectionName=clickhouse`.
With `WithDigitalBrainFakes()` the projection wires nothing and the module uses the fake.

## Configuration keys

| Key | Meaning |
|---|---|
| `DigitalBrain:ClickHouse:Provider` | `ClickHouse` for the driver; unset (with no connection string) selects the fake. |
| `DigitalBrain:ClickHouse:ConnectionName` | Connection string name, default `clickhouse`. |
| `ConnectionStrings:<name>` | `Host=…;Port=8123;Username=…;Password=…;Database=digitalbrain` from Aspire. |
| `DigitalBrain:Fakes:Enabled` | `true` forces the fake provider regardless of the other keys. |

A connection string with an unset provider fails loudly at startup, the same rule Memory applies.

## Safety model

Server side, every statement runs through `ExecuteReaderAsync` with `QueryOptions`: `readonly=2`,
`max_result_rows` (the requested cap, `result_overflow_mode=break`), `max_rows_to_read=50 000 000`,
`max_memory_usage=2 GB` and `max_execution_time=15 s`.

Client side, `ClickHouseQueryGuard` accepts one `SELECT` or `WITH … SELECT` and rejects comments,
`;`, `FORMAT`, `SETTINGS`, `INTO OUTFILE`, write and control verbs, and table functions that reach
outside the database (`url`, `s3`, `file`, `remote`, `mysql`, …). The agent's SQL is validated in the
tool, in the neuron and again in the provider.

View filters never touch SQL text: `QueryPlanCompiler` binds column names as `{cN:Identifier}` and
values as typed parameters (`String`, `Float64`, `Date`, `Bool`) and mirrors `TablePolicy` semantics
(case-insensitive text, `neq` keeps nulls, nulls first ascending). A view sort adds every other
orderable column as a tiebreaker so pages never overlap; without one the base query's own
`ORDER BY` decides the page order. Schema reads are pinned to `WHERE database = {db:String}`.

The table-function denylist is a real part of the safety model, not decoration: `readonly=2`
still lets a SELECT call `url()`, `s3()` or `remote()`. Until the dedicated read-only user lands
(TODO below), a new ClickHouse table function that reaches outside the server has to be added to
`ClickHouseQueryGuard`.

## Adding a schema or seed

1. Put an idempotent `.sql` file under `src/Modules/ClickHouse/Aspire.Hosting/Seeds/` (`CREATE … IF NOT EXISTS`,
   inserts guarded by `WHERE (SELECT count() FROM …) = 0`).
2. Register it with `WithSeed("<file name without .sql>")`.
3. Mirror the tables you need in scenarios in `FakeLeads` so tests stay Docker-free.

## Tests

- `tests/DigitalBrain.Tests/Features/clickhouse.feature` runs on the fake provider: schema, typed
  query, refused write, server-side paging/filtering with revision conflicts, and the card signal.
- `ClickHouseQueryGuardFacts`, `ClickHouseTypeMapFacts`, `QueryPlanCompilerFacts` pin the pure parts;
  `ClickHouseTableAgentFacts` proves `read_table` / `update_table_view` / `/ui/tables` on a `chtable-` id.
- `ClickHouseDriverProviderIntegrationFacts` runs the real provider against Testcontainers:

  ```bash
  DIGITALBRAIN_CLICKHOUSE_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.ClickHouseDriverProviderIntegrationFacts
  ```

  It needs Docker and pulls `clickhouse/clickhouse-server:25.8`; without the variable the four facts are skipped.

## Known limits

- A query table has at most 32 columns; column names must be unique and at most 100 characters.
- `clickhouse_query` returns at most 1 000 rows; table pages are at most 200 rows.
- Numbers keep TablePolicy's rule (15 significant digits, magnitude below 2^53); larger values,
  `Int128`+ columns and non-finite floats arrive as text. Text cells are cut at 4 000 characters.
- Counts are clamped to `int.MaxValue`.
- No writes yet.

## TODO (out of scope here)

- Ingest/insert commands (`InsertBinaryAsync`) and the lead-generation crawler as their own module.
- A dedicated read-only ClickHouse user injected through `users.d` so the caps cannot be loosened.
- Vector or full-text indexes in the seed; semantic search.
- Exporting a table card to the spreadsheet card or a CSV download.
