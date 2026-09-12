# ClickHouse as a DigitalBrain module — research

_Prepared 2026-09-12 for `E:\intochat\digitalbrain`. Companion file: `clickhouse-module-plan.md` (the Claude Code brief)._

---

## 1. TL;DR — decisions

| Question | Decision | Why |
|---|---|---|
| Reuse Drizzle's custom `ClickHouseResource`? | **No.** Use the official **`Aspire.Hosting.ClickHouse` 13.5.3** package. | It ships `AddClickHouse`, `AddDatabase`, `WithDataVolume`, a `/ping` health check, generated password parameter, `CREATE DATABASE IF NOT EXISTS` on ready, and Aspire-standard connection properties. Drizzle's resource predates all of this and still uses the passwordless `default` user (which current images refuse network access to). |
| .NET client? | **`ClickHouse.Driver` 1.x** (official, ClickHouse-maintained). | `ClickHouse.Client` 7.9.1 (Drizzle) was adopted by ClickHouse and renamed; `ClickHouseBulkCopy` is deprecated in favour of `ClickHouseClient.InsertBinaryAsync`. Targets net6–net10 (runs on the repo's .NET 11 RC via the net10.0 asset). |
| Where does ClickHouse live in DigitalBrain? | A new **module** `DigitalBrain.Modules.ClickHouse` with the standard three projects (`Contracts`, module, `Aspire.Hosting`), exposing an **`IClickHouse` neuron** (read-only query + schema), **native AI tools**, and a **query-backed table neuron** that implements the existing `ITable` contract so the existing Flutter `UiDataTable` renders ClickHouse result sets with **server-side paging/filter/sort**. | Mirrors the Memory→Qdrant and Salesforce patterns already in the repo. The query-backed table is what lets "huge data" reach the UI without copying rows into the 1 000-row in-memory `TableNeuron`. |
| How do init scripts get in? | Aspire 13 **`WithContainerFiles("/docker-entrypoint-initdb.d", …)`** on the ClickHouse container resource (inline `ContainerFile` entries or a host source path). | No host bind mount, works cross-platform, and is the documented Aspire 13 mechanism. Persistent containers are recreated when file contents change, so keep seeds idempotent (`CREATE … IF NOT EXISTS`). |
| Multi-turn refinement ("from these, which have > 50 employees?") | Model the **saved query table as the conversation's working set**; refinement = the existing `update_table_view` tool adding an AND filter, which the ClickHouse table neuron pushes down into SQL. | Reuses an existing agent tool and UI affordance (filter chips). Avoids the "stateless multi-turn text-to-SQL collapses" problem from the earlier report by carrying state in a durable neuron, not in the prompt. |
| Tests without Docker in CI | A **fake provider** that executes the same `QueryPlan` in memory over canned tables, gated by `DigitalBrainFakes` (like `FakeSalesforceProvider`). Optional **Testcontainers.ClickHouse 4.14.0** integration tests gated by an env var. | CI is `ubuntu-latest` + `dotnet test` only; the repo's `BrainSimulation` is in-process. |

---

## 2. What the repo looks like today (facts gathered from `E:\intochat\digitalbrain`)

### 2.1 Toolchain
- SDK **11.0.100-rc.1** (`global.json`, `allowPrerelease: true`), **Aspire 13.5.3** (`Aspire.AppHost.Sdk/13.5.3`, `AspireUseCliBundle=true`), **Orleans 10.3.1**, Central Package Management with `CentralPackageTransitivePinningEnabled=true`, solution file `DigitalBrain.slnx`, xunit v3 + **Reqnroll 3.3.4** BDD, `dotnet format whitespace --verify-no-changes` in CI, Flutter 3.47.2 web build in CI.
- CI has no container step. Unit tests run on an in-process Orleans `InProcessTestCluster` (`src/Testing/DigitalBrain.Testing/BrainSimulation.cs`).

### 2.2 Module anatomy (the template to copy)
Every module has:
1. `Contracts` project — grain interfaces (`IMemory : INeuron`, `ISalesforce : INeuron`) with `[Alias]`, and `[GenerateSerializer]` records with `[property: Id(n)]` and `[Alias("db.x.y")]`; a `XxxJson` source-generated JSON context; a `XxxSignals`/`XxxVocabulary` static class of signal-type strings. References only `Kernel/DigitalBrain.Contracts`.
2. Module project — `public sealed class XxxModule : Core.IModule { void Configure(ISiloBuilder builder) }` registering services, providers, and **native AI tools** via `builder.Services.AddNativeTool("tool_name", services => …AIFunction…)`; neurons are `[GrainType("x")] internal sealed class XxxNeuron(NeuronRuntime runtime, [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TState>> state) : Neuron<TState>(runtime, state), IXxx`. Commands are `ExecuteCommandAsync(Descriptor("alias"), command, Json.Command, Json.Accepted, args => { validate; return new Accepted<T>(receipt, Schedule(Signal.FromJson(...))); })`; reactions happen in `ReceiveAsync(SignalDelivery, ct)`; `Announce(...)` fires outward signals; `[ReadOnly]` methods read state or providers.
3. `Aspire.Hosting` project — extension on `DigitalBrainModuleBuilder<TModule>` (e.g. `WithQdrant()`, `WithHostedMcp()`) that creates a `DigitalBrainModuleProjection` state via `module.Brain.GetOrAddState(...)` + `module.AddProjection(state)`. `Apply<TResource>(IResourceBuilder<TResource>)` runs against the **kernel** project and calls `WithReference(resource, connectionName: …)`, `WithAnnotation(new WaitAnnotation(resource, WaitType.WaitUntilHealthy, exitCode: 0))`, and `WithEnvironment("DigitalBrain__Xxx__…", …)`; `brain.FakesEnabled` short-circuits real wiring. Config keys map via `EnvironmentKeys.For("DigitalBrain:Xxx:Root", "Name")` → `DigitalBrain__Xxx__Root__Name`.

Concrete references:
- `src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs` — container-backed projection (`builder.AddQdrant("qdrant").WithParentRelationship(brain.Resource)`).
- `src/Modules/Memory/Memory/MemoryModule.cs` — provider selection by `DigitalBrain:Memory:Provider`, loud failure if a connection string exists but the provider isn't selected, in-memory fallback.
- `src/Modules/Salesforce/Salesforce/SalesforceModule.cs`, `SalesforceNativeTools.cs`, `SalesforceQueryGuard.cs` — read-only query neuron + `AddNativeTool` + a regex/lexer guard that requires one bounded `SELECT … WHERE … LIMIT n` and rejects comments, multiple statements and locking clauses.
- `src/Modules/Excel/Excel/ExcelNativeTools.cs` — the canonical "tool → create neuron → `Connect(chat, SignalType)` → invoke command → card appears in chat" flow.
- AppHost: `src/Aspire/DigitalBrain.AppHost/AppHost.cs` composes `builder.AddDigitalBrain("brain").AddModule<XModule>(x => x.WithY())`; module Aspire.Hosting projects are referenced with `IsAspireProjectResource="false"`; the kernel project gets `.WithReference(brain)`, which applies all projections.

### 2.3 UI plumbing relevant to "visualise ClickHouse data"
- **Tables** (`src/Modules/UI`): `ITable` (`Create`, `Update`, `Read(offset, limit)`, `ReadOperation`) on grain type `table`; `TableSnapshot { Columns(id,label,type∈text|number|date|boolean), Rows(id, cells JsonElement[]), Filters, Sort, VisibleColumns, TotalRows, FilteredRows, Offset, Limit }`; `TablePolicy` caps **1 000 rows / 32 columns**, filters `eq neq contains gt gte lt lte isNull isNotNull`, page limit ≤ 200; `TableService` (catalog via durable synapses on `ui-table-catalog`, `Create/Read/Update/List`); Silo `TableEndpoints` (`GET/POST /ui/tables`, `GET /ui/tables/{id}`, `PUT /ui/tables/{id}/view`) and `TableAgentTools` (`create_table`, `read_table`, `update_table_view`, `list_tables`).
- **Flutter** `UiDataTable` (`Flutter/ui/lib/src/components/table/ui_data_table.dart`) is explicitly a **server-filtered, server-sorted** widget driven by `UiTableController` with `read`/`update` callbacks — i.e. it already assumes the data source may be remote and paged. `table_models.dart` mirrors `TableSnapshot` 1:1.
- **Chat cards**: `UiCardKinds` = `chart | image | spreadsheet | graph` — **there is no `table` card kind today**. Cards travel as `UiCardOffer(Kind, Name, Caption)` and render in Flutter as `*-ref` parts (`chart-ref`, `spreadsheet-ref`, `graph-ref`, `image-ref` in `ui_part.dart`) resolved by name. Tables currently appear only as workspace artifacts.
- **Charts**: `render_chart` tool → `ChartNeuron` (`ChartState(Title, ChartKind, Points≤512)`) → `ChartRendered` → `chart-ref` card. Flutter `UiChart` (`graphic` package) draws **bars only** (`IntervalMark`) and ignores `chartKind == 'line'`.
- `DigitalBrain.Modules.UI.Contracts/flutter-wire-contracts.golden.json` + `tests/…/FlutterWireGoldenFacts.cs` pin the wire contracts — any new card kind/part must update the golden.

### 2.4 What Drizzle has (and what's outdated)
`E:\projects\Drizzle` (Aspire 9.0.0, `ClickHouse.Client` 7.9.1):
- `ClickHouseResource : ContainerResource, IResourceWithConnectionString` with `http` 8123 + `tcp` 9000 endpoints, `ConnectionStringExpression = Host=…;Port=…` (no protocol/user/password), a `ConnectionStringRedirectAnnotation` check, `.WithImage("clickhouse")` (untagged), `.WithHttpHealthCheck("/ping")`.
- `WithInitScript` = host **bind mount** of one file to `/docker-entrypoint-initdb.d/init.sql`; `WithDataVolume` to `/var/lib/clickhouse`; hand-written `config.xml`/`users.xml` giving `default` an **empty password** open to `0.0.0.0/0`.
- Collector writes with `ClickHouseBulkCopy` over a `DataTable`.

Migration checklist (Aspire 9 → 13.5 / today's ecosystem):

| Drizzle (2024) | Replace with (2026) |
|---|---|
| Hand-rolled `ClickHouseResource` + builder extensions | `Aspire.Hosting.ClickHouse` → `builder.AddClickHouse("clickhouse", port?, userName?, password?)`, `.AddDatabase(name, databaseName?)`, `.WithDataVolume(name?, isReadOnly?)`, `.WithDataBindMount(source, isReadOnly?)` |
| `WithImage("clickhouse")` untagged | Package pins `clickhouse/clickhouse-server` with a tested tag; override only via `WithImageTag` if needed |
| `Host=…;Port=…` connection string | Aspire emits `Host=;Port=;Username=;Password=;Database=` and per-property env vars `CLICKHOUSE_HOST`, `_PORT`, `_USERNAME`, `_PASSWORD`, `_DATABASENAME` |
| Passwordless `default` user via `users.xml` | Package sets `CLICKHOUSE_USER=default` + generated `CLICKHOUSE_PASSWORD` stored as parameter `Parameters:clickhouse-password`; pass your own via `AddParameter(..., secret: true)` |
| `WithBindMount(file, "/docker-entrypoint-initdb.d/init.sql")` | `WithContainerFiles("/docker-entrypoint-initdb.d", [new ContainerFile { Name = "001-schema.sql", Contents = … }])` or `WithContainerFiles(dest, sourcePath)` |
| `ConnectionStringRedirectAnnotation` | Not needed — `WithReference(databaseResource, connectionName:)` handles it |
| `ClickHouse.Client` 7.9.1 + `ClickHouseBulkCopy` | `ClickHouse.Driver` 1.x — `ClickHouseClient` singleton, `ExecuteReaderAsync(sql, parameters, QueryOptions)`, `InsertBinaryAsync` |
| Persistent container by lifetime only | Also `WithParentRelationship(brain.Resource)` so it nests under the brain in the dashboard; optional `WithUrlForEndpoint("http", e => new ResourceUrlAnnotation { Url = "/play", DisplayText = "ClickHouse Play" })` |

---

## 3. Verified ecosystem facts (September 2026)

### 3.1 `Aspire.Hosting.ClickHouse` 13.5.3 (official; also listed under the ClickHouse NuGet profile)
- `aspire add clickhouse` / `<PackageReference Include="Aspire.Hosting.ClickHouse" />`.
- API: `AddClickHouse(name, port?, userName?, password?) → ClickHouseServerResource`; `AddDatabase(name, databaseName?) → ClickHouseDatabaseResource : IResourceWithConnectionString, IResourceWithParent<ClickHouseServerResource>`; `WithDataVolume(name?, isReadOnly?)` (mounts `/var/lib/clickhouse`); `WithDataBindMount(source, isReadOnly?)`.
- Runs `clickhouse/clickhouse-server`; default credentials `CLICKHOUSE_USER=default` + random `CLICKHOUSE_PASSWORD` persisted in the AppHost secret store as `Parameters:clickhouse-password`.
- Database is created with `CREATE DATABASE IF NOT EXISTS` when the server resource becomes ready; the hosting health check does `GET /ping`.
- Connection properties: server → `Host`, `Port` (8123), `Username`, `Password`; database adds `DatabaseName`. Example: `Host=localhost;Port=8123;Username=default;Password=…;Database=clickhousedb`.
- No `WithInitFiles`/`WithInitScript` on this integration (as of 13.5.3) — use `WithContainerFiles` on the server resource (it is a `ContainerResource`).
- TypeScript AppHost support not yet available; C# only (fine here).

### 3.2 `Aspire.ClickHouse.Driver` (client integration)
- `builder.AddClickHouseDataSource(connectionName: "…")` on `IHostApplicationBuilder` registers `IClickHouseClient` **and** `ClickHouseDataSource`; `AddKeyedClickHouseDataSource(name)` for multiples. Settings section `Aspire:ClickHouse:Driver` (`ConnectionString`, `DisableHealthChecks`, `DisableTracing`, `DisableMetrics` (default true — no metrics yet), `HealthCheckTimeout`).
- Health check calls `PingAsync`; tracing ActivitySource is **`ClickHouse.Driver`**; logging categories `ClickHouse.Driver`, `.BulkCopy`, `.Client`, `.Command`, `.Connection`, `.Transport`.
- **Caveat for this repo:** modules configure an `ISiloBuilder`, not `IHostApplicationBuilder`. The Memory module registers `QdrantClient` manually rather than using `Aspire.Qdrant.Client`. Do the same here: build a singleton `ClickHouseClient` from the `ConnectionStrings:<name>` value with `IHttpClientFactory`, register a health check yourself, and add `AddSource("ClickHouse.Driver")` to the ServiceDefaults tracing so the driver's spans show in the dashboard.

### 3.3 `ClickHouse.Driver` 1.x (official .NET client; repo `ClickHouse/clickhouse-cs`)
- 1.0.0 went stable 2026-03-02; semver from then on. Latest seen on NuGet ~1.3.x–1.4.x (docs reference behaviours "before 1.4.0") — **let Claude Code resolve the current stable version**. Targets .NET 6/8/9/10.
- Primary API: `ClickHouseClient` (thread-safe singleton; `new ClickHouseClient(connectionString | ClickHouseClientSettings, IHttpClientFactory?, httpClientName?)`).
  - `ExecuteReaderAsync(sql, ClickHouseParameterCollection? parameters, QueryOptions? options)` → `ClickHouseDataReader` (typed getters, `GetFieldValue<T>`, `GetSchemaTable`).
  - `ExecuteScalarAsync`, `ExecuteNonQueryAsync`, `QueryAsync<T>` (POCO stream after `RegisterPocoType<T>()`), `ExecuteRawResultAsync("… FORMAT JSONEachRow")` for pass-through JSON.
  - `InsertBinaryAsync(table, columns, IEnumerable<object[]> rows, InsertOptions)` (RowBinary, batching, parallelism, zstd default) — replaces `ClickHouseBulkCopy`. POCO inserts via `RegisterBinaryInsertType<T>()`.
- Parameters: `{name:Type}` syntax; `{name:Identifier}` for safe table/column names; `ClickHouseParameterCollection.AddParameter(name, value)`; `@name` placeholders are rewritten client-side. Parameters travel as URL query params (URL-length limit ⇒ don't pass huge IN-lists).
- `QueryOptions`: `QueryId`, `Database`, `Roles`, `CustomSettings` (any server setting, e.g. `max_result_rows`, `result_overflow_mode`, `max_rows_to_read`, `max_memory_usage`, `readonly`), `MaxExecutionTime` (→ `max_execution_time`), `CustomHeaders`, `BearerToken`, `AcceptEncoding`.
- Connection-string extras: `Protocol=http|https`, `Timeout`, `Compression`, `set_<setting>=…` (e.g. `set_readonly=1`), `Roles=`, `JsonReadMode=String|Binary`, `UseSession`.
- Type mapping highlights: `Array(T)`→`T[]`, `Map`→`Dictionary`, `JSON`→`JsonObject` (or string), `DateTime`→`DateTime` (Kind by column tz), `UUID`→`Guid`, `LowCardinality(T)`→`T`, `Enum8/16`→string label, `Decimal`→`decimal`/`ClickHouseDecimal`.
- Don't `new ClickHouseConnection(...)` per call (creates a pool each time); use one `ClickHouseClient` or `ClickHouseDataSource`.

### 3.4 `clickhouse/clickhouse-server` image behaviour (Docker Hub / docs, current)
- Env: `CLICKHOUSE_USER`, `CLICKHOUSE_PASSWORD`, `CLICKHOUSE_DB`, `CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT` (allow `default` to manage users), `CLICKHOUSE_SKIP_USER_SETUP=1` (insecure, opens `default` without password), `CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS` (re-run init scripts even when the data dir already exists).
- **If none of `CLICKHOUSE_USER`/`CLICKHOUSE_PASSWORD`/`CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT` is set, the `default` user has network access disabled** — the Drizzle users.xml approach is obsolete; the Aspire package sets user+password so this is a non-issue.
- Init: `*.sql`, `*.sql.gz`, executable `*.sh` (run), non-executable `*.sh` (sourced) under `/docker-entrypoint-initdb.d`, alphabetical order, only on first start unless `CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS` is set. `CLICKHOUSE_USER/PASSWORD` are used by `clickhouse-client` during init.
- Ports: 8123 HTTP, 9000 native, 9004 MySQL-wire, 9005 Postgres-wire, 9009 interserver. `/ping` returns `Ok.`; `/play` is the built-in SQL UI.
- Overrides go in `/etc/clickhouse-server/config.d/*.xml` and `/etc/clickhouse-server/users.d/*.xml` (the entrypoint writes `users.d/default-user.xml` itself).

### 3.5 Aspire 13 `WithContainerFiles`
- `WithContainerFiles(destinationPath, IEnumerable<ContainerFileSystemItem> entries, defaultOwner?, defaultGroup?, umask?)` with `ContainerFile { Name, Contents|SourcePath, Mode }` and `ContainerDirectory { Name, Entries }`; overload `WithContainerFiles(destinationPath, sourcePath, options?)` copies from the host; overload with async callback `(ContainerFileSystemCallbackContext, ct) => …` for files derived from other resources/parameters.
- For `ContainerLifetime.Persistent` containers, **changing file contents recreates the container** — keep seed SQL idempotent and stable. `PublishWithContainerFiles` is the publish-time counterpart.

### 3.6 Testing
- **`Testcontainers.ClickHouse` 4.14.0** (net8.0 / netstandard2.0): `new ClickHouseBuilder("clickhouse/clickhouse-server:<tag>").Build()`, `StartAsync()`, `GetConnectionString()`. Needs Docker; GitHub `ubuntu-latest` has Docker, but the repo's CI does not currently rely on it — gate such tests behind an environment variable and skip otherwise.
- `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`) is heavier (boots the whole AppHost incl. Azurite/Qdrant) — not recommended for this module's unit tests.

### 3.7 Flutter `graphic`
- Has `LineMark`, `PointMark`, `AreaMark`, `IntervalMark`, `TooltipGuide`, `CrosshairGuide`, and `selections` (`PointSelection`) — enough to add a `line` kind and hover tooltips to `UiChart` without switching libraries.

---

## 4. Safety model for LLM-generated SQL (what the module must enforce)

Server-side (cheap, authoritative):
- Every agent/UI query runs with `QueryOptions.CustomSettings`: `readonly=2` (read-only, but per-request settings still accepted), `max_result_rows=<cap>` + `result_overflow_mode='break'`, `max_rows_to_read`, `max_memory_usage`, and `MaxExecutionTime` (e.g. 15 s). Use `readonly=1` only if you also stop sending other settings in the same request (ClickHouse refuses setting changes under `readonly=1`).
- Phase-2 hardening: a dedicated reader user (`users.d/*.xml` injected with `WithContainerFiles`, or `CREATE USER … SETTINGS readonly=2 … ; GRANT SELECT ON <db>.* …` in an init script) whose profile pins the caps so the app can't loosen them.

Client-side (defence in depth, mirrors `SalesforceQueryGuard`):
- Exactly one statement; must start with `SELECT` or `WITH`; reject `;`, `--`, `/* */`, `INSERT|ALTER|DROP|CREATE|SYSTEM|KILL|OPTIMIZE|TRUNCATE|RENAME|ATTACH|DETACH|GRANT|REVOKE|SET `, `INTO OUTFILE`, trailing `FORMAT`, and `SETTINGS` clauses; require an outer `LIMIT` ≤ cap (or wrap: `SELECT * FROM (<sql>) LIMIT {limit:UInt64}`).
- UI filters never touch SQL text: they compile to `WHERE` predicates over **validated column identifiers** (`{col:Identifier}`) with **typed parameters** (`{p0:String}`, `{p1:Float64}`, `{p2:Date}`); `contains` → `positionCaseInsensitiveUTF8(col, {p:String}) > 0`; `isNull` → `col IS NULL`.
- Schema discovery via `system.tables` / `system.columns` restricted to the configured database (`WHERE database = {db:String}`) so the agent never sees `system.*`.

---

## 5. Design: how ClickHouse maps onto DigitalBrain concepts

```
AppHost                     Kernel (silo)                                   Flutter
─────────                   ────────────────────────────────────────────   ───────────────
AddClickHouse("clickhouse") ClickHouseModule : IModule                      UiDataTable (unchanged)
 .AddDatabase("clickhouse-db")  ├─ IClickHouseProvider                        └─ new table-ref card
 .WithDataVolume()              │    ├─ ClickHouseDriverProvider (real)        └─ UiChart: + line/tooltip
 .WithContainerFiles(initdb)    │    └─ FakeClickHouseProvider (fakes/tests)
 .WithParentRelationship        ├─ ClickHouseNeuron  [GrainType("clickhouse")]  : IClickHouse
WithClickHouse() projection     │    Query / ReadSchema / ReadConnection (ReadOnly)
 → ConnectionStrings__clickhouse├─ ClickHouseTableNeuron [GrainType("clickhouse-table")] : IClickHouseTable : ITable
 → DigitalBrain__ClickHouse__*  │    CreateFromQuery → TableRendered → card; Read/Update push filters+paging into SQL
                                └─ native tools: clickhouse_schema, clickhouse_query, show_query_table
                                   (+ existing: update_table_view, read_table, render_chart)
```

Neurons
- **`IClickHouse`** (`[Alias("clickhouse")]`, one instance per configured database, e.g. `clickhouse:default`): `[ReadOnly] Query(ClickHouseQuery{Sql, MaxRows})` → `ClickHouseQueryResult{Columns[(Name, ClickHouseType, TableType)], Rows(JsonElement[][]), RowCount, Truncated, ElapsedMs}`; `[ReadOnly] ReadSchema(ReadClickHouseSchema{Table?})` → `ClickHouseSchema{Database, Tables[(Name, Engine, Rows?, Columns[(Name, Type, Comment)])]}`; `[ReadOnly] ReadConnection()` → `ClickHouseConnection{Connected, Database, ServerVersion, Provider}`. No persistent state needed beyond a small usage counter (optional). Later: `Ingest(IngestRows)` command scheduled as a reaction that calls `InsertBinaryAsync` and announces `RowsIngested`.
- **`IClickHouseTable : ITable`** (`[GrainType("clickhouse-table")]`, ids `chtable-<guid>`): adds `[Alias("create-query")] CreateFromQuery(CreateQueryTableCommand{Title, Sql})`. State = `{Title, BaseSql, Columns, Filters, Sort, VisibleColumns, Revision}`; **rows are never persisted**. `Read(offset, limit)` compiles `QueryPlan{BaseSql, Filters, Sort, Offset, Limit}` → provider → `TableSnapshot` (with `TotalRows`, `FilteredRows` from `count()` queries, clamped to `int`). `Update` follows `TableNeuron`'s revision/`TableOperationResult` semantics so `TableService`, `TableEndpoints`, `TableAgentTools` and the Flutter controller all work unchanged. Announces `TableRendered {name,title}` (new UI signal) so the chat shows a `table` card.
- `TableService` must resolve grain type from the id prefix. Add an `ITableSource(Prefix, GrainType)` DI seam in the UI module (default `table-` → `table`); the ClickHouse module registers `chtable-` → `clickhouse-table`. `ListAsync` accepts any registered source type.

Chat/UI
- New `UiCardKinds.Table = "table"`, `UIVocabulary.TableRendered`, `ChatNeuron` maps it to a `UiCardOffer`, Flutter `UiTableRefPart` (`table-ref`) renders `UiDataTable` through the existing `/ui/tables/{id}` client. Update the wire golden.
- Charts: agent chains `clickhouse_query` (GROUP BY) → `render_chart`. Flutter `UiChart`: honour `line` (`LineMark` + `PointMark`), add `TooltipGuide`/`CrosshairGuide`, ellipsize long category labels.

Provider abstraction (what makes the fake possible)
```csharp
public interface IClickHouseProvider
{
    Task<ClickHouseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken ct);                // agent free-form SELECT
    Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken ct);                              // table paging
    Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken ct);              // DESCRIBE (SELECT …)
    Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken ct);
    Task<ClickHouseConnection> PingAsync(CancellationToken ct);
}
```
The real provider compiles `QueryPlan` to `SELECT * FROM (<BaseSql>) AS q WHERE … ORDER BY … LIMIT {limit:UInt64} OFFSET {offset:UInt64}`; the fake keeps `Dictionary<baseSql, (columns, rows)>` and applies the same filter/sort/page semantics as `TablePolicy.Query` in memory.

Type mapping ClickHouse → `TableColumn.Type`: `(U)Int*`, `Float*`, `Decimal*` → `number` (guard 15 significant digits / |x| ≤ 2^53, else `text`); `Bool` → `boolean`; `Date`, `Date32` → `date` (`yyyy-MM-dd`); `DateTime*`, `String`, `UUID`, `Enum*`, `IPv*`, `Array`, `Map`, `JSON`, `Tuple` → `text` (ISO/JSON serialised). `LowCardinality(T)`/`Nullable(T)` unwrap to `T`.

---

## 6. Options considered

| Topic | Chosen | Rejected & why |
|---|---|---|
| Hosting resource | Official `Aspire.Hosting.ClickHouse` | Porting Drizzle's resource: more code, no parameters/health/DB creation, obsolete auth model. |
| Client | `ClickHouse.Driver` directly (like `Qdrant.Client`) | `Aspire.ClickHouse.Driver` needs `IHostApplicationBuilder`, which `IModule.Configure(ISiloBuilder)` doesn't have; revisit if the kernel exposes a host hook. |
| Getting rows to the UI | Query-backed `ITable` implementation (server paging) | Copying rows into `create_table` (≤ 1 000 rows, 32 cols; fine as a fallback and for tiny results). |
| Agent access | In-process native tools (`AddNativeTool`) calling the neuron | External `mcp-clickhouse` MCP server: extra process, separate auth, bypasses neuron/journal semantics. The brain's own MCP surface (`DigitalBrain.Mcp`) can still expose the neuron. |
| Tests | Fake provider + optional Testcontainers | Aspire testing host (too heavy), embedded chDB (no .NET story). |
| Lead-gen schema | Ship as an **optional dev seed** (`WithClickHouse(seed: …)`) with `companies_current`/`facts`/`employees` from the earlier report | Baking the domain into the generic module — the lead generator should be its own consumer/module later. |

---

## 7. Risks and unknowns to verify during implementation

1. **Package versions**: pin `Aspire.Hosting.ClickHouse` to `13.5.3` (matches the rest of Aspire); resolve latest stable `ClickHouse.Driver` (≥ 1.3.0) and `Testcontainers.ClickHouse` (4.14.0) with `dotnet package search` before editing `Directory.Packages.props`. With `CentralPackageTransitivePinningEnabled=true`, transitive `Microsoft.Extensions.Http`/`Logging.Abstractions` from the driver will be pinned to the repo's 11.0 RC versions — build to confirm.
2. **.NET 11 RC** compatibility: `ClickHouse.Driver` ships net10.0 assets; expect it to load. If not, fall back to `Protocol=http` raw `HttpClient` + `FORMAT JSONEachRow` (documented `ExecuteRawResultAsync` path).
3. **`readonly` semantics**: confirm that per-request `readonly=2` + other settings works on the server version the package pins; otherwise move caps into the user profile.
4. **`TableSnapshot` counts are `int`**: clamp `count()` results; show "≥ 2 147 483 647" is unrealistic anyway for a UI table.
5. **Persistent container + `WithContainerFiles`**: any change to seed contents recreates the container (data volume survives, but init scripts only run on an empty data dir). Provide `CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS=1` in dev if seeds must be re-applied, and keep seeds idempotent.
6. **`ChatNeuron` card mapping**: the exact switch that turns `ChartRendered`/`SheetChanged`/… into `UiCardOffer` must be located and extended for `TableRendered`; check `tests/DigitalBrain.Tests/Features/uichat.feature` for the assertion style.
7. **Golden wire contracts**: `flutter-wire-contracts.golden.json` will change; regenerate via the existing test and review the diff.
8. **Fakes**: when `DigitalBrain__Fakes__Enabled=true` the projection must skip the container wiring (`brain.FakesEnabled`) and the module must select `FakeClickHouseProvider`, exactly as Salesforce does.

---

## 8. Sources
- Aspire ClickHouse hosting: https://aspire.dev/integrations/databases/clickhouse/clickhouse-host/ · get started: https://aspire.dev/integrations/databases/clickhouse/clickhouse-get-started/ · client/connection properties: https://aspire.dev/integrations/databases/clickhouse/clickhouse-connect/ · API reference (v13.5.3): https://aspire.dev/reference/api/typescript/aspire.hosting.clickhouse/
- Aspire container files: https://aspire.dev/id/app-host/container-files (and the `WithContainerFiles` overloads in `dotnet/aspire` `ContainerResourceBuilderExtensions.cs`)
- ClickHouse .NET client docs: https://clickhouse.com/docs/integrations/language-clients/csharp/overview · repo: https://github.com/ClickHouse/clickhouse-cs · 1.0.0 announcement: https://clickhouse.com/blog/clickhouse-driver-1_0_0-official-dotnet-client · NuGet profile (Aspire.Hosting.ClickHouse, Aspire.ClickHouse.Driver, EF Core provider, Serilog sink): https://www.nuget.org/profiles/ClickHouse
- ClickHouse Docker image: https://clickhouse.com/docs/install/docker · https://hub.docker.com/r/clickhouse/clickhouse-server/ · entrypoint: https://github.com/ClickHouse/ClickHouse/blob/master/docker/server/entrypoint.sh
- Testcontainers: https://www.nuget.org/packages/Testcontainers.ClickHouse/4.14.0 · https://testcontainers.com/modules/clickhouse/
- Flutter `graphic`: https://pub.dev/documentation/graphic/latest/graphic/graphic-library.html
- Earlier report (this conversation): "ClickHouse for Lead-Generation Platforms: 2026 Evaluation" — schema (`companies_current`, `facts`, `sources`, `employees`), ReplacingMergeTree/`argMax` guidance, multi-turn text-to-SQL findings.
