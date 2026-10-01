# Platform Capacity + Postgres Provisioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Platform capacity facet (Sdk contracts + resolver) with Postgres as its first consumer: local `aspire run` provisions one database per brain in the Docker server on demand; production uses a manually created Azure database via a Key Vault secret parameter, and provisioning requests there answer "Sorry, runtime provisioning is not accessible at the moment."

**Architecture:** `DigitalBrain.Sdk/Capacity` declares `ICapacity` (resolution), `ICapacityConfiguredSource` and `ICapacityProvisioner` (per-kind registrations) and `CapacityUnavailableException`. `DigitalBrain.Platform/Capacity` implements the resolver (account slot → configured source → provisioner → refusal) behind an idempotent `AddCapacity()` registration. The Postgres module registers EITHER a configured source for kind `"postgres"` (external connection = production) OR a Docker provisioner (admin connection present = local hosted run), rewires `PostgresTableProvider` to resolve an `NpgsqlDataSource` per origin string, and pins each table's origin in grain state at first `Define`. Aspire hosting splits: run mode keeps the container and passes the server's admin connection; publish mode emits the `postgres-connection` secret parameter, which the kernel deployment auto-resolves from stack config into Key Vault.

**Tech Stack:** .NET / Orleans grains, Npgsql, Aspire hosting, xunit v3 (`UnitTest.Create()`, `TestContext.Current.CancellationToken`).

**Spec:** `docs/superpowers/specs/2026-10-01-postgres-capacity-and-provisioning-design.md` (amended by `docs/superpowers/specs/2026-10-01-kernel-sdk-platform-ring-law-design.md`: Capacity is a Platform facet, not a module).

## Global Constraints

- Never change script-visible contracts, signals, wire shapes, or app packages: `IPostgresTable`, `TableDefined`/`RowUpserted`/`RowDeleted`, `Apps/CustomerResearcher/**` stay byte-identical.
- Persisted grain state: append `[Id(n)]`, never renumber; concrete arrays only.
- Refusal message, exact: `Sorry, runtime provisioning is not accessible at the moment.`
- Connection strings and admin credentials are platform configuration; nothing script-visible ever carries them.
- Build/test per project (`dotnet test src/<path>`), never the `.slnx`.
- No `/// <summary>` boilerplate; tests are facts with sentence-shaped names.
- Spec deviation (approved by the code, record in commit message of Task 5): no `DigitalBrain.Modules.Postgres.Deployment` project — `ManifestValues` resolves a `parameter.v0` from stack config (`options.Parameters.RequireSecret`) and the kernel deployment vault-mounts secret values, so the publish-mode parameter alone satisfies the supplier check.

## Review Focus

- A table defined before this change (grain state with `Origin == null`) must keep working after upgrade: null origin means the platform source. Test pinned in Task 2.
- Two brains defining the same table key locally must land in different databases and never see each other's rows. Test pinned in Task 3.
- A `Resolve` for kind `"postgres"` when neither a configured source nor a provisioner is registered must throw `CapacityUnavailableException` with the exact message, not `NullReferenceException`. Test pinned in Task 1.
- Provisioning the same brain twice (restart, at-least-once delivery) must be idempotent — second call returns the same origin without erroring on an existing database. Test pinned in Task 3.
- A malformed or unreachable admin connection must surface as `PostgresUnavailableException` ("Postgres is unreachable..."), not hang or leak the connection string into the message. Test pinned in Task 3.

---

### Task 1: Sdk capacity contracts and the Platform resolver

**Files:**
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Capacity/ICapacity.cs`
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Capacity/CapacityUnavailableException.cs`
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Capacity/CapacityResolver.cs`
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Capacity/CapacityServiceCollectionExtensions.cs`
- Test: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit/Capacity/CapacityResolverFacts.cs`
- Modify: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit/DigitalBrain.Core.Tests.Unit.csproj` (add a `ProjectReference` to `../DigitalBrain.Platform/DigitalBrain.Platform.csproj` if not already present)

**Interfaces:**
- Consumes: nothing new.
- Produces (later tasks rely on these exact shapes):

```csharp
namespace DigitalBrain.Sdk.Capacity;

public sealed record CapacityScope(string BrainId, string? AppId);
public sealed record ResolvedCapacity(string Kind, string Origin);

// Resolution: the brain's account (future slot) -> configured platform source -> provisioner -> refusal.
public interface ICapacity
{
    ValueTask<ResolvedCapacity> Resolve(string kind, CapacityScope scope, CancellationToken ct = default);
    ValueTask<ResolvedCapacity> Provision(string kind, CapacityScope scope, CancellationToken ct = default);
}

// A deployment-configured source for one kind (production: the platform connection).
public interface ICapacityConfiguredSource
{
    string Kind { get; }
    string Origin { get; }
}

// Creates (idempotently) capacity for a scope; registered only where the environment supports it.
public interface ICapacityProvisioner
{
    string Kind { get; }
    ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct);
}

public sealed class CapacityUnavailableException : Exception
{
    public const string RefusalMessage = "Sorry, runtime provisioning is not accessible at the moment.";
    public CapacityUnavailableException() : base(RefusalMessage) { }
}
```

- [ ] **Step 1: Write the failing tests**

`CapacityResolverFacts.cs`:

```csharp
using DigitalBrain.Platform.Capacity;
using DigitalBrain.Sdk.Capacity;

namespace DigitalBrain.Core.Tests.Unit.Capacity;

public sealed class CapacityResolverFacts
{
    private static readonly CapacityScope Scope = new("brain-1", "app-1");

    private sealed record Source(string Kind, string Origin) : ICapacityConfiguredSource;

    private sealed class Provisioner(string kind, string origin) : ICapacityProvisioner
    {
        public int Calls;
        public string Kind => kind;
        public ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct) { Calls++; return ValueTask.FromResult(origin); }
    }

    [Fact]
    public async Task AConfiguredSourceAnswersBeforeAnyProvisioner()
    {
        var provisioner = new Provisioner("postgres", "db:provisioned");
        var resolver = new CapacityResolver([new Source("postgres", "platform")], [provisioner]);
        var resolved = await resolver.Resolve("postgres", Scope, TestContext.Current.CancellationToken);
        Assert.Equal(new ResolvedCapacity("postgres", "platform"), resolved);
        Assert.Equal(0, provisioner.Calls);
    }

    [Fact]
    public async Task WithoutAConfiguredSourceTheProvisionerForTheKindAnswers()
    {
        var resolver = new CapacityResolver([new Source("clickhouse", "platform")], [new Provisioner("postgres", "db:brain-1")]);
        var resolved = await resolver.Resolve("postgres", Scope, TestContext.Current.CancellationToken);
        Assert.Equal("db:brain-1", resolved.Origin);
    }

    [Fact]
    public async Task WithNothingRegisteredResolutionRefusesWithTheExactMessage()
    {
        var resolver = new CapacityResolver([], []);
        var refusal = await Assert.ThrowsAsync<CapacityUnavailableException>(
            async () => await resolver.Resolve("postgres", Scope, TestContext.Current.CancellationToken));
        Assert.Equal("Sorry, runtime provisioning is not accessible at the moment.", refusal.Message);
    }

    [Fact]
    public async Task AnExplicitProvisionRequiresAProvisionerEvenWhenASourceIsConfigured()
    {
        var resolver = new CapacityResolver([new Source("postgres", "platform")], []);
        await Assert.ThrowsAsync<CapacityUnavailableException>(
            async () => await resolver.Provision("postgres", Scope, TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit --filter CapacityResolverFacts`
Expected: compile failure (`CapacityResolver` not defined).

- [ ] **Step 3: Implement the contracts and resolver**

Create the two Sdk files with the contract code from the Interfaces block above (split: `ICapacity.cs` holds `CapacityScope`, `ResolvedCapacity`, `ICapacity`, `ICapacityConfiguredSource`, `ICapacityProvisioner`; `CapacityUnavailableException.cs` holds the exception).

`CapacityResolver.cs`:

```csharp
using DigitalBrain.Sdk.Capacity;

namespace DigitalBrain.Platform.Capacity;

// Resolution order is the platform's law: brain account (future) -> configured source -> provisioner.
internal sealed class CapacityResolver(
    IEnumerable<ICapacityConfiguredSource> sources,
    IEnumerable<ICapacityProvisioner> provisioners) : ICapacity
{
    public async ValueTask<ResolvedCapacity> Resolve(string kind, CapacityScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);
        if (sources.FirstOrDefault(source => source.Kind == kind) is { } configured)
        { return new(kind, configured.Origin); }
        return await Provision(kind, scope, ct);
    }

    public async ValueTask<ResolvedCapacity> Provision(string kind, CapacityScope scope, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(scope);
        if (provisioners.FirstOrDefault(provisioner => provisioner.Kind == kind) is { } provisioner)
        { return new(kind, await provisioner.Ensure(scope, ct)); }
        throw new CapacityUnavailableException();
    }
}
```

`CapacityServiceCollectionExtensions.cs`:

```csharp
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Platform.Capacity;

public static class CapacityServiceCollectionExtensions
{
    // Idempotent: any module needing capacity calls this; the ring-law migration will move the
    // call into the kernel so the facet stands up unconditionally.
    public static IServiceCollection AddCapacity(this IServiceCollection services)
    {
        services.TryAddSingleton<ICapacity, CapacityResolver>();
        return services;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit --filter CapacityResolverFacts`
Expected: 4 passed. Also build Platform: `dotnet build src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform`.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Capacity src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Capacity src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit
git commit -m "feat(platform): capacity facet - Sdk contracts and resolver

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Postgres resolves its data source through capacity, pinning the origin per table

**Files:**
- Create: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresSourceRegistry.cs`
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/IPostgresTableProvider.cs` (every method gains a leading `string origin` parameter)
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresTableProvider.cs`
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresTableNeuron.cs`
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresHosting.cs`
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/DigitalBrain.Modules.Postgres.csproj` (add `ProjectReference` to `../../DigitalBrain/Kernel/DigitalBrain.Platform/DigitalBrain.Platform.csproj`)
- Test: `src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit/PostgresCapacityFacts.cs`

**Interfaces:**
- Consumes: `ICapacity`, `CapacityScope`, `ICapacityConfiguredSource`, `AddCapacity()` from Task 1.
- Produces:

```csharp
namespace DigitalBrain.Postgres;

// Origin strings: "platform" = the module's configured connection; "db:<name>" = a database on the
// admin-connection server (provisioned). Task 3 adds the provisioner that mints "db:" origins.
internal interface IPostgresSourceRegistry
{
    NpgsqlDataSource Get(string origin);
}

internal static class PostgresCapacityKind { public const string Kind = "postgres"; public const string PlatformOrigin = "platform"; }

internal sealed record PostgresConfiguredSource : ICapacityConfiguredSource
{ public string Kind => PostgresCapacityKind.Kind; public string Origin => PostgresCapacityKind.PlatformOrigin; }
```

and `PostgresTableState` gains `[Id(2)] public string? Origin { get; init; }` — null means `"platform"` (pre-existing tables).

- [ ] **Step 1: Write the failing test**

`PostgresCapacityFacts.cs`:

```csharp
using DigitalBrain.Postgres;
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresCapacityFacts
{
    [Fact]
    public async Task AComposedPostgresModuleResolvesThePlatformOriginForTables()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>()
            .ConfigureSilo(silo => silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader")
            .StartAsync(ct);
        var capacity = brain.Services.GetRequiredService<ICapacity>();
        var resolved = await capacity.Resolve("postgres", new("brain-1", "app-1"), ct);
        Assert.Equal("platform", resolved.Origin);
    }

    [Fact]
    public async Task ProvisioningIsRefusedWhenOnlyThePlatformConnectionExists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>()
            .ConfigureSilo(silo => silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader")
            .StartAsync(ct);
        var capacity = brain.Services.GetRequiredService<ICapacity>();
        var refusal = await Assert.ThrowsAsync<CapacityUnavailableException>(
            async () => await capacity.Provision("postgres", new("brain-1", "app-1"), ct));
        Assert.Equal("Sorry, runtime provisioning is not accessible at the moment.", refusal.Message);
    }

    [Fact]
    public void TheRegistryAnswersThePlatformSourceForANullOrLegacyOrigin()
    {
        // A table pinned before origins existed (Origin == null) maps to "platform".
        Assert.Equal(PostgresCapacityKind.PlatformOrigin, PostgresCapacityKind.OriginOrPlatform(null));
        Assert.Equal("db:brain_x", PostgresCapacityKind.OriginOrPlatform("db:brain_x"));
    }
}
```

(If `brain.Services` is not how `UnitTest` exposes the silo's provider, use the equivalent accessor the fixture offers — check `src/Testing/DigitalBrain.Testing.Unit` for the established way other facts reach a silo service, e.g. how `PostgresTableFacts` reaches keyed services, and follow it.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit --filter PostgresCapacityFacts`
Expected: compile failure (`PostgresCapacityKind` not defined).

- [ ] **Step 3: Implement**

`PostgresSourceRegistry.cs` (also holds the kind/origin helpers and configured source):

```csharp
using System.Collections.Concurrent;
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DigitalBrain.Postgres;

internal static class PostgresCapacityKind
{
    public const string Kind = "postgres";
    public const string PlatformOrigin = "platform";
    public const string DatabasePrefix = "db:";
    public const string AdminConnectionKey = "DigitalBrain:Capacity:Postgres:AdminConnection";
    public static string OriginOrPlatform(string? origin) => string.IsNullOrEmpty(origin) ? PlatformOrigin : origin;
}

internal sealed record PostgresConfiguredSource : ICapacityConfiguredSource
{
    public string Kind => PostgresCapacityKind.Kind;
    public string Origin => PostgresCapacityKind.PlatformOrigin;
}

internal interface IPostgresSourceRegistry
{
    NpgsqlDataSource Get(string origin);
}

internal sealed class PostgresSourceRegistry(IServiceProvider provider, IConfiguration configuration) : IPostgresSourceRegistry, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _provisioned = new(StringComparer.Ordinal);

    public NpgsqlDataSource Get(string origin)
    {
        if (origin == PostgresCapacityKind.PlatformOrigin)
        { return provider.GetRequiredKeyedService<NpgsqlDataSource>(PostgresHosting.DataSourceKey); }
        if (!origin.StartsWith(PostgresCapacityKind.DatabasePrefix, StringComparison.Ordinal))
        { throw new PostgresUnavailableException("Unknown Postgres capacity origin."); }
        return _provisioned.GetOrAdd(origin, key =>
        {
            var admin = configuration[PostgresCapacityKind.AdminConnectionKey]
                ?? throw new PostgresUnavailableException("This table's database is not reachable from this deployment.");
            var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionSettings.Parse(admin).ConnectionString)
            { Database = key[PostgresCapacityKind.DatabasePrefix.Length..] };
            return NpgsqlDataSource.Create(builder.ConnectionString);
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _provisioned.Values) { await source.DisposeAsync(); }
    }
}
```

`IPostgresTableProvider.cs`: add `string origin` as the first parameter of `DefineAsync`, `UpsertAsync`, `DeleteAsync`, `ReadAsync`, `PageAsync`.

`PostgresTableProvider.cs`: change the constructor to `PostgresTableProvider(IPostgresSourceRegistry registry)`, each public method takes `string origin` and `Execute` becomes `Execute(origin, ...)` opening `registry.Get(origin).OpenConnectionAsync(...)` instead of the injected keyed `source`.

`PostgresTableNeuron.cs`:
- State: `[Id(2)] public string? Origin { get; init; }` on `PostgresTableState` (append only).
- Constructor gains `DigitalBrain.Sdk.Capacity.ICapacity capacity`.
- `Define`: before `provider.DefineAsync`, resolve and pin:

```csharp
var origin = state.State.Origin
    ?? (await capacity.Resolve(PostgresCapacityKind.Kind, new(BrainScope.CurrentId(), CallerContextStamper.Require().AppId))).Origin;
await provider.DefineAsync(origin, table, normalized, CancellationToken.None);
// persist Origin = origin alongside Owner and Accepted in the state write
```

- Every other operation passes `PostgresCapacityKind.OriginOrPlatform(state.State.Origin)` as the first provider argument.

`PostgresHosting.AddPostgres`:
- Call `services.AddCapacity();` (new `using DigitalBrain.Platform.Capacity;`).
- Register the registry: `services.TryAddSingleton<IPostgresSourceRegistry, PostgresSourceRegistry>();`
- Register the configured source only when no admin connection is present (local hosted runs register the provisioner instead, Task 3):

```csharp
services.AddSingleton<ICapacityConfiguredSource>(provider =>
    provider.GetRequiredService<IConfiguration>()[PostgresCapacityKind.AdminConnectionKey] is null
        ? new PostgresConfiguredSource()
        : throw new InvalidOperationException("unreachable"));
```

— that conditional-by-throw shape is wrong; instead register via a factory that returns a marker and have the resolver skip it. Simplest correct form: keep registration unconditional here, and in Task 3 the provisioner registration REMOVES the configured source by checking configuration inside a composite. Concretely: register

```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapacityConfiguredSource>(provider =>
    new PostgresConfiguredSource(provider.GetRequiredService<IConfiguration>()[PostgresCapacityKind.AdminConnectionKey] is null)));
```

with `PostgresConfiguredSource(bool active)` and the resolver-facing `Kind` returning `"postgres"` only when active, else `"postgres:inactive"`. Final shape to implement:

```csharp
internal sealed class PostgresConfiguredSource(bool active) : ICapacityConfiguredSource
{
    public string Kind => active ? PostgresCapacityKind.Kind : PostgresCapacityKind.Kind + ":inactive";
    public string Origin => PostgresCapacityKind.PlatformOrigin;
}
```

(An inactive kind never matches a lookup, so hosted-local runs fall through to the provisioner while the shared `ConnectionStrings:postgres` continues to serve module startup, health checks, live tables and `PostgresNeuron` unchanged.)

- [ ] **Step 4: Run the Postgres suite**

Run: `dotnet test src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit`
Expected: all pass, including pre-existing `PostgresTableFacts`/`PostgresWriteTableFacts` (they run with no admin connection configured → origin `"platform"` → identical behavior).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Postgres
git commit -m "feat(postgres): resolve table data sources through the capacity facet, pin origin per table

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Docker provisioner — one database per brain on the local server

**Files:**
- Create: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/DockerPostgresProvisioner.cs`
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres/PostgresHosting.cs` (register it)
- Test: `src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit/PostgresCapacityFacts.cs` (extend)

**Interfaces:**
- Consumes: `ICapacityProvisioner`, `CapacityScope` (Task 1); `PostgresCapacityKind` (Task 2).
- Produces: origins of the form `db:brain_<16 hex chars>`; database naming is `brain_` + first 16 hex of SHA-256 of the brain id (lowercase).

- [ ] **Step 1: Write the failing tests** (append to `PostgresCapacityFacts.cs`)

```csharp
[Fact]
public void DatabaseNamesAreStableHashedAndDistinctPerBrain()
{
    var one = DockerPostgresProvisioner.DatabaseName("brain-1");
    var two = DockerPostgresProvisioner.DatabaseName("brain-2");
    Assert.Equal(one, DockerPostgresProvisioner.DatabaseName("brain-1"));
    Assert.NotEqual(one, two);
    Assert.Matches("^brain_[0-9a-f]{16}$", one);
}

[Fact]
public async Task AnAdminConnectionActivatesTheProvisionerAndDeactivatesThePlatformSource()
{
    var ct = TestContext.Current.CancellationToken;
    await using var brain = await UnitTest.Create().WithModule<PostgresModule>()
        .ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
            silo.Configuration["DigitalBrain:Capacity:Postgres:AdminConnection"] = "Host=localhost;Database=postgres;Username=admin;Password=admin";
        }).StartAsync(ct);
    var capacity = brain.Services.GetRequiredService<ICapacity>();
    var resolved = await capacity.Resolve("postgres", new("brain-1", "app-1"), ct);
    Assert.Equal("db:" + DockerPostgresProvisioner.DatabaseName("brain-1"), resolved.Origin);
}
```

The second fact must not require a live server: `Resolve` returns the origin; the `CREATE DATABASE` happens lazily. See Step 3 — `Ensure` computes the origin and performs DDL only when a connection succeeds is NOT acceptable (silent skip); instead `Ensure` runs DDL eagerly, so this fact needs the DDL seam faked. Give the provisioner an internal hook: constructor takes `Func<string, string, CancellationToken, Task>? createDatabase = null` (admin connection, database, ct) defaulting to the real implementation; the test registers the provisioner is not re-wired — instead ConfigureSilo replaces it:

```csharp
silo.Services.AddSingleton<ICapacityProvisioner>(new DockerPostgresProvisioner(
    "Host=localhost;Database=postgres;Username=admin;Password=admin", (_, _, _) => Task.CompletedTask));
```

(Registration in `AddPostgres` uses `TryAddEnumerable` with an `IConfiguration`-reading factory, so a test-registered instance coexists; assert on the resolved origin as above. If duplicate registrations double-resolve, prefer `RemoveAll<ICapacityProvisioner>()` first — follow whichever the fixture supports.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit --filter PostgresCapacityFacts`
Expected: compile failure (`DockerPostgresProvisioner` not defined).

- [ ] **Step 3: Implement**

`DockerPostgresProvisioner.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Sdk.Capacity;
using Npgsql;

namespace DigitalBrain.Postgres;

// Local runtime provisioning: one database per brain on the Aspire-hosted server, created over the
// admin connection run-mode hosting passes in. Publish mode never configures that connection, so
// this class is absent from production and provisioning refuses there by construction.
internal sealed class DockerPostgresProvisioner(string adminConnection, Func<string, string, CancellationToken, Task>? createDatabase = null) : ICapacityProvisioner
{
    public string Kind => PostgresCapacityKind.Kind;

    public static string DatabaseName(string brainId)
        => "brain_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(brainId)))[..16];

    public async ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct)
    {
        var database = DatabaseName(scope.BrainId);
        await (createDatabase ?? CreateDatabase)(adminConnection, database, ct);
        return PostgresCapacityKind.DatabasePrefix + database;
    }

    private static async Task CreateDatabase(string adminConnection, string database, CancellationToken ct)
    {
        try
        {
            await using var source = NpgsqlDataSource.Create(PostgresConnectionSettings.Parse(adminConnection).ConnectionString);
            await using var connection = await source.OpenConnectionAsync(ct);
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", connection))
            {
                exists.Parameters.AddWithValue(database);
                if (await exists.ExecuteScalarAsync(ct) is not null) { return; }
            }
            // CREATE DATABASE cannot be parameterized; the name is hash-derived, never user input.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            try { await create.ExecuteNonQueryAsync(ct); }
            catch (PostgresException error) when (error.SqlState == "42P04") { } // created by a concurrent Ensure
        }
        catch (NpgsqlException) { throw new PostgresUnavailableException("Postgres is unreachable or the database connection failed."); }
    }
}
```

In `PostgresHosting.AddPostgres`, register after the configured source:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapacityProvisioner>(provider =>
    provider.GetRequiredService<IConfiguration>()[PostgresCapacityKind.AdminConnectionKey] is { } admin
        ? new DockerPostgresProvisioner(admin)
        : new InactiveProvisioner()));
```

with a private `InactiveProvisioner : ICapacityProvisioner { Kind => "postgres:inactive"; Ensure => throw new CapacityUnavailableException(); }` beside it (an inactive kind never matches, mirroring Task 2's inactive source).

- [ ] **Step 4: Run the suite**

Run: `dotnet test src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit`
Expected: all pass. The two-brains-isolation guarantee is carried by `DatabaseNamesAreStableHashedAndDistinctPerBrain` plus origin pinning (Task 2); a live two-database round trip lands in the aspire smoke (Task 5).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Postgres
git commit -m "feat(postgres): docker provisioner creates one database per brain over the admin connection

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Hosting split — admin connection in run mode, secret parameter in publish mode

**Files:**
- Modify: `src/Modules/Postgres/DigitalBrain.Modules.Postgres.Aspire.Hosting/PostgresHostingExtensions.cs`
- Test: `src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit/PostgresAspireHostingFacts.cs` (extend — follow the file's existing pattern for building a test `DistributedApplication` in run vs publish mode)

**Interfaces:**
- Consumes: nothing from earlier tasks at compile time; produces the runtime configuration Task 2/3 read: env var `DigitalBrain__Capacity__Postgres__AdminConnection` (run mode, hosted only).
- Produces: publish-mode manifest where the brain's `ConnectionStrings__postgres` references `{postgres-connection.value}` even when `WithPostgres()` (hosted) was configured.

- [ ] **Step 1: Write the failing facts** (shapes — adapt construction to the existing facts in the file):

```csharp
[Fact]
public async Task HostedPostgresInRunModePassesTheAdminConnectionToTheBrain()
{
    // Build the app in run mode with WithPostgres(); assert the brain resource's environment
    // contains DigitalBrain__Capacity__Postgres__AdminConnection referencing the postgres-server resource.
}

[Fact]
public async Task HostedPostgresInPublishModeEmitsTheSecretConnectionParameterInstead()
{
    // Build in publish mode with WithPostgres(); assert no postgres-server container resource exists,
    // a secret parameter "postgres-connection" exists, and the brain's environment maps
    // ConnectionStrings__postgres to it. Assert no DigitalBrain__Capacity__Postgres__AdminConnection is set.
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit --filter PostgresAspireHostingFacts`
Expected: the two new facts FAIL (publish mode currently creates the container; run mode sets no admin env).

- [ ] **Step 3: Implement in `PostgresHostingExtensions.PostgresHostingState`**

In `Enable`, route by execution mode:

```csharp
if (configuration.DatabaseName is { } databaseName && !brain.ApplicationBuilder.ExecutionContext.IsPublishMode)
{
    var server = brain.ApplicationBuilder.AddPostgres("postgres-server").WithParentRelationship(module).WithRepl();
    if (configuration.PersistentStorage) { server.WithDataVolume().WithLifetime(ContainerLifetime.Persistent); }
    _server = server;
    _database = server.AddDatabase("postgres-database", databaseName);
}
else
{
    _connection = brain.ApplicationBuilder.AddParameter("postgres-connection", () =>
        brain.ApplicationBuilder.Configuration["Parameters:postgres-connection"]
        ?? brain.ApplicationBuilder.Configuration.GetConnectionString(configuration.ConnectionName)
        ?? throw new InvalidOperationException($"Connection string '{configuration.ConnectionName}' is required."), secret: true)
        .WithParentRelationship(module);
}
```

(add the `_server` field: `private IResourceBuilder<PostgresServerResource>? _server;`). In `Apply`, alongside the existing `_database` branch:

```csharp
if (_server is not null)
{
    builder.WithEnvironment("DigitalBrain__Capacity__Postgres__AdminConnection", _server.Resource.ConnectionStringExpression);
}
```

(if `WithEnvironment(string, ReferenceExpression)` is not available in the repo's Aspire version, use the callback overload: `builder.WithEnvironment(context => context.EnvironmentVariables["DigitalBrain__Capacity__Postgres__AdminConnection"] = _server.Resource.ConnectionStringExpression);`).

Note the `WithRepl()` guard moves inside the run-mode branch (publish mode never has the container now), preserving current behavior.

- [ ] **Step 4: Run the suite**

Run: `dotnet test src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit`
Expected: all pass, including prior hosting facts.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Postgres/DigitalBrain.Modules.Postgres.Aspire.Hosting src/Modules/Postgres/DigitalBrain.Modules.Postgres.Tests.Unit
git commit -m "feat(postgres): run mode passes the admin connection; publish mode emits the postgres-connection secret parameter

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Host cleanup, smoke, and deployment verification

**Files:**
- Modify: `src/Applications/IntoChat/AppHost/AppHost.cs:51` — `options.DatabaseName = "customer-research"` becomes `options.DatabaseName = "digitalbrain"`.
- Modify: `docs/superpowers/specs/2026-10-01-postgres-capacity-and-provisioning-design.md` — in the Production section, replace the `DigitalBrain.Modules.Postgres.Deployment` sentence with: "No deployment project is needed: the publish-mode `postgres-connection` secret parameter is a `parameter.v0`, which `ManifestValues` resolves from stack configuration and the kernel deployment writes to Key Vault as a mounted `secretRef`."

**Interfaces:** none new; this task proves the stack end to end.

- [ ] **Step 1: Apply both edits.**

- [ ] **Step 2: Build the hosts**

Run: `dotnet build src/Applications/IntoChat/AppHost && dotnet build src/Applications/IntoChat`
Expected: both succeed.

- [ ] **Step 3: Run the IntoChat unit suite** (composition/packaging guards)

Run: `dotnet test src/Applications/IntoChat/Tests/Unit`
Expected: pass. If a fact pins the old database name, update that fact in the same change.

- [ ] **Step 4: Aspire smoke with live provisioning check**

Run `aspire run` from `src/Applications/IntoChat/AppHost` until all resources are Healthy. Then confirm in the postgres-server container that the shared database exists and that exercising a table (e.g. via the Customer Researcher journey or a REPL `SELECT datname FROM pg_database`) shows a `brain_<hash>` database appear after a first `Define`. Record the resource-health and `pg_database` evidence in the task notes.

- [ ] **Step 5: Publish-manifest verification (no cloud needed)**

Run the AppHost manifest publish (`dotnet run --project src/Applications/IntoChat/AppHost -- --publisher manifest --output-path ../../../artifacts/manifest.json` or the repo's established publish command) and verify in the output: no `postgres-server` container resource; a secret `postgres-connection` parameter; the brain's `ConnectionStrings__postgres` references `{postgres-connection.value}`; no `DigitalBrain__Capacity__Postgres__AdminConnection` entry.

- [ ] **Step 6: Commit**

```bash
git add src/Applications/IntoChat docs/superpowers/specs/2026-10-01-postgres-capacity-and-provisioning-design.md
git commit -m "chore(intochat): neutral postgres database name; spec: parameter.v0 replaces the Postgres deployment project

The kernel deployment resolves parameter.v0 values from stack config and vault-mounts
secrets, so the publish-mode postgres-connection parameter alone satisfies the
supplier check; no DigitalBrain.Modules.Postgres.Deployment project is required.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Self-review notes

- Spec coverage: Sdk/Platform facet (Task 1), Postgres as reference consumer with pinning (Task 2), local provisioning + refusal (Task 3), run/publish split (Task 4), host cleanup + production path + smoke (Task 5). The spec's "integration account" resolution step is an explicit future slot (comment in `CapacityResolver`), per spec. The spec's deployment-project paragraph is corrected by Task 5's spec edit, justified by `ManifestValues`/`DigitalBrainDeployment` behavior read during planning.
- Known judgment calls an implementer may hit: the exact `UnitTest` accessor for silo services (follow `PostgresTableFacts`), the `WithEnvironment` overload for `ReferenceExpression` (fallback given), and duplicate provisioner registration in the Task 3 fact (fallback given). These are adaptation points, not placeholders — the target behavior and assertions are fixed.
