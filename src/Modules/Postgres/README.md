# Postgres

Postgres exposes one read-only database neuron, `DigitalBrain.Postgres.IPostgres`.
The module follows Time's contracts, implementation and unit-test project layout,
with a separate `DigitalBrain.Modules.Postgres.Aspire.Hosting` project for AppHosts.

```csharp
var postgres = brain.Get<DigitalBrain.Postgres.IPostgres>("default");
var schema = await postgres.ReadSchema(new());
var result = await postgres.Query(new("select id, name from public.people", MaxRows: 100));
var connection = await postgres.ReadConnection();
```

All neuron identities use the module's configured database. Results are read live;
the neuron does not persist query results. The assistant integration can open live table windows backed by the same connection.

## Hosting

Register `PostgresModule` with the brain, or call `silo.AddPostgres()` when configuring
the silo directly. The module delegates its registration to that extension.
Set `ConnectionStrings:postgres` through the host's secret configuration.
Both Npgsql connection strings and `postgres://` / `postgresql://` URIs are supported.

To select a different connection name, configure the module with
`module.WithConnection("analytics")` and supply `ConnectionStrings:analytics`.
The corresponding public setting is `DigitalBrain:Postgres:ConnectionName`.
Missing or malformed connections fail startup validation. The `postgres` readiness
health check probes connectivity. Registration is idempotent, and the module owns
a keyed data source so it can coexist with Supabase and other database modules.
The host disposes its pool.

## Aspire hosting

Reference `DigitalBrain.Modules.Postgres.Aspire.Hosting` from the AppHost. Like
ClickHouse, the module offers an opt-in managed resource using the official
`Aspire.Hosting.PostgreSQL` integration:

```csharp
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Postgres;

var brain = builder.AddDigitalBrain("brain")
    .WithModule<PostgresModule>(module => module.WithPostgres());

builder.AddProject<Projects.Api>("api").WithReference(brain);
```

This creates `postgres-server` and `postgres-database`, injects
`ConnectionStrings__postgres` into consumers, and waits for database readiness.
By default the database is named `postgres`, and the server uses a data volume
and persistent container lifetime, matching ClickHouse's local hosting defaults.

```csharp
var brain = builder.AddDigitalBrain("brain")
    .WithModule<PostgresModule>(module => module.WithConnection("reporting").WithPostgres(options =>
    {
        options.DatabaseName = "analytics";
        options.PersistentStorage = false;
    }));
```

Here `WithConnection` selects the connection name, and the following `WithPostgres`
enables provisioning. Consumers receive `ConnectionStrings__reporting` pointing
to the `analytics` database. Disable persistence for disposable test environments.

For an external database, use only `module.WithConnection("reporting")` and set
`ConnectionStrings:reporting` in the AppHost's secrets. Hosting projects that
reference Postgres without `WithPostgres` also use external mode. The connection
is projected as a secret parameter named `postgres-connection`; Aspire deployments
can supply `Parameters:postgres-connection` instead. Calling `WithConnection`
after `WithPostgres` switches back to external mode.

## Query behavior

`Query` accepts one SELECT or WITH ... SELECT, with an optional final semicolon.
The default limit is 200 rows; the maximum is 1,000. `Truncated` indicates more rows
were available. Each cell is JSON text; large or high-precision numbers remain
JSON strings to avoid precision loss. Text cells are capped at 4,000 characters.

Every operation executes inside a PostgreSQL read-only transaction with a
15-second statement timeout, a 3-second lock timeout and a 20-second client
deadline. Connection probes have a 3-second deadline. SQL restrictions improve
errors; database role permissions and read-only transactions enforce access.
Use a database role restricted to the intended data and functions.

`ReadSchema(new())` returns visible application tables and columns, excluding
`information_schema` and `pg_` schemas. A plain table name or schema-qualified name
narrows the result. Schema discovery is capped at 10,000 columns. Quoted identifiers
are supported in SQL queries, but not in the schema request's table-name field.

Query failures carry sanitized errors, including SQLSTATE when available, without
server details or credentials. `ReadConnection` returns `Connected = false` for
connection failures. There are no automatic retries.

Successful connection and schema reads report the database selected by the server,
including when the connection string omits its name. A failed connection probe
reports the configured database name, or an empty string if none was supplied.

## Tests

```text
dotnet test --project src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit/DigitalBrain.Modules.Postgres.Tests.Unit.csproj -p:CodeGraphRefresh=false
```

Tests exercise real Orleans neuron routing with a controlled database provider,
query validation, cell encoding, startup configuration and independent Postgres /
Supabase pool registration. Aspire model tests verify managed and external
connections, readiness dependencies, persistence and multiple consumers.

The live provider test runs when `DIGITALBRAIN_POSTGRES_TEST_CONNECTION` is set;
otherwise it is skipped. Point it at a disposable PostgreSQL database with permission
to create schemas, tables and functions. It creates and removes a uniquely named
schema, and verifies database metadata, row limits, typed cells and server-enforced
read-only transactions. Omit `Database` from that test connection to exercise the
server's default database selection.

## Assistant tables

The module registers `postgres_schema` and `show_postgres_query_table`. The latter
opens a window labelled **Postgres** and records its source as `postgres`.
`table_read` and `table_refine` preserve that source for paging, filters, sort and
aggregates. Explicit Postgres requests exclude Supabase schema/open tools and
reject reads or refinements of an existing Supabase window; unavailable sources
never fall back to another database.

The implementation reuses the existing live-table engine in the Supabase assembly
through `ILiveTableSource`. Its historical contract names remain compatible with
saved windows, but the Postgres adapter is constructed exclusively from the
Postgres module's keyed Npgsql pool. This does not configure a Supabase connection.
Hosts opening windows also include the Flutter workspace module.
