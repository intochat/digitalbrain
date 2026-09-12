# ClickHouse module — implementation notes

Running log of what the code actually looks like versus the brief in `clickhouse-module-plan.md`.
Deviations are listed at the end and repeated in the PR description.

## Pinned versions (verified with `dotnet package search --exact-match`)

| Package | Version | Where |
|---|---|---|
| `Aspire.Hosting.ClickHouse` | 13.5.3 | `Directory.Packages.props`, `Aspire.Hosting` project |
| `ClickHouse.Driver` | 1.4.0 (newest stable 1.x on nuget.org; 1.4.0-rc1/rc2 skipped) | module project |
| `Testcontainers.ClickHouse` | 4.14.0 (4.15.0 exists; brief pins 4.14.0) | test project only |

`ClickHouse.Client` is not referenced anywhere.

## Things worth knowing (found while reading the reference files)

- **Card mapping lives in `ChatNeuron.OfferAsync`.** `ReceiveAsync` lists the four ui signals
  (`ChartRendered`, `GraphRendered`, `ImageDescribed`, `SheetChanged`) and `OfferAsync` maps the
  type to a `UiCardKinds` value with `_ => UiCardKinds.Spreadsheet` as the default. `TableRendered`
  must be added to both places or it becomes a spreadsheet card.
- **`AddNativeTool` signature:** `IServiceCollection AddNativeTool(string name, Func<IServiceProvider, AIFunction> create)`
  in `DigitalBrain.Modules.AI.Contracts`. Tools are resolved lazily per name.
- **Module loading:** the kernel resolves `DigitalBrain__Modules__N = "FullName, Assembly"` with
  `Type.GetType`, so `DigitalBrain.Silo.csproj` must reference the module project or the silo cannot
  load it at runtime. The brief does not mention this; Memory/Salesforce are referenced the same way.
- **Descriptors walk inherited interfaces.** `DescriptorTable` enumerates every `INeuron` interface a
  grain class implements (including `ITable` through `IClickHouseTable`) and picks the JSON context
  from the assembly that declares each interface. So `create`/`update`/`read`/`operation` on the
  query table neuron use `UIJson`, and `create-query` uses `ClickHouseJson`.
- **`TableService` is the only routing point.** It hard-codes the `table-` prefix and the `table`
  grain type in `Table(id)` and `ListAsync`; the `ITableSource` seam replaces both.
- **`TablePolicy` is internal** to `DigitalBrain.Modules.UI`; the ClickHouse module reaches it through
  `InternalsVisibleTo` rather than a copy.
- **Working-tree line endings are LF** even though `.editorconfig` says CRLF; `.gitattributes` normalises
  on commit and `dotnet format whitespace --verify-no-changes` passes either way.
- **Docker is available locally** (engine 29.7.2), so the gated Testcontainers test and `aspire run`
  can both be exercised on this machine.

## Integration run against a real server

`DIGITALBRAIN_CLICKHOUSE_TESTS=1` ran `ClickHouseDriverProviderIntegrationFacts` on this machine against
`clickhouse/clickhouse-server:25.8` through Testcontainers 4.14.0 (4 facts, 35 s including the pull).
What that proved:

- `Seeds/leads.sql` applies as written on 25.8 (the `values()` table function, `Array(String)`,
  the `JSON` column, `ReplacingMergeTree(version)` and the count()-guarded idempotent inserts).
- `readonly=2` sent together with `max_result_rows`, `result_overflow_mode`, `max_rows_to_read`,
  `max_memory_usage` and `max_execution_time` in one request is accepted, and an `INSERT` under
  those settings is refused by the server. The fallback in the brief (dropping `readonly`) was not needed.
- `{name:Identifier}`, `{p:Float64}`, `{p:Date}` and `{p:Bool}` parameters bind as compiled; the
  ClickHouse column type names come back through `GetDataTypeName` and map as expected.

How to run it:

```bash
DIGITALBRAIN_CLICKHOUSE_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.ClickHouseDriverProviderIntegrationFacts
```

## Deviations from the brief

1. `WithClickHouse` takes `Action<ClickHouseHostingOptions>? configure` (the usage shown in the brief,
   `WithClickHouse(o => o.WithSeed("leads"))`) rather than an options instance.
2. `ClickHouseNames` lives in the Contracts project (like `UIVocabulary`/`ExcelVocabulary`) so the
   hosting projection and the tests share one vocabulary without referencing module internals.
3. The seed starts with `SET allow_experimental_json_type = 1;` so the `JSON attributes` column also
   creates on 24.8–25.2 images where the type was still gated; on newer images the setting is an
   accepted no-op.
4. `ClickHouseNeuron` derives from the non-generic `Neuron` (like `PlainNeuron`) instead of carrying a
   `ClickHouseState(QueriesServed, LastQueryAt)`: `[ReadOnly]` methods cannot save a snapshot, so the
   counter could never have been written.
5. `QueryPlan` carries the table's persisted `ClickHouseColumn` list (`SourceColumns` in the state)
   instead of re-describing `BaseSql` through a bounded cache. Filters were already validated against
   those columns by `TablePolicy.ValidateView`, and the ClickHouse type is needed to decide which
   columns can serve as `ORDER BY` tiebreakers.
6. `QueryPage` is `(Rows, Total, Filtered)`; the columns are already known to the caller.
7. Compiled pages always end with every orderable column as an `ORDER BY` tiebreaker (`ASC NULLS FIRST`
   / `DESC NULLS LAST` on the sort column). Without it LIMIT/OFFSET pages over an unsorted or
   duplicate-keyed result can overlap or skip rows between requests.
8. The guard additionally rejects `EXCHANGE`, `MOVE` and the table functions that reach outside the
   database (`url`, `s3`, `file`, `remote`, `mysql`, `postgresql`, …): `readonly=2` alone does not stop
   a SELECT from fetching an arbitrary URL.
9. `IClickHouse.Query` throws `ClickHouseQueryException` (Orleans-serialisable) for guard rejections
   rather than `ArgumentException`; the message still contains "read-only" as the brief's scenario expects.
10. `ClickHouseTableNeuron.Read` wraps provider failures in a new `TableSourceException` (UI module,
    serialisable), mapped to 502 by `TableEndpoints` and to `source_failed` by `TableAgentTools`, so the
    kernel never has to know ClickHouse types.
11. `CreateFromQuery` cannot call `DescribeAsync` inside the command (command bodies are synchronous
    validators); the describe happens in the `QueryTableCreating` reaction and a bad query becomes an
    `invalid` operation result, which `TableService`/the tool surface as the server's message.
12. `show_query_table(title, sql, chatName?)`: `chatName` is optional and the result is the first page
    as a `kind: "table"` JSON object plus a `message`. The production workspace chat runs the AG-UI
    `ConversationalAgent`, which has no uichat behind it and renders `kind: "table"` tool results as
    live tables; a bare string would have shown nothing there. With a `uichat` the card path is used too.
13. The AG-UI `ConversationalAgent` allowlists `clickhouse_schema`, `clickhouse_query` and
    `show_query_table` next to the Salesforce tools and gets one instruction sentence about them; the
    repo has no per-module prompt seam, and `NativeTools.Resolve` skips names nobody registered.
14. `TableService` keeps a single constructor with an optional `IEnumerable<ITableSource>` so the
    existing `new TableService(brain.Grains)` tests keep compiling.
15. `TablePolicy.Apply(source)` was extracted from `Query` so the fake provider reuses the exact filter
    and sort engine; `Query` still calls it.
16. The wire golden did not change: `TableRendered`, `UiCardKinds.Table` and `ITableSource` add no
    record to `DigitalBrain.Modules.UI.Contracts`, so `FlutterWireGoldenFacts` passes untouched.
17. The Flutter table card creates its own `UiTableController` per card and re-reads the table on a
    later offer for the same name; controllers are not shared across cards.

## `aspire run` observations (2026-09-12)

- Dashboard: `clickhouse` (container) is nested under `brain`, lifetime Persistent, volume
  `…-clickhouse-data` on `/var/lib/clickhouse`, health `clickhouse_http_/ping_200_check` Healthy, and the
  `http` endpoint carries both the raw URL and the **ClickHouse Play** link (`http://localhost:<port>/play`).
  `clickhouse-db` is Healthy and the kernel showed `Waiting … clickhouse-db` until it was.
- The kernel received `ConnectionStrings__clickhouse`, `DigitalBrain__ClickHouse__Provider` and
  `DigitalBrain__ClickHouse__ConnectionName` plus Aspire's `CLICKHOUSE_HOST/PORT/USERNAME/PASSWORD/DATABASENAME`.
- The container console shows `/entrypoint.sh: running /docker-entrypoint-initdb.d/001-leads.sql`; inside the
  container `clickhouse-client` reports 30 companies (GB 11, DE 10, CZ 9), 12 employees, 10 facts, 5 sources.
- **Aspire.Hosting.ClickHouse 13.5.3 pins the image tag `latest`** (`docker.io/clickhouse/clickhouse-server:latest`),
  which resolved to 26.8.2.7 on this machine. The gated Testcontainers facts pin `25.8` instead so they stay
  reproducible; the seed applied on both.
- The dashboard `rebuild` command on the kernel is not usable within one run: the restarted silo finds the
  previous silo's membership row (same per-run cluster id) and dies with
  `OrleansClusterConnectivityCheckFailedException`. Restarting the whole AppHost (`aspire stop` + `aspire run`)
  is the working loop; this predates the module (see the cluster-id comment in `AppHost.cs`).
- The kernel's `/mcp` (with `fire`, `connect`, `call`) is only mapped when `DigitalBrain:Graph:Enabled` is true;
  the uichat smoke run below started the AppHost with `DigitalBrain__Graph__Enabled=true` in its environment.

## Manual chat smoke test (2026-09-12, live kernel, OpenAI gpt-5.6 luna)

Both transcripts were captured over HTTP against the running kernel with the seeded ClickHouse 26.8.2.7 container.
Tool results are abbreviated to their first characters; the workspace transcript shows every tool call.

### Workspace chat (AG-UI `/agent`)

```text

> Which tables do we have in ClickHouse?
  [tool call] clickhouse_schema {}
  [tool result] {"database": "digitalbrain", "tables": [{"name": "companies_current", "engine": "ReplacingMergeTree", "totalRows": 30, "columns": [{"name": "company_id", "clickHouseType": "String", "tableType": "text", "sampleValues": null}, {"name": "name", "clickHouseType": "String", "tableType": "text", "sampleValues": null}, {"name": "country", "clickHouseType": "LowCardinality(String)", "tableType": "text", "sampleValues": ["CZ", "DE", "GB"]}, {"name": "city", "clickHouseType": "String", "tableType": "text", "sampleValues": null}, {"name": "website", "clickHouseType": "String", "tableType": "text", "sampleValues": null}, {"name": "industry", "clickHouseType": "LowCardinality(String)", "tableType": "text", "sampleValues": ["Construction", "Software"]}, {"name": "activity_tags", "clickHouseType": "Array(String)", "tableType": "text", "sampleValues": null}, {"name": "employee_count", "clickHouseType":
  [assistant] We have 4 ClickHouse tables in the `digitalbrain` database:
              
              - `companies_current` — 30 rows
              - `employees` — 12 rows
              - `facts` — 10 rows
              - `sources` — 5 rows

> Find me all companies in the UK which do construction
  [tool call] show_query_table {"title":"UK Construction Companies","sql":"SELECT company_id AS company_id, name AS name, country AS country, city AS city, website AS website, industry AS industry, employee_count AS employee_count, revenue_eur AS revenue_eur, founded_year AS founded_year, is_active AS is_active FROM companies_current WHERE country = 'GB' AND industry = 'Construction' ORDER BY name"}
  [tool result] {"kind": "table", "id": "chtable-cc21ffda82b944ee815a91adf9872ba9", "title": "UK Construction Companies", "revision": 1, "columns": ["company_id", "name", "country", "city", "website", "industry", "employee_count", "revenue_eur", "founded_year", "is_active"], "filters": [], "sort": null, "totalRows": 8, "filteredRows": 8, "rows": [["gb-001", "Thames Roofing Ltd", "GB", "London", "thamesroofing.example", "Construction", 64, 8200000, 1998, true], ["gb-002", "Northgate Builders", "GB", "Manchester", "northgatebuilders.example", "Construction", 120, 21500000, 1985, true], ["gb-003", "Pennine Slate & Tile", "GB", "Leeds", "pennineslate.example", "Construction", 18, 2400000, 2005, true]], "message": "Table 'UK Construction Companies' is saved as chtable-cc21ffda82b944ee815a91adf9872ba9; the workspace opens it from this result. Refine it with update_table_view (filters are AND-combined); read a
  [assistant] I found **8 UK construction companies** and saved them in the interactive table **“UK Construction Companies.”** It includes their location, website, employee count, revenue, founding year, and active status.

> From these, which have more than 50 employees?
  [tool call] read_table {"id":"chtable-cc21ffda82b944ee815a91adf9872ba9","offset":0,"limit":50}
  [tool result] {"kind": "table", "id": "chtable-cc21ffda82b944ee815a91adf9872ba9", "title": "UK Construction Companies", "revision": 1, "columns": ["company_id", "name", "country", "city", "website", "industry", "employee_count", "revenue_eur", "founded_year", "is_active"], "filters": [], "sort": null, "totalRows": 8, "filteredRows": 8, "rows": [["gb-001", "Thames Roofing Ltd", "GB", "London", "thamesroofing.example", "Construction", 64, 8200000, 1998, true], ["gb-002", "Northgate Builders", "GB", "Manchester", "northgatebuilders.example", "Construction", 120, 21500000, 1985, true], ["gb-003", "Pennine Slate & Tile", "GB", "Leeds", "pennineslate.example", "Construction", 18, 2400000, 2005, true]], "message": null}
  [tool call] update_table_view {"id":"chtable-cc21ffda82b944ee815a91adf9872ba9","view":{"expectedRevision":1,"filters":[{"columnId":"employee_count","operator":"gt","value":50}],"sort":{"columnId":"employee_count","descending":true},"visibleColumns":["company_id","name","country","city","website","industry","employee_count","revenue_eur","founded_year","is_active"]}}
  [tool result] {"kind": "table", "id": "chtable-cc21ffda82b944ee815a91adf9872ba9", "title": "UK Construction Companies", "revision": 2, "columns": ["company_id", "name", "country", "city", "website", "industry", "employee_count", "revenue_eur", "founded_year", "is_active"], "filters": [{"columnId": "employee_count", "operator": "gt", "value": 50}], "sort": {"columnId": "employee_count", "descending": true}, "totalRows": 8, "filteredRows": 3, "rows": [["gb-002", "Northgate Builders", "GB", "Manchester", "northgatebuilders.example", "Construction", 120, 21500000, 1985, true], ["gb-008", "Severn Groundworks", "GB", "Gloucester", "severngroundworks.example", "Construction", 88, 12800000, 1993, true], ["gb-001", "Thames Roofing Ltd", "GB", "London", "thamesroofing.example", "Construction", 64, 8200000, 1998, true]], "message": null}
  [assistant] Three companies have more than 50 employees:
              
              - **Northgate Builders** — 120 employees
              - **Severn Groundworks** — 88 employees
              - **Thames Roofing Ltd** — 64 employees
              
              The table has been filtered and sorted by employee count.
```

### uichat `smoke` with `agent:smoke` instructed with clickhouse_schema, clickhouse_query, show_query_table, render_chart

```text
[instruct agent:smoke] delivered=1

> Find me all companies in the UK which do construction
  [card] kind=table name=chtable-2994a37431aa4bfa89b6388c626cb2db caption=UK construction companies
  [Completed] Found 8 UK construction companies. They’re displayed in the **UK construction companies** table, including names, locations, websites, employee counts, revenue, founding years, and activity status.

> Chart them by country
  [card] kind=chart name=chart-8a3b3ee7 caption=Companies by country
  [Completed] Charted companies by country: GB (11), DE (10), and CZ (9).

> Show that as a line chart instead
  [card] kind=chart name=chart-184b7629 caption=Facts over time
  [Completed] Done — the line chart is now showing in the chat.
```

The first run of the workspace transcript (before the fixes in the `clickhouse: run findings` commit) failed twice:
the model passed `chatName: "uichat:desk"` although that workspace has no chat, so the tool waited 30 s for a card
that could not arrive and surfaced as "Function failed"; and it filtered `country = 'UK'` where the data says `GB`.
The card wait is now best effort and never fails after the table exists, `clickhouse_schema` returns sample values
for categorical columns, and the workspace agent is told to omit `chatName` and to spell values as sampled.

## Deviations added after the run

18. `ClickHouseColumn` gained `SampleValues` (`IReadOnlyList<string>?`, Orleans id 3): schema reads return up
    to 12 distinct values for `LowCardinality(String)` and `Enum*` columns (at most 24 sampled columns per
    read, one `SELECT DISTINCT toString(col) … LIMIT 12` each, identifiers bound as parameters). The first
    live run showed the model guessing `country = 'UK'`; with the samples it wrote `country = 'GB'`.
19. `show_query_table` waits for the chat card only while the named chat has a running turn, treats a
    missed card as "saved, not carded" instead of failing, and maps any `TimeoutException` to a `pending`
    result; the workspace agent's instructions tell it to omit `chatName`.
