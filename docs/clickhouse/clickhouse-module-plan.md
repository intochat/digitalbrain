# Claude Code brief — ClickHouse module for DigitalBrain

> Paste everything below the line into Claude Code, started from `E:\intochat\digitalbrain`.
> Keep `docs/clickhouse/clickhouse-module-research.md` next to this file; the brief refers to it.

---

You are implementing a new **ClickHouse module** in this repository on a new branch. Work in small, verified steps; build and run the tests after every phase; commit after every phase with a message prefixed `clickhouse:`.

## 0. Ground rules (read before touching anything)

1. Branch: `git switch -c feature/clickhouse-module` from `master`.
2. Read, in this order, before writing code: `CONTEXT.md`, `docs/clickhouse/clickhouse-module-research.md` (design + verified package/API facts), then the reference files listed in §1. Follow the repo's vocabulary: **neuron / signal / synapse / journal**, never "service/agent/grain" in product-facing names.
3. Toolchain is fixed: .NET SDK `11.0.100-rc.1` (`global.json`), Aspire `13.5.3`, Orleans `10.3.1`, Central Package Management (`Directory.Packages.props`, transitive pinning on), `DigitalBrain.slnx`. CI runs `dotnet format whitespace DigitalBrain.slnx --verify-no-changes`, `dotnet build -c Release`, `dotnet test`, and a Flutter web build with `dart format --set-exit-if-changed`. Everything you add must pass those four commands **without Docker**.
4. New packages (add to `Directory.Packages.props`, resolve exact versions with `dotnet package search <id> --exact-match --prerelease` / NuGet before pinning):
   - `Aspire.Hosting.ClickHouse` **13.5.3** (must match the other Aspire packages).
   - `ClickHouse.Driver` — latest stable 1.x (≥ 1.3.0). Do **not** add `ClickHouse.Client`.
   - `Testcontainers.ClickHouse` **4.14.0** — only in the test project, only for the gated integration test (§7).
   If `Aspire.ClickHouse.Driver` turns out to expose an `IServiceCollection`-level registration usable from `ISiloBuilder.Services`, you may use it instead of hand-registering the client; otherwise register `ClickHouse.Driver` manually (that is the assumed path).
5. Module layout must mirror existing modules exactly (three projects under `src/Modules/ClickHouse/`), be added to `DigitalBrain.slnx` under a `/Modules/ClickHouse/` folder, and its `Aspire.Hosting` project referenced from `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` with `IsAspireProjectResource="false"`.
6. No Docker in unit tests: tests use `BrainSimulation` with a **fake provider** selected through the same mechanism Salesforce uses (`DigitalBrainFakes.Enabled(configuration)` and/or provider config key).
7. Do not weaken existing limits or tests. Do not modify `TablePolicy` caps for the in-memory table. Do not add bind mounts.
8. When a fact in this brief conflicts with the actual API you observe (package versions, method signatures), **trust the code you can compile** and note the deviation in the final PR description.

## 1. Reference files to read first

| Purpose | File |
|---|---|
| Container-backed module projection (template) | `src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs` |
| Provider selection + loud misconfiguration error | `src/Modules/Memory/Memory/MemoryModule.cs`, `Qdrant/QdrantVectorMemoryRegistration.cs` |
| Read-only query neuron + native tools + SQL guard | `src/Modules/Salesforce/Salesforce/SalesforceModule.cs`, `SalesforceNativeTools.cs`, `SalesforceQueryGuard.cs`, `Contracts/ISalesforce.cs`, `Contracts/SoqlQuery.cs`, `Contracts/SalesforceQueryResult.cs` |
| Tool → neuron → chat card flow | `src/Modules/Excel/Excel/ExcelNativeTools.cs`, `src/Modules/UI/DigitalBrain.Modules.UI/Ui/UiTools.cs` (`RenderChartAsync`, `WaitForCardAsync`) |
| Table contract, policy, service, endpoints, agent tools | `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Table/*.cs`, `src/Modules/UI/DigitalBrain.Modules.UI/Table/*.cs`, `src/Kernel/DigitalBrain.Silo/Http/TableEndpoints.cs`, `TableAgentTools.cs` |
| UI vocabulary / card kinds / chat card mapping | `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/UIVocabulary.cs`, `Chat/UiCardKinds.cs`, `Chat/UiCardOffer.cs`, `src/Modules/UI/DigitalBrain.Modules.UI/Chat/ChatNeuron.cs`, `UIModule.cs` |
| Wire golden | `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/flutter-wire-contracts.golden.json`, `tests/DigitalBrain.Tests/Features/FlutterWireGoldenFacts.cs` |
| Flutter table/chart/cards | `src/Modules/UI/Flutter/ui/lib/src/components/table/ui_data_table.dart`, `ui_table_controller.dart`, `components/chart/ui_chart.dart`, `models/ui_part.dart`, `chat/ui_chat_builders.dart`, `Flutter/core/lib/src/models/table_models.dart`, `Flutter/shell/lib/workspace/workspace_chat_presentation.dart` |
| AppHost composition | `src/Aspire/DigitalBrain.AppHost/AppHost.cs`, `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/*.cs`, `src/Aspire/DigitalBrain.ServiceDefaults/ServiceDefaultsExtensions.cs` |
| Test harness | `src/Testing/DigitalBrain.Testing/BrainSimulation.cs`, `tests/DigitalBrain.Tests/Features/BrainSteps.cs`, `BrainWorld.cs`, `memory.feature` + `MemorySteps.cs`, `excel.feature` + `ExcelSteps.cs`, `TableNeuronFacts.cs`, `TableAgentFacts.cs`, `uichat.feature` + `UiChatSteps.cs` |

Write a short `docs/clickhouse/NOTES.md` as you go with anything surprising you find (e.g. where `ChatNeuron` maps signals to cards, the exact `AddNativeTool` signature). Keep it; it goes in the PR.

## 2. Phase 1 — Aspire hosting projection

Create `src/Modules/ClickHouse/Aspire.Hosting/DigitalBrain.Modules.ClickHouse.Aspire.Hosting.csproj` (RootNamespace `DigitalBrain.ClickHouse.Aspire.Hosting`, `IsPackable`, references `Aspire.Hosting.ClickHouse`, the module project, and `DigitalBrain.Aspire.Hosting`).

`ClickHouseHostingExtensions.WithClickHouse(this DigitalBrainModuleBuilder<ClickHouseModule> module, ClickHouseHostingOptions? options = null)` following `MemoryHostingExtensions` exactly (state via `GetOrAddState`, `AddProjection` once, `Enable()` idempotent). In `Enable()`:

```csharp
var builder = brain.ApplicationBuilder;
_server = builder.AddClickHouse(ClickHouseNames.Server)          // "clickhouse"
    .WithDataVolume()                                            // /var/lib/clickhouse
    .WithLifetime(ContainerLifetime.Persistent)
    .WithParentRelationship(brain.Resource);
_database = _server.AddDatabase(ClickHouseNames.DatabaseResource, ClickHouseNames.DatabaseName); // "clickhouse-db", "digitalbrain"
```
- Add a dashboard link to the built-in SQL UI: `_server.WithUrlForEndpoint("http", e => new ResourceUrlAnnotation { Url = "/play", DisplayText = "ClickHouse Play", Endpoint = e })` — verify the endpoint name the package uses (`http`) by inspecting the resource's endpoints; adjust if different.
- Seed/init files: `WithClickHouse(o => o.WithSeed("leads"))` adds `_server.WithContainerFiles("/docker-entrypoint-initdb.d", [ new ContainerFile { Name = "001-leads.sql", Contents = <embedded resource text> } ])`. Keep the SQL idempotent (`CREATE DATABASE IF NOT EXISTS digitalbrain; CREATE TABLE IF NOT EXISTS …`). Remember persistent containers get recreated when file contents change; the data volume survives, and init scripts only run on an empty data dir — expose `o.AlwaysRunInitScripts` which sets `WithEnvironment("CLICKHOUSE_ALWAYS_RUN_INITDB_SCRIPTS", "1")` for dev.
- `Apply<TResource>(builder)`: return early if `!_enabled || brain.FakesEnabled`; otherwise
  ```csharp
  builder.WithReference(_database, connectionName: ClickHouseRegistration.DefaultConnectionName)   // "clickhouse"
         .WithAnnotation(new WaitAnnotation(_database.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
         .WithEnvironment(EnvironmentKeys.For(ClickHouseModule.ConfigurationRoot, "Provider"), ClickHouseModule.DriverProviderName)
         .WithEnvironment(EnvironmentKeys.For(ClickHouseModule.ConfigurationRoot, "ConnectionName"), ClickHouseRegistration.DefaultConnectionName);
  ```
  (`WaitAnnotation` on the database resource makes the kernel wait for server health **and** the `CREATE DATABASE` step.)
- Put the seed SQL under `src/Modules/ClickHouse/Aspire.Hosting/Seeds/leads.sql` as an `EmbeddedResource`. Content: the `companies_current`, `facts`, `sources`, `employees` tables from `docs/clickhouse/clickhouse-module-research.md` §5 of the earlier report (ReplacingMergeTree(version) / MergeTree, `Array(String) activity_tags`, `JSON attributes`) **without** the vector/text indexes (keep the seed portable), plus ~30 sample rows across UK/DE/CZ construction/roofing/software companies so the demo conversation works. Insert sample rows with plain `INSERT INTO … VALUES` guarded by `INSERT INTO … SELECT … WHERE (SELECT count() FROM companies_current) = 0` or equivalent idempotent pattern.

Wire it in `AppHost.cs`: `.AddModule<ClickHouseModule>(clickhouse => clickhouse.WithClickHouse(o => o.WithSeed("leads")))` after `MemoryModule`. Add the project reference to the AppHost csproj.

Build the AppHost project. Commit.

## 3. Phase 2 — Contracts

Project `src/Modules/ClickHouse/Contracts/DigitalBrain.Modules.ClickHouse.Contracts.csproj` (RootNamespace `DigitalBrain.ClickHouse`, `GenerateDocumentationFile`, `NoWarn CS1591`, references `Kernel/DigitalBrain.Contracts` and `Modules/UI/DigitalBrain.Modules.UI.Contracts` — the latter only because `IClickHouseTable : ITable`; if you prefer to keep Contracts free of UI, put `IClickHouseTable` in the module project's public surface instead and note it).

Records (all `[GenerateSerializer]`, `[Alias("db.clickhouse.<name>")]`, `[property: Id(n)]`):

- `ClickHouseQuery(string Sql, int MaxRows = 200)`
- `ClickHouseColumn(string Name, string ClickHouseType, string TableType)` — `TableType ∈ text|number|date|boolean` (same domain as `TableColumn.Type`).
- `ClickHouseQueryResult(IReadOnlyList<ClickHouseColumn> Columns, IReadOnlyList<IReadOnlyList<JsonElement>> Rows, long RowCount, bool Truncated, double ElapsedMs)`
- `ReadClickHouseSchema(string? Table = null)`
- `ClickHouseTableInfo(string Name, string Engine, long? TotalRows, IReadOnlyList<ClickHouseColumn> Columns)`, `ClickHouseSchema(string Database, IReadOnlyList<ClickHouseTableInfo> Tables)`
- `ClickHouseConnection(bool Connected, string Database, string? ServerVersion, string Provider)`
- `CreateQueryTable(string Title, string Sql)`, `CreateQueryTableCommand(CommandId Id, CreateQueryTable Table) : Command(Id)`
- `ClickHouseTableState(TableSnapshot? View /* rows always empty */, string? BaseSql, IReadOnlyList<TableOperationResult> Operations)` — or a dedicated record; rows are **never** persisted.
- `ClickHouseSignals`: `QueryTableCreating`, `QueryTableUpdating`, and the outward `TableRendered` (define the string in `UIVocabulary` if the UI module owns card signals — see Phase 5).
- `ClickHouseJson` — `[JsonSerializable]` context for all of the above (copy the `MemoryJson`/`UIJson` pattern, including `Accepted<string>`).

Interfaces:

```csharp
[Alias("clickhouse")]
public interface IClickHouse : INeuron
{
    /// <summary>Runs one read-only SELECT with server-side caps and returns typed rows.</summary>
    [ReadOnly, Alias("query")]
    Task<ClickHouseQueryResult> Query(ClickHouseQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads tables and columns of the configured database; omit Table for the index.</summary>
    [ReadOnly, Alias("schema")]
    Task<ClickHouseSchema> ReadSchema(ReadClickHouseSchema query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("connection")]
    Task<ClickHouseConnection> ReadConnection(CancellationToken cancellationToken = default);
}

[Alias("clickhouse.table")]
public interface IClickHouseTable : ITable
{
    /// <summary>Schedules creation of a table whose rows are served live from a ClickHouse query.</summary>
    [Alias("create-query")]
    Task<Accepted<string>> CreateFromQuery(CreateQueryTableCommand command, CancellationToken cancellationToken = default);
}
```

Build. Commit.

## 4. Phase 3 — Module, provider, neuron, guard, fake

Project `src/Modules/ClickHouse/ClickHouse/DigitalBrain.Modules.ClickHouse.csproj` (RootNamespace `DigitalBrain.ClickHouse`, `NoWarn ORLEANSEXP005;CA1812`, references Contracts, `Kernel/DigitalBrain`, `Modules/AI/Contracts` (for `AddNativeTool`), `Modules/UI/DigitalBrain.Modules.UI.Contracts`, package `ClickHouse.Driver`).

### 4.1 `ClickHouseModule : Core.IModule`
Constants: `ConfigurationRoot = "DigitalBrain:ClickHouse"`, `ProviderConfigurationKey = "DigitalBrain:ClickHouse:Provider"`, `DriverProviderName = "ClickHouse"`, `ClickHouseRegistration.ConnectionNameConfigurationKey = "DigitalBrain:ClickHouse:ConnectionName"`, `DefaultConnectionName = "clickhouse"`, `ClickHouseNames.NeuronType = "clickhouse"`, `TableType = "clickhouse-table"`, `TableIdPrefix = "chtable-"`.

`Configure(ISiloBuilder builder)`:
- Resolve provider like `MemoryModule`: if `DigitalBrainFakes.Enabled(configuration)` **or** provider unset → register `FakeClickHouseProvider` (singleton) and skip the driver; if provider == `ClickHouse` → require `ConnectionStrings:<name>` (throw the same style of `InvalidOperationException` if missing) and register:
  ```csharp
  services.AddHttpClient(ClickHouseRegistration.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(2));
  services.TryAddSingleton(sp => new ClickHouseClient(connectionString, sp.GetRequiredService<IHttpClientFactory>(), ClickHouseRegistration.HttpClientName));
  services.AddSingleton<IClickHouseProvider, ClickHouseDriverProvider>();
  services.AddHealthChecks().AddCheck<ClickHouseHealthCheck>("clickhouse", tags: ["ready"]);   // PingAsync via provider
  ```
  Mirror `MemoryModule`'s guard: if a `ConnectionStrings:clickhouse` exists but `Provider` isn't `ClickHouse`, throw with a helpful message.
- Register `ITableSource` for `chtable-` → `clickhouse-table` (see Phase 5).
- Register native tools (Phase 4).
- Add `"ClickHouse.Driver"` to the tracing sources in `DigitalBrain.ServiceDefaults` (`AddSource(...)` next to the existing sources) so driver spans reach the dashboard.

### 4.2 `IClickHouseProvider` (internal) — the seam that makes the fake possible
Use the interface from the research doc §5: `QueryAsync(sql, maxRows, ct)`, `ExecutePlanAsync(QueryPlan, ct)`, `DescribeAsync(sql, ct)`, `ReadSchemaAsync(table?, ct)`, `PingAsync(ct)`.

`QueryPlan(string BaseSql, IReadOnlyList<TableFilter> Filters, TableSort? Sort, int Offset, int Limit)`; `QueryPage(IReadOnlyList<ClickHouseColumn> Columns, IReadOnlyList<TableRow> Rows, long Total, long Filtered)`.

### 4.3 `ClickHouseDriverProvider`
- All reads go through `ClickHouseClient.ExecuteReaderAsync(sql, parameters, options)` with
  ```csharp
  new QueryOptions {
      QueryId = $"digitalbrain-{Guid.NewGuid():N}",
      MaxExecutionTime = TimeSpan.FromSeconds(15),
      CustomSettings = new Dictionary<string, object> {
          ["readonly"] = 2, ["max_result_rows"] = maxRows, ["result_overflow_mode"] = "break",
          ["max_rows_to_read"] = 50_000_000, ["max_memory_usage"] = 2_000_000_000 } }
  ```
  If the server rejects `readonly=2` together with other settings, drop `readonly` from `CustomSettings` and rely on the guard + a later reader-user (note it in NOTES.md).
- `QueryAsync`: run `ClickHouseQueryGuard.Validate(sql)` then execute `SELECT * FROM (<sql>) AS q LIMIT {limit:UInt64}` with `limit = maxRows + 1` to detect truncation; map columns with `ClickHouseTypeMap.ToTableType(reader.GetDataTypeName(i))` and cells to `JsonElement` (`number` → `JsonSerializer.SerializeToElement(Convert.ToDouble/decimal)`, `boolean` → bool, `date` → `yyyy-MM-dd` string, everything else → `ToString()`/JSON-serialised string; `null` → JSON null). Guard numbers by `TablePolicy`'s rule (≤ 15 significant digits, |x| ≤ 9007199254740991) — otherwise emit text.
- `DescribeAsync`: `DESCRIBE (<sql>)` **or** execute `SELECT * FROM (<sql>) AS q LIMIT 0` and read `GetSchemaTable()`/`GetDataTypeName` — pick whichever the driver supports cleanly; return `ClickHouseColumn`s.
- `ExecutePlanAsync`: validate every `filter.ColumnId`/`sort.ColumnId` against `DescribeAsync(BaseSql)` (cache per BaseSql in a bounded `ConcurrentDictionary`), then build
  ```
  SELECT * FROM (<BaseSql>) AS q [WHERE p0 AND p1 …] [ORDER BY {sortCol:Identifier} ASC|DESC] LIMIT {limit:UInt64} OFFSET {offset:UInt64}
  SELECT count() FROM (<BaseSql>) AS q                      -- Total
  SELECT count() FROM (<BaseSql>) AS q [WHERE …]            -- Filtered
  ```
  Predicates use `{colN:Identifier}` for the column and typed parameters `{pN:String|Float64|Date|Bool}` for values; `contains` → `positionCaseInsensitiveUTF8({col:Identifier}, {p:String}) > 0`; `isNull`/`isNotNull` → `IS NULL`/`IS NOT NULL`; `eq/neq/gt/gte/lt/lte` → operators. Never interpolate user text into SQL. Clamp counts to `int.MaxValue` when building `TableSnapshot`.
- `ReadSchemaAsync`: query `system.tables` (`database = {db:String}`) and `system.columns` (`database = {db:String} [AND table = {t:String}]`), never exposing other databases. Database name comes from the connection string (`Database=`) — parse it with the driver's `ClickHouseConnectionStringBuilder` (verify the type name) or a tiny parser like `QdrantVectorMemoryRegistration.TryParseConnectionString`.
- `PingAsync`: `SELECT version()` with a 3 s timeout.

### 4.4 `ClickHouseQueryGuard` (static, `partial` with `GeneratedRegex`, modelled on `SalesforceQueryGuard`)
Reject: empty, > 20 000 chars, `;` outside string literals, `--`, `/* */`, `#` comments, anything not starting with `SELECT`/`WITH` (after trimming), and — outside string literals — the tokens `INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE|RENAME|ATTACH|DETACH|OPTIMIZE|SYSTEM|KILL|GRANT|REVOKE|SET|INTO OUTFILE|FORMAT|SETTINGS`. Reason strings must be actionable (`"Use one read-only SELECT (or WITH … SELECT). Comments, multiple statements, FORMAT/SETTINGS clauses and writes are not allowed."`). Write `ClickHouseQueryGuardFacts` with ≥ 12 cases (accepted and rejected, including a `'--'` inside a string literal that must be accepted).

### 4.5 `FakeClickHouseProvider`
- In-memory `Dictionary<string, FakeTable>` keyed by **table name**, plus a `Dictionary<string, (columns, rows)>` keyed by **normalised BaseSql** for plans. Seed it at construction with a deterministic small dataset that matches the `leads.sql` seed shape (same tables/columns, ~12 rows) so BDD scenarios can assert on it.
- `QueryAsync(sql)`: support the shape the tools/tests use: `SELECT * FROM <table> [WHERE …simple…] [LIMIT n]` by delegating to the same in-memory filter engine; for any other SQL, require an exact registered result (`Script(sql, result)`), otherwise throw `NotSupportedException` with the sql — tests must register what they need. Keep it small; do not write a SQL parser.
- `ExecutePlanAsync`: look up `BaseSql`, apply filters/sort/paging with the **same semantics as `TablePolicy.Query`** (reuse the comparison logic by extracting `TablePolicy.Matches/Compare` into an internal shared helper in the UI module if that is cleanly possible; otherwise duplicate with a comment and a test that pins parity).
- Expose test hooks: `RegisterTable(name, columns, rows)`, `Script(sql, result)`, `FailNext(exception)`.

### 4.6 `ClickHouseNeuron` `[GrainType("clickhouse")]` : `Neuron<ClickHouseState>`, `IClickHouse`
- State: `ClickHouseState(long QueriesServed, DateTimeOffset? LastQueryAt)` (optional; keep persistence minimal).
- `[ReadOnly] Query` → validate arguments (`MaxRows` 1..1000, non-blank Sql) → provider → return; wrap provider exceptions into a `ClickHouseQueryException` that carries the server message (the agent needs the ClickHouse error text to self-correct hallucinated columns).
- `[ReadOnly] ReadSchema`, `[ReadOnly] ReadConnection` straightforward.
- No `ReceiveAsync` cases yet (return on unknown signals).

### 4.7 `ClickHouseTableNeuron` `[GrainType("clickhouse-table")]` : `Neuron<ClickHouseTableState>`, `IClickHouseTable`
- `CreateFromQuery`: guard SQL, `DescribeAsync` to derive `TableColumn`s (id = column name, label = column name, type = mapped), validate ≤ 32 columns; schedule `QueryTableCreating` with the command; receipt is `Id.Name`.
- `Create(CreateTableCommand)` from `ITable`: reject with `CommandRejectedException("use create-query", …)` — a ClickHouse table has no static rows.
- `Update(UpdateTableCommand)`: same revision/`TableOperationResult` semantics as `TableNeuron`; validate view through `TablePolicy.ValidateView(snapshotWithoutRows, view)` (it only needs columns).
- `[ReadOnly] Read(ReadTable)`: `TablePolicy.ValidatePage`; build `QueryPlan` from state; `await provider.ExecutePlanAsync`; return `TableSnapshot` with live rows and counts, `Kind == "table"`.
- `ReceiveAsync`: on `QueryTableCreating` → persist state (Title, BaseSql, Columns, VisibleColumns = all, Revision 1) and `Announce(Signal.FromJson(UIVocabulary.TableRendered, new UiCard(Id.Name, title), UIJson.Default.UiCard))`; on `QueryTableUpdating` → apply view, bump revision, announce `TableRendered` again (so the chat card refreshes). Persist `TableOperationResult`s exactly like `TableNeuron` (dedupe by `CommandId`, keep last 255).

Build. Commit.

## 5. Phase 4 — Native tools (agent surface)

`ClickHouseNativeTools(IGrainFactory grains, INeuronInvoker invoker, TableService tables)` registered via `AddNativeTool` in the module (`services.AddSingleton<ClickHouseNativeTools>()` + one `AddNativeTool` per tool, like Salesforce):

1. `clickhouse_schema(table?)` → `IClickHouse.ReadSchema`. Description: "Read the ClickHouse database schema (tables, engines, columns, types). Start with no table for the index. Always read the schema before writing SQL; column names must match exactly."
2. `clickhouse_query(sql, maxRows=200)` → `IClickHouse.Query`. Description: "Run one read-only ClickHouse SELECT and return typed rows (max 1000). Use ClickHouse SQL (`count()`, `groupArray`, `has(tags,'x')`, `hasAny`, `positionCaseInsensitiveUTF8`, `LIMIT`). For results the person should see or refine, call show_query_table instead of pasting rows." On failure return the server error text so the model can fix the query.
3. `show_query_table(chatName, title, sql)` → creates `chtable-<guid>` via `IClickHouseTable.CreateFromQuery`, **after** `grains.GetGrain<INeuron>(neuron.ToGrainId()).Connect(chat, UIVocabulary.TableRendered)` (copy `ExcelNativeTools`/`UiTools.RenderChartAsync` including `WaitForCardAsync` with `UiCardKinds.Table`), registers the table in the catalog through `TableService` (extend `TableService.CreateAsync`-style registration for external sources: add `TableService.RegisterAsync(NeuronId)` that writes the `TableListed` synapse) and returns: `"Table '<title>' is now showing in the chat as card '<name>' (id chtable-…). Refine it with update_table_view (filters are AND-combined); read a page with read_table."`
4. Rely on the **existing** `read_table`, `update_table_view`, `list_tables`, `render_chart` tools for refinement and charts — do not duplicate them. Verify that `TableAgentTools` resolves `chtable-` ids after the `ITableSource` change (Phase 5) — this is how "from these companies, which have more than 50 employees?" becomes `update_table_view` with an added `employee_count gte 50` filter.

Add an operator hint to the assistant's system prompt only if the repo has a per-module prompt contribution seam (search for how Salesforce/Excel describe their tools to Ino); otherwise the tool descriptions above are the contract.

Build. Commit.

## 6. Phase 5 — UI: table source routing, table card, Flutter

### 6.1 UI module (C#)
- `ITableSource { string IdPrefix; string GrainType; }` + `TableSource` record in `DigitalBrain.Modules.UI` (public), registered by `UIModule` for `("table-", UIVocabulary.TableType)`. `TableService` takes `IEnumerable<ITableSource>` and resolves the grain type by longest matching prefix; `ListAsync` accepts any registered `GrainType`. Add `TableService.RegisterAsync(NeuronId table, ct)` used by Phase 4.
- `UiCardKinds.Table = "table"`; `UIVocabulary.TableRendered = "TableRendered"` (document the body `{ "name": "...", "title": "..." }` next to the other four ui signals).
- `ChatNeuron`: extend the mapping that turns `ChartRendered`/`GraphRendered`/`ImageDescribed`/`SheetChanged` into `UiCardOffer` so `TableRendered` → `UiCardOffer(UiCardKinds.Table, name, title)`. Locate it by searching for `UiCardKinds.Chart` in `ChatNeuron.cs`/`ChatBodies.cs`.
- Update `flutter-wire-contracts.golden.json` via the existing golden test; review the diff — only additive changes are acceptable.
- Add a `uichat.feature` scenario: a connected `clickhouse-table` neuron announcing `TableRendered` yields a `table` card on the running turn (follow the existing chart-card scenario).

### 6.2 Flutter
- `ui/lib/src/models/ui_part.dart`: add `UiTableRefPart` (`kindName = 'table-ref'`, fields `name`, `caption`, `copyText => caption`) and register it in `UiPart.tryParse`.
- Wherever `*-ref` parts become widgets in chat (`ui_chat_builders.dart` / `workspace_chat_presentation.dart` — find the `chart-ref` case), render `UiDataTable(controller: UiTableController(...))` that reads `/ui/tables/<name>` through the existing core client (`digitalbrain_flutter` `ui_client.dart` table functions; see `table_client_test.dart`) with `showTitle: true` and the "Use in chat" affordance wired the same way workspace tables do. The card must refresh when a later `table-ref` offer for the same name arrives (revision bump).
- `ui_chart.dart`: honour `part.chartKind`:
  - `bar` → existing `IntervalMark`.
  - `line` → `LineMark` + `PointMark` (small size), same colour token.
  - Add `selections: {'tap': PointSelection(on: {GestureType.hover, GestureType.tap}, dim: Dim.x)}`, `tooltip: TooltipGuide(...)`, `crosshair: CrosshairGuide(...)` using the current `graphic` API (check `pubspec.lock` for the version and read its docs before writing).
  - Ellipsize category labels longer than ~12 chars on the axis (`Defaults.horizontalAxis` label callback or a custom `AxisGuide` with a `LabelStyle`/`textStyle` and rotated labels).
  - Add a widget test in `ui/test/` for `line` and for empty data.
- Run `dart format core ui shell`, `flutter analyze`, and the widget tests (`flutter test` in `ui` and `shell`).

Build everything (`dotnet build DigitalBrain.slnx -c Release`, `flutter build web --release --no-tree-shake-icons` in `shell`). Commit.

## 7. Phase 6 — Tests

In `tests/DigitalBrain.Tests` (add project references to the new module + contracts; the module must be included in the test brain's `ModuleManifest` wherever `memory`/`excel` are — find how `BrainSteps` builds the manifest):

1. `Features/clickhouse.feature` + `ClickHouseSteps.cs` (fake provider, no Docker):
   - "The schema of the configured database is readable" — `ReadSchema()` lists `companies_current` with an `employee_count` column of type `number`.
   - "A read-only query returns typed rows" — `Query("SELECT name, employee_count FROM companies_current WHERE country = 'GB' LIMIT 10")` returns > 0 rows, `Truncated == false`.
   - "A write is refused before it reaches the server" — `Query("INSERT INTO …")` fails with a `CommandRejectedException`/`ArgumentException` containing "read-only".
   - "A query table pages and filters on the server" — create via `CreateFromQuery`, wait until `Read(0, 5)` returns 5 rows and `TotalRows == 12`; `Update` with filter `employee_count gte 50` → `FilteredRows` drops; `Read(offset: 5, limit: 5)` returns the next page; a stale `ExpectedRevision` yields `conflict`.
   - "A query table announces a card" — connect `claude` to `clickhouse-table:<name>` for `TableRendered`, create, assert the incoming journal contains `TableRendered {"name":…,"title":…}` (mirror `excel.feature`'s "Two rapid cell edits" journal assertions).
2. `ClickHouseQueryGuardFacts.cs`, `ClickHouseTypeMapFacts.cs` (number/date/boolean/text mapping incl. `Nullable(LowCardinality(String))`, `Array(String)`, `DateTime64(3)`), `QueryPlanCompilerFacts.cs` (asserts the exact SQL text and parameter set produced for each operator; identifiers bound via `Identifier` parameters; no user text interpolated).
3. `TableAgentFacts` extension: `read_table`/`update_table_view` work against a `chtable-` id.
4. Gated integration test `ClickHouseDriverProviderIntegrationFacts.cs`: skipped unless `DIGITALBRAIN_CLICKHOUSE_TESTS=1`; uses `Testcontainers.ClickHouse` (`new ClickHouseBuilder("clickhouse/clickhouse-server:<same tag as Aspire package>")`) to run the real provider against the `leads.sql` seed (apply the seed SQL through the driver) and assert: schema read, paging plan, `contains` filter, `readonly` refusal of `INSERT`, truncation flag. Document how to run it in `docs/clickhouse/NOTES.md`.

All of CI's commands must pass locally: `dotnet restore DigitalBrain.slnx`, `dotnet format whitespace DigitalBrain.slnx --verify-no-changes --no-restore`, `dotnet build DigitalBrain.slnx -c Release --no-restore`, `dotnet test DigitalBrain.slnx -c Release --no-build --no-restore`, plus the Flutter steps from `.github/workflows/ci.yml`.

Commit.

## 8. Phase 7 — Run it, document it, open the PR

1. `aspire run` (or the repo's usual launch) with the default AppHost; confirm in the dashboard: `clickhouse` container nested under `brain`, `clickhouse-db` healthy, kernel waits for it, "ClickHouse Play" link opens, and `/play` shows `digitalbrain.companies_current` with seed rows.
2. Manual chat smoke test (write the transcript into `docs/clickhouse/NOTES.md`):
   - "Which tables do we have in ClickHouse?" → `clickhouse_schema`.
   - "Find me all companies in the UK which do construction" → `show_query_table` with `hasAny(activity_tags, ['construction','roofing'])`-style SQL → table card appears.
   - "From these, which have more than 50 employees?" → `update_table_view` adds `employee_count gte 50`; card refreshes; filter chip visible.
   - "Chart them by country" → `clickhouse_query` (GROUP BY) then `render_chart` (`bar`), and "as a line" → `line`.
3. Write `docs/clickhouse/README.md`: what the module is, AppHost options (`WithClickHouse`, seeds, `AlwaysRunInitScripts`), configuration keys, safety model, how to add a schema/seed, how to run the gated integration tests, known limits (32 columns, 1 000 rows per agent query, counts clamped, no writes yet).
4. Update `CONTEXT.md` "Specialist modules" paragraph: add `IClickHouse` in one sentence using the repo's vocabulary (neuron, signal `TableRendered`, synapse to the chat).
5. Open a PR `feature/clickhouse-module → master` titled "ClickHouse module: Aspire hosting, read-only query neuron, live query tables, chart line kind". The description lists every deviation from this brief with the reason, the exact package versions pinned, and the manual smoke-test transcript.

## 9. Definition of done

- [ ] New branch with one commit per phase; CI commands green without Docker.
- [ ] `Aspire.Hosting.ClickHouse` 13.5.3 + `ClickHouse.Driver` 1.x pinned in `Directory.Packages.props`; no `ClickHouse.Client`.
- [ ] `WithClickHouse()` projection with data volume, persistent lifetime, parent relationship, `/play` link, `WithContainerFiles` seed, env/config wiring, fakes short-circuit.
- [ ] `IClickHouse` neuron (`query`, `schema`, `connection`) with server caps and a client-side guard; ClickHouse error text surfaces to the agent.
- [ ] `IClickHouseTable : ITable` neuron with server-side paging/filter/sort, revision conflicts, `TableRendered` card; `TableService` routes `chtable-` ids; existing `read_table`/`update_table_view` work on it.
- [ ] Native tools `clickhouse_schema`, `clickhouse_query`, `show_query_table`.
- [ ] Flutter: `table-ref` card renders `UiDataTable`; `UiChart` supports `line` + tooltips; golden updated; widget tests added.
- [ ] Tests: feature file + facts + gated Testcontainers integration test.
- [ ] `docs/clickhouse/README.md`, `NOTES.md`, `CONTEXT.md` touched; PR opened with deviations listed.

## 10. Out of scope (do not start; leave TODOs in README)

- Ingest/insert commands (`InsertBinaryAsync`) and the lead-generation crawler — separate module.
- Dedicated read-only ClickHouse user/profile injected via `users.d` (hardening follow-up).
- Vector/full-text indexes in the seed; embeddings-based semantic search.
- Exporting a table card to the spreadsheet card or CSV download.
