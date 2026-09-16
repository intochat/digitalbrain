# Supabase module

Connect an existing Supabase PostgreSQL database with one AppHost registration:

```csharp
using DigitalBrain.Supabase;

var brain = builder.AddDigitalBrain("brain")
    .AddModule<SupabaseModule>();

builder.AddProject<Projects.DigitalBrain_Silo>("kernel").WithReference(brain);
```

Reference `DigitalBrain.Modules.Supabase.Aspire.Hosting` from the AppHost and
`DigitalBrain.Modules.Supabase` from the silo. These references and registration
are already included in this repository's AppHost.

On startup, Aspire shows an unresolved secret connection-string parameter named
`supabase`. Enter its value through the dashboard. If `ConnectionStrings:supabase`
is already configured in AppHost user secrets or environment configuration,
Aspire reuses it without prompting. Do not put credentials in source control or chat.

Accepted formats:

```text
postgresql://reader.project:percent-encoded-password@pooler-host:5432/postgres?sslmode=require
Host=pooler-host;Port=5432;Database=postgres;Username=reader.project;Password=...;SSL Mode=Require
```

Copy the actual host and username from the Supabase Connect dialog. For this
persistent backend use a direct connection when reachable, or the session pooler
on IPv4-only networks. URI credentials must be percent-encoded. This module uses
the database password, not a Supabase API key.

## Read-only behavior

`ISupabase` exposes `Query`, `ReadSchema`, and `ReadConnection`. The default neuron
is `supabase/default`. Schema discovery lists accessible tables/views across application
schemas, including `public` and custom schemas such as `demo`; it does not advertise
Supabase internal or PostgreSQL system schemas. Query access is governed by the supplied database role.
Omit the table argument for the index, or pass `demo.companies` or
`postgres.demo.companies` (the database qualifier must match the connected database).
A bare table name returns matches across application schemas. Returned names are
schema-qualified and quoted when needed; use them directly in SQL.

Every database operation uses a PostgreSQL read-only transaction, a 15-second
statement timeout, and a 3-second lock timeout. Queries accept a single SELECT or
WITH/SELECT, default to 200 rows, and cap at 1000 rows. Comments, semicolons,
escape/dollar-quoted strings, write statements, and session-control operations
are refused. There are no database write methods or automatic migrations.

Use a dedicated role with SELECT access only to the intended tables and with
restricted function execution privileges. Read-only transactions are not a
sandbox for arbitrary database extensions or externally side-effecting functions.
Direct SQL runs with this database role; it does not inherit a Supabase end-user
JWT or automatically impersonate that user's RLS context.

The module registers `supabase_schema`, `supabase_query`, and
`show_supabase_query_table` for agents. Live query tables use the `sbtable-` prefix
and existing table filtering, sorting, paging, and chart tools. Changing a table
view persists only view settings in DigitalBrain. It never updates database rows.
Page rows and counts are separate live reads and may differ during concurrent
database changes. Set a table view sort to select its ordering.

Numbers outside the UI's exact decimal range are returned as text. Structured
and extension types use PostgreSQL's text representation, matching text filters.
Individual text cells are capped at 4000 characters. Server errors expose a
SQLSTATE code rather than server detail or connection credentials.

## Fakes and tests

`.WithDigitalBrainFakes()` before consumers are attached skips the parameter,
whether called before or after `.AddModule<SupabaseModule>()`. The runtime uses
the fake only in explicit fake/testing mode; missing production configuration
fails instead of silently returning fake results. The fake supports exact
scripted query results and includes `SELECT 1 AS value` as a default example.

The test suite covers hosting parameters, fake composition, query restrictions,
connection formats, and agent/live-table behavior. Set
`DIGITALBRAIN_SUPABASE_TESTS=1` to also run the disposable PostgreSQL integration
tests (Docker required). These test the SQL provider, not hosted Supabase services.

## Integration choice

This module uses Npgsql and Aspire's built-in connection-string parameters.
`Nextended.Aspire.Hosting.Supabase` is useful when hosting a full local Supabase
stack; it is not needed to connect to an existing database. Auth, Storage,
Realtime, local stack orchestration, and write operations are outside this version.

References: [Supabase connections](https://supabase.com/docs/guides/database/connecting-to-postgres),
[Aspire parameters](https://aspire.dev/fundamentals/external-parameters/),
[Npgsql](https://www.npgsql.org/doc/basic-usage.html),
[Nextended integration](https://www.nuget.org/packages/Nextended.Aspire.Hosting.Supabase).
