# ino Prod Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a 60-second Telegram-miniapp demo that hits every pillar of the ino vision (instant open, persona-top layout, Google/Uber OAuth cascade, L1 self-evolve) backed by a production-shaped codebase restructure that integrates TripRadar as the user/auth/billing backbone.

**Architecture:** Six-facet Neuron runtime (`SynapseSchema`, `ScriptSource`, `ToolRefs`, `ModelHints`, `FeatureSchema`, `RfwTemplateSource`) sits on top of TripRadar's DDD user-management backbone. Identity is free via Telegram `initData`; OAuth scopes are granted per-skill on demand through a shared `AuthRequestCard` RFW template. Per-user OAuth tokens live in a new encrypted `UserOAuthTokens` table in `TripRadar.Server.Db`. The whole ecosystem runs under a single unified Aspire AppHost.

**Tech Stack:** .NET 11 + Orleans + Aspire + EF Core + Roslyn Scripting + PostgreSQL + Redis + Flutter web (CanvasKit) + Rive 0.14.5 + RFW 1.1.3 + gRPC + Google OAuth 2.0 with PKCE + cloudflared tunnel.

**Spec:** `docs/superpowers/specs/2026-04-12-ino-prod-demo-design.md` — read it first. This plan operationalizes that spec task-by-task.

---

## ⚠ Worktree recommendation

The restructure phase moves ~140 files. Execute this plan in a dedicated git worktree:

```bash
git worktree add ../ino-prod-demo -b prod-demo-2026-04-12
cd ../ino-prod-demo
```

If you prefer to run on master, ensure `git status` is clean before starting (the untracked `deployment/Synapse/` folder is the Aspire deployment reference template — it's fine to leave untracked or `.gitignore` it first).

## Reading order for zero-context engineers

If you've never seen this codebase:
1. Read `CLAUDE.md` at the repo root — vision + primitives + working instructions
2. Read the spec linked above — the "what" and "why"
3. Read `domains/travel/TripRadar/CLAUDE.md` — TripRadar's own rules; you'll touch TripRadar in Phases 1 and 3
4. Skim `features/ino-new/InoNew.Core/NeuronGrain.cs` (255 LOC) — the current neuron runtime you're extending in Phase 2
5. Skim `iaw/Telegram/Services/InoService.cs` — the current gRPC surface you're moving in Phase 3

## Verification pattern used throughout

Every task follows this loop:
1. **Write the failing test first** (exact code shown)
2. **Run it and watch it fail** with the expected message
3. **Write the minimal implementation** (exact code shown, or a diff against existing file)
4. **Run the test and watch it pass**
5. **Run the full suite for the affected project** (`dotnet test <project>`)
6. **Commit with a conventional-commit message**

End-of-phase checkpoints also require `dotnet build ino.slnx` + `aspire start` + "every resource Healthy in the Aspire dashboard" before moving on.

## Context7 usage (non-negotiable per repo CLAUDE.md)

Before writing code that touches an external library, resolve the library ID and query docs:

```
mcp__context7__resolve-library-id → mcp__context7__query-docs
```

Libraries you'll touch: `orleans` (grains, IPersistentState, streams), `aspire` (parameters, hosting, `AddProject`, `WithReference`), `ef-core` (value converters, migrations, IDataProtection), `google.apis.auth` (OAuth 2.0, PKCE S256), `rive-flutter` (state machine inputs, asset loading), `rfw` (library parsing, DynamicContent), `grpc.aspnetcore` (Kestrel dual listener), `microsoft.extensions.ai` (IChatClient), and Telegram Bot API (initData HMAC).

Every task that writes new code has a Context7 query reminder in Step 1.

---

# Phase 0 — Preparation

**Goal:** Everything non-blocking and parallelizable before the main plan starts. No code changes to the ino repo yet.

**Duration:** ~half day, can overlap with Phase 1 / 2.

## Task 0.1: Google Cloud OAuth client setup

**Files:** none in repo.

- [ ] **Step 1:** Create a new Google Cloud project (or reuse a dev one). Enable **Google Calendar API** and **Gmail API** in the API Library.
- [ ] **Step 2:** Go to APIs & Services → OAuth consent screen. Choose **External**, **Testing** mode. Add the demo user email as a test user. Fill the app name as `ino (dev)`.
- [ ] **Step 3:** Credentials → Create Credentials → OAuth client ID → **Web application**. Authorized redirect URIs: the cloudflared tunnel URL from TripRadar's AppHost dashboard + `/api/auth/google/callback`. (You'll add the real URL in Phase 4; for now use a placeholder you can update.)
- [ ] **Step 4:** Copy the client ID and client secret to a secure note. You'll set them as Aspire user-secrets in Phase 4.
- [ ] **Step 5:** Verify the consent screen shows `calendar.readonly` and `gmail.readonly` scopes as test-user-scoped (unverified).

**No commit** — this is external state.

## Task 0.2: Rive persona asset

**Files:** none in repo yet; you'll drop the file in Phase 5.

- [ ] **Step 1:** Browse Rive Community for an orb/morph animation matching the existing persona colors (blue-violet `#6C63FF` baseline, see `ino.flutter/lib/persona/persona_widget.dart:181-196` for the emotion→color map in the fallback renderer).
- [ ] **Step 2:** Fork the chosen file in Rive editor. Add a state machine named `persona` with inputs: `emotion` (enum with values `sleeping, idle, thinking, presenting, evolving, celebrating, confused`), `pulse` (trigger), `energy` (number 0..1).
- [ ] **Step 3:** Create 7 states, one per emotion value, with cross-fade transitions (250ms) bound to the `emotion` input.
- [ ] **Step 4:** Bind the `pulse` trigger to a ripple overlay that fires regardless of the current state.
- [ ] **Step 5:** Export as `.riv`. Save locally — you'll commit it in Task 5.9 at `clients/ino.flutter/assets/rive/ino_persona.riv`.

**Fallback if no designer bandwidth:** the existing `_PersonaPainter` CustomPaint body stays as the emergency renderer. Phase 5 will still ship the Rive integration code behind a feature flag; the demo can go live with the CustomPaint persona.

## Task 0.3: Context7 library lookups

**Files:** none in repo; capture a scratch note.

- [ ] **Step 1:** Resolve each library ID:

```
mcp__context7__resolve-library-id → orleans
mcp__context7__resolve-library-id → aspire
mcp__context7__resolve-library-id → ef-core
mcp__context7__resolve-library-id → google.apis.auth
mcp__context7__resolve-library-id → rive-flutter
mcp__context7__resolve-library-id → rfw
mcp__context7__resolve-library-id → microsoft.extensions.ai
```

- [ ] **Step 2:** Query each one for the topic you need:
  - `orleans` — "IPersistentState encryption, custom IGrainStorage, stream consumer patterns"
  - `aspire` — "AddParameter secret, AddProject WithReference, Kestrel dual listener configuration"
  - `ef-core` — "ValueConverter for encrypted column, IDataProtection integration, migration with Schema"
  - `google.apis.auth` — "OAuth 2.0 authorization code flow with PKCE S256, token exchange with code_verifier"
  - `rive-flutter` — "RiveAnimation.asset, StateMachineController, SMIEnum + SMITrigger inputs"
  - `rfw` — "parseLibraryFile, LibraryName, DynamicContent, FullyQualifiedWidgetName, event handlers"
  - `microsoft.extensions.ai` — "IChatClient GetResponseAsync, ChatMessage, tool-calling"

- [ ] **Step 3:** Save the relevant API signatures in a scratch note. You'll reference them in Phase 1/2/4/5 tasks.

## Task 0.4: Telegram test bot registration

**Files:** none in repo.

- [ ] **Step 1:** Talk to `@BotFather` on Telegram. `/newbot` → pick a name like `ino_dev_bot` → get the bot token.
- [ ] **Step 2:** `/setdomain` on your bot → set to your cloudflared tunnel hostname (you'll update this once TripRadar AppHost is running — Phase 3).
- [ ] **Step 3:** `/setmenubutton` → set menu button text to `Open ino` and URL to the cloudflared tunnel root (`/` — the Flutter miniapp will be at the root).
- [ ] **Step 4:** Copy the bot token. You'll set it as an Aspire user-secret in Phase 3.

---

# Phase 1 — TripRadar extensions

**Goal:** Add encrypted per-user OAuth token storage to TripRadar's existing DDD backbone. All changes live under `domains/travel/TripRadar/src/` and are independently testable via TripRadar's own `dotnet test`.

**Duration:** ~1 day, parallelizable with Phase 2.

**Context:** Read `domains/travel/TripRadar/CLAUDE.md` first. Commits in this phase go to TripRadar's history — follow its commit style (no default `///` summaries, self-explanatory naming, build-then-test verification via its Aspire MCP).

## Task 1.1: Add `UserOAuthTokens` EF model

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Server.Db/Models/UserOAuthTokens.cs`
- Modify: `domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadarDbContext.cs` — add `DbSet<UserOAuthTokens>`

- [ ] **Step 1: Context7 query** — `ef-core` → "[Index] composite unique + ForeignKey attribute + IDataProtection value converter example"

- [ ] **Step 2: Write the model file**

```csharp
// domains/travel/TripRadar/src/TripRadar.Server.Db/Models/UserOAuthTokens.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UserId), nameof(Service), IsUnique = true)]
[Table("UserOAuthTokens", Schema = DbConstants.SchemaName)]
public class UserOAuthTokens
{
    [Key]
    public long Id { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.BigInt)]
    public long UserId { get; set; }

    [Required]
    [Column(TypeName = "varchar(32)")]
    public string Service { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string AccessToken { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? RefreshToken { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string Scopes { get; set; } = string.Empty;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime ExpiresAt { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? UpdatedOn { get; set; }

    [ForeignKey("UserId")]
    public Users User { get; set; } = null!;
}
```

- [ ] **Step 3: Wire into the DbContext**

Find `TripRadarDbContext.cs` and add:

```csharp
public DbSet<UserOAuthTokens> UserOAuthTokens => Set<UserOAuthTokens>();
```

- [ ] **Step 4: Build**

```bash
dotnet build domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadar.Server.Db.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add domains/travel/TripRadar/src/TripRadar.Server.Db/Models/UserOAuthTokens.cs \
        domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadarDbContext.cs
git commit -m "feat(db): add UserOAuthTokens table for per-user OAuth token vault

$(cat <<'EOF'
New table under TripRadar.Server schema. Stores encrypted access + refresh
tokens per (userId, service) pair with scopes and expiration. EF migration
follows in the next commit.
EOF
)"
```

## Task 1.2: Generate the migration

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Server.Db/Migrations/{timestamp}_AddUserOAuthTokens.cs` (generated)

- [ ] **Step 1: Run the EF migration generator**

```bash
cd domains/travel/TripRadar
dotnet ef migrations add AddUserOAuthTokens \
  --project src/TripRadar.Server.Db \
  --startup-project src/TripRadar.Server.Jobs.API \
  --output-dir Migrations
```

Expected: a new `*_AddUserOAuthTokens.cs` file in `src/TripRadar.Server.Db/Migrations/`.

- [ ] **Step 2: Read the generated file**

Verify it creates the `UserOAuthTokens` table with the composite unique index and the FK to `Users.UserId`. If anything looks wrong (e.g. missing schema), delete the migration and re-run.

- [ ] **Step 3: Apply to a scratch database and verify**

```bash
dotnet ef database update --project src/TripRadar.Server.Db --startup-project src/TripRadar.Server.Jobs.API
psql -h localhost -U tripradar -d tripradar -c "\d \"TripRadar.Server\".\"UserOAuthTokens\""
```

Expected: table schema matches the model.

- [ ] **Step 4: Commit**

```bash
git add domains/travel/TripRadar/src/TripRadar.Server.Db/Migrations/
git commit -m "feat(db): migration AddUserOAuthTokens"
```

## Task 1.3: Add `OAuthToken` domain value record

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Server.Domain/ValueObjects/OAuthToken.cs`

- [ ] **Step 1: Write the record**

```csharp
// domains/travel/TripRadar/src/TripRadar.Server.Domain/ValueObjects/OAuthToken.cs
namespace TripRadar.Server.Domain.ValueObjects;

public sealed record OAuthToken(
    string Service,
    string AccessToken,
    string? RefreshToken,
    string Scopes,
    DateTime ExpiresAt)
{
    public bool IsExpired(TimeSpan? skew = null)
        => ExpiresAt <= DateTime.UtcNow.Add(skew ?? TimeSpan.FromMinutes(1));
}
```

- [ ] **Step 2: Build**

```bash
dotnet build domains/travel/TripRadar/src/TripRadar.Server.Domain/TripRadar.Server.Domain.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add domains/travel/TripRadar/src/TripRadar.Server.Domain/ValueObjects/OAuthToken.cs
git commit -m "feat(domain): OAuthToken value record with IsExpired helper"
```

## Task 1.4: `StoreOAuthTokenCommand` + handler + test

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Server.Application/UseCases/Authentication/Commands/StoreOAuthToken/StoreOAuthTokenCommand.cs`
- Create: `.../StoreOAuthToken/StoreOAuthTokenCommandHandler.cs`
- Create: `domains/travel/TripRadar/src/TripRadar.Server.Tests/.../StoreOAuthTokenCommandHandlerTests.cs` (check `dotnet sln` for the right test project name — it may be `TripRadar.Server.Application.Tests` or similar)

- [ ] **Step 1: Write the failing test first**

```csharp
// StoreOAuthTokenCommandHandlerTests.cs
using FluentAssertions;
using TripRadar.Server.Application.UseCases.Authentication.Commands.StoreOAuthToken;
using TripRadar.Server.Domain.ValueObjects;
using TripRadar.Server.Tests.Fixtures;
using Xunit;

public class StoreOAuthTokenCommandHandlerTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Stores_new_token_as_upsert_first_write()
    {
        var userId = await db.SeedUser();
        var token = new OAuthToken(
            Service: "google",
            AccessToken: "access-abc",
            RefreshToken: "refresh-xyz",
            Scopes: "calendar.readonly,gmail.readonly",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30));

        var handler = db.Resolve<StoreOAuthTokenCommandHandler>();
        await handler.Handle(new StoreOAuthTokenCommand(userId, token), default);

        var row = await db.Context.UserOAuthTokens
            .SingleAsync(r => r.UserId == userId && r.Service == "google");
        row.AccessToken.Should().Be("access-abc");
        row.RefreshToken.Should().Be("refresh-xyz");
        row.Scopes.Should().Be("calendar.readonly,gmail.readonly");
    }

    [Fact]
    public async Task Overwrites_existing_token_on_second_call()
    {
        var userId = await db.SeedUser();
        var handler = db.Resolve<StoreOAuthTokenCommandHandler>();

        await handler.Handle(new StoreOAuthTokenCommand(userId,
            new OAuthToken("google", "old", "old-r", "old-scope", DateTime.UtcNow.AddMinutes(5))), default);
        await handler.Handle(new StoreOAuthTokenCommand(userId,
            new OAuthToken("google", "new", "new-r", "new-scope", DateTime.UtcNow.AddMinutes(30))), default);

        var rows = await db.Context.UserOAuthTokens
            .Where(r => r.UserId == userId && r.Service == "google").ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].AccessToken.Should().Be("new");
        rows[0].UpdatedOn.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test → expect failure**

```bash
dotnet test domains/travel/TripRadar/src/TripRadar.Server.Application.Tests \
  --filter "StoreOAuthTokenCommandHandlerTests"
```

Expected: compile error (command + handler don't exist yet).

- [ ] **Step 3: Write the command record**

```csharp
// StoreOAuthTokenCommand.cs
using MediatR;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.StoreOAuthToken;

public sealed record StoreOAuthTokenCommand(long UserId, OAuthToken Token) : IRequest;
```

- [ ] **Step 4: Write the handler**

```csharp
// StoreOAuthTokenCommandHandler.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Application.UseCases.Authentication.Commands.StoreOAuthToken;

public sealed class StoreOAuthTokenCommandHandler(TripRadarDbContext db)
    : IRequestHandler<StoreOAuthTokenCommand>
{
    public async Task Handle(StoreOAuthTokenCommand request, CancellationToken ct)
    {
        var existing = await db.UserOAuthTokens
            .FirstOrDefaultAsync(r => r.UserId == request.UserId && r.Service == request.Token.Service, ct);

        if (existing is null)
        {
            db.UserOAuthTokens.Add(new UserOAuthTokens
            {
                UserId = request.UserId,
                Service = request.Token.Service,
                AccessToken = request.Token.AccessToken,
                RefreshToken = request.Token.RefreshToken,
                Scopes = request.Token.Scopes,
                ExpiresAt = request.Token.ExpiresAt,
                CreatedOn = DateTime.UtcNow,
            });
        }
        else
        {
            existing.AccessToken = request.Token.AccessToken;
            existing.RefreshToken = request.Token.RefreshToken;
            existing.Scopes = request.Token.Scopes;
            existing.ExpiresAt = request.Token.ExpiresAt;
            existing.UpdatedOn = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run test → expect pass**

```bash
dotnet test domains/travel/TripRadar/src/TripRadar.Server.Application.Tests \
  --filter "StoreOAuthTokenCommandHandlerTests"
```

Expected: both tests pass.

- [ ] **Step 6: Commit**

```bash
git add domains/travel/TripRadar/src/TripRadar.Server.Application/UseCases/Authentication/Commands/StoreOAuthToken/ \
        domains/travel/TripRadar/src/TripRadar.Server.Application.Tests/
git commit -m "feat(auth): StoreOAuthTokenCommand + handler + tests"
```

## Task 1.5: `GetOAuthTokenQuery` + handler + test

**Files:**
- Create: `.../GetOAuthToken/GetOAuthTokenQuery.cs`
- Create: `.../GetOAuthToken/GetOAuthTokenQueryHandler.cs`
- Create: `.../GetOAuthTokenQueryHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Returns_null_when_no_token_exists()
{
    var userId = await db.SeedUser();
    var handler = db.Resolve<GetOAuthTokenQueryHandler>();
    var result = await handler.Handle(new GetOAuthTokenQuery(userId, "google"), default);
    result.Should().BeNull();
}

[Fact]
public async Task Returns_null_for_expired_token()
{
    var userId = await db.SeedUser();
    db.Context.UserOAuthTokens.Add(new()
    {
        UserId = userId, Service = "google",
        AccessToken = "a", Scopes = "calendar.readonly",
        ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
        CreatedOn = DateTime.UtcNow.AddHours(-1),
    });
    await db.Context.SaveChangesAsync();

    var handler = db.Resolve<GetOAuthTokenQueryHandler>();
    var result = await handler.Handle(new GetOAuthTokenQuery(userId, "google"), default);
    result.Should().BeNull();
}

[Fact]
public async Task Returns_token_when_present_and_unexpired()
{
    var userId = await db.SeedUser();
    db.Context.UserOAuthTokens.Add(new()
    {
        UserId = userId, Service = "google",
        AccessToken = "abc", RefreshToken = "xyz", Scopes = "calendar.readonly,gmail.readonly",
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        CreatedOn = DateTime.UtcNow,
    });
    await db.Context.SaveChangesAsync();

    var handler = db.Resolve<GetOAuthTokenQueryHandler>();
    var result = await handler.Handle(new GetOAuthTokenQuery(userId, "google"), default);
    result.Should().NotBeNull();
    result!.AccessToken.Should().Be("abc");
    result.RefreshToken.Should().Be("xyz");
    result.Scopes.Should().Be("calendar.readonly,gmail.readonly");
}
```

- [ ] **Step 2: Run the test → expect failure (types don't exist).**

- [ ] **Step 3: Write the query + handler**

```csharp
// GetOAuthTokenQuery.cs
using MediatR;
using TripRadar.Server.Domain.ValueObjects;
namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GetOAuthToken;
public sealed record GetOAuthTokenQuery(long UserId, string Service) : IRequest<OAuthToken?>;

// GetOAuthTokenQueryHandler.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db;
using TripRadar.Server.Domain.ValueObjects;
namespace TripRadar.Server.Application.UseCases.Authentication.Commands.GetOAuthToken;

public sealed class GetOAuthTokenQueryHandler(TripRadarDbContext db)
    : IRequestHandler<GetOAuthTokenQuery, OAuthToken?>
{
    public async Task<OAuthToken?> Handle(GetOAuthTokenQuery request, CancellationToken ct)
    {
        var row = await db.UserOAuthTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == request.UserId && r.Service == request.Service, ct);

        if (row is null) return null;

        var token = new OAuthToken(row.Service, row.AccessToken, row.RefreshToken, row.Scopes, row.ExpiresAt);
        return token.IsExpired() ? null : token;
    }
}
```

- [ ] **Step 4: Run the test → expect pass.**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(auth): GetOAuthTokenQuery + handler + tests"
```

## Task 1.6: `RefreshOAuthTokenCommand` + handler + test

**Files:**
- Create: `.../RefreshOAuthToken/RefreshOAuthTokenCommand.cs`
- Create: `.../RefreshOAuthToken/RefreshOAuthTokenCommandHandler.cs`
- Create: `.../RefreshOAuthTokenCommandHandlerTests.cs`

Implementation shape: inject `IMediator`, `TripRadarDbContext`, and an abstract `IOAuthProviderRefresher` that takes a service name + refresh token and returns a new `OAuthToken`. Test with a stub refresher; real implementations (Google/Uber) go in Phase 4.

- [ ] **Step 1: Write the failing test** (refresh returns new token; command upserts via `StoreOAuthTokenCommand`)
- [ ] **Step 2: Run test → fail**
- [ ] **Step 3: Define `IOAuthProviderRefresher` interface in `TripRadar.Server.Application.Contracts`** with `Task<OAuthToken> RefreshAsync(string service, string refreshToken, CancellationToken ct)`
- [ ] **Step 4: Write handler that dispatches `StoreOAuthTokenCommand` via `IMediator` after calling the refresher**
- [ ] **Step 5: Run test → pass**
- [ ] **Step 6: Commit:** `feat(auth): RefreshOAuthTokenCommand + handler + tests`

## Task 1.7: `RevokeOAuthTokenCommand` + handler + test

**Files:**
- Create: `.../RevokeOAuthToken/RevokeOAuthTokenCommand.cs`
- Create: `.../RevokeOAuthToken/RevokeOAuthTokenCommandHandler.cs`
- Create: `.../RevokeOAuthTokenCommandHandlerTests.cs`

- [ ] **Step 1:** Write failing test — revoking deletes the row; revoking a non-existent row is a no-op (no exception).
- [ ] **Step 2:** Run test → fail.
- [ ] **Step 3:** Write handler: `ExecuteDeleteAsync` on the filtered DbSet.
- [ ] **Step 4:** Run test → pass.
- [ ] **Step 5:** Commit: `feat(auth): RevokeOAuthTokenCommand + handler + tests`

## Task 1.8: EF value converter for encrypted columns

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Server.Infrastructure/Persistence/Converters/ProtectedStringConverter.cs`
- Modify: `domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadarDbContext.cs` — apply converter in `OnModelCreating` for `AccessToken` and `RefreshToken` columns

- [ ] **Step 1: Context7 query** — `ef-core` → "ValueConverter lifecycle + IDataProtectionProvider in OnModelCreating"

- [ ] **Step 2: Write the converter**

```csharp
// ProtectedStringConverter.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TripRadar.Server.Infrastructure.Persistence.Converters;

public sealed class ProtectedStringConverter : ValueConverter<string, string>
{
    public ProtectedStringConverter(IDataProtector protector)
        : base(
            plaintext => protector.Protect(plaintext),
            ciphertext => protector.Unprotect(ciphertext))
    { }
}
```

- [ ] **Step 3: Apply in `OnModelCreating` on `TripRadarDbContext`**

```csharp
// Add a constructor parameter: IDataProtectionProvider dpProvider
// In OnModelCreating:
var protector = _dpProvider.CreateProtector("ino.oauth");
var converter = new ProtectedStringConverter(protector);

modelBuilder.Entity<UserOAuthTokens>()
    .Property(t => t.AccessToken)
    .HasConversion(converter);

modelBuilder.Entity<UserOAuthTokens>()
    .Property(t => t.RefreshToken)
    .HasConversion(
        plaintext => plaintext == null ? null : protector.Protect(plaintext),
        ciphertext => ciphertext == null ? null : protector.Unprotect(ciphertext));
```

- [ ] **Step 4: Wire `AddDataProtection` in the TripRadar service DI setup** (check `TripRadar.Server.Infrastructure/DependencyInjection.cs` or similar)

```csharp
services.AddDataProtection()
    .SetApplicationName("ino")
    .PersistKeysToDbContext<TripRadarDbContext>();
```

(Alternative: `PersistKeysToFileSystem(...)` for dev. Choice is part of Task 0.4. Start with DbContext persistence so keys survive silo restarts.)

- [ ] **Step 5: Add integration test**

```csharp
[Fact]
public async Task Stored_tokens_are_encrypted_at_rest_and_round_trip_decrypted()
{
    var userId = await db.SeedUser();
    var handler = db.Resolve<StoreOAuthTokenCommandHandler>();
    await handler.Handle(new(userId, new OAuthToken("google", "secret-abc", "secret-xyz", "scope", DateTime.UtcNow.AddHours(1))), default);

    // Read raw column via a second connection to prove the column is not plaintext
    await using var conn = new NpgsqlConnection(db.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT \"AccessToken\" FROM \"TripRadar.Server\".\"UserOAuthTokens\" WHERE \"UserId\" = @u",
        conn);
    cmd.Parameters.AddWithValue("u", userId);
    var raw = (string)(await cmd.ExecuteScalarAsync())!;
    raw.Should().NotBe("secret-abc");
    raw.Should().NotContain("secret");

    // Decrypt round-trip via the query handler
    var decrypted = await db.Resolve<GetOAuthTokenQueryHandler>()
        .Handle(new GetOAuthTokenQuery(userId, "google"), default);
    decrypted.Should().NotBeNull();
    decrypted!.AccessToken.Should().Be("secret-abc");
    decrypted.RefreshToken.Should().Be("secret-xyz");
}
```

- [ ] **Step 6: Run test → expect pass**

```bash
dotnet test domains/travel/TripRadar/src/TripRadar.Server.Application.Tests \
  --filter "Stored_tokens_are_encrypted_at_rest"
```

- [ ] **Step 7: Commit**

```bash
git commit -am "feat(auth): encrypt UserOAuthTokens columns via ProtectedStringConverter"
```

## Task 1.9: Run full TripRadar test suite

- [ ] **Step 1: Run all tests**

```bash
cd domains/travel/TripRadar
dotnet test
```

Expected: every existing test still passes + the new tests for Phase 1.

- [ ] **Step 2: Aspire smoke test** — per the TripRadar CLAUDE.md:

```bash
dotnet build src/Aspire/Aspire.csproj
```

Then use `mcp__aspire__list_resources` to verify the TripRadar stack boots green with the new migration applied.

- [ ] **Step 3: Phase 1 checkpoint** — **STOP** if anything is red. All Phase 1 tests green → proceed to Phase 2.

---

# Phase 2 — Six-facet Neuron runtime

**Goal:** Extend the existing `Neuron` record + `NeuronGrain` in `features/ino-new/InoNew.Core/` with three new facets (`ToolRefs`, `ModelHints`, `RfwTemplateSource`) and three new script globals (`Tools`, `Chat`, `Rfw`). `Agent<T>` stays as-is; a reflection adapter at silo startup generates shadow Neuron records.

**Duration:** ~1 day, parallelizable with Phase 1.

**Context:** Read `features/ino-new/InoNew.Core/NeuronGrain.cs` (255 LOC) end-to-end before starting. Read `features/ino-new/InoNew.Core/Specialists/EvolutionHandler.cs` for the existing script-compile pattern.

## Task 2.1: Extend `Neuron` record with three new fields

**Files:**
- Modify: `features/ino-new/InoNew.Core/Neuron.cs`

- [ ] **Step 1: Write the diff**

Replace the existing `Neuron` record:

```csharp
[GenerateSerializer]
public sealed record Neuron(
    [property: Id(0)]  string Id,
    [property: Id(1)]  string Name,
    [property: Id(2)]  string Purpose,
    [property: Id(3)]  IReadOnlyList<string> Capabilities,
    [property: Id(4)]  DateTimeOffset CreatedAt,
    [property: Id(5)]  IReadOnlyDictionary<string, string> Metadata,
    [property: Id(6)]  string? SynapseSchema = null,
    [property: Id(7)]  global::Core.ML.FeatureSchema? FeatureSchema = null,
    [property: Id(8)]  string? ScriptSource = null,
    [property: Id(9)]  string? AuthorId = null,
    [property: Id(10)] string DomainId = "default",
    [property: Id(11)] IReadOnlyList<string>? ToolRefs = null,
    [property: Id(12)] ModelHints? ModelHints = null,
    [property: Id(13)] string? RfwTemplateSource = null);

[GenerateSerializer]
public sealed record ModelHints(
    [property: Id(0)] string Model,
    [property: Id(1)] string SystemPrompt,
    [property: Id(2)] float Temperature = 0.2f);
```

Update `Blueprint` with the same three new optional fields.

- [ ] **Step 2: Build**

```bash
dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj
```

Expected: build succeeds (existing tests will still pass because new fields are optional).

- [ ] **Step 3: Commit**

```bash
git commit -am "feat(core): extend Neuron record with ToolRefs, ModelHints, RfwTemplateSource facets"
```

## Task 2.2: Extend `SynapseResult` with RFW bytes and NeedsEvolution factory

**Files:**
- Modify: `features/ino-new/InoNew.Core/SynapseResult.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// features/ino-new/InoNew.Tests/SynapseResultTests.cs
[Fact]
public void AuthRequired_factory_sets_auth_required_verb_and_service()
{
    var r = SynapseResult.AuthRequired("google", ["calendar.readonly", "gmail.readonly"]);
    r.Success.Should().BeFalse();
    r.Verb.Should().Be("auth_required");
    r.Service.Should().Be("google");
    r.Scopes.Should().ContainInOrder("calendar.readonly", "gmail.readonly");
}

[Fact]
public void NeedsEvolution_factory_carries_base_id_and_hint()
{
    var r = SynapseResult.NeedsEvolution("home_resolver", "store locations", "user asked for 'home'");
    r.Success.Should().BeFalse();
    r.Verb.Should().Be("needs_evolution");
    r.EvolutionBlueprint.Should().NotBeNull();
    r.EvolutionBlueprint!.BaseId.Should().Be("home_resolver");
    r.EvolutionBlueprint.Hint.Should().Be("user asked for 'home'");
}
```

- [ ] **Step 2: Run → fail (factories and fields don't exist).**

- [ ] **Step 3: Update the record**

```csharp
[GenerateSerializer]
public sealed record SynapseResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Payload,
    [property: Id(2)] string Verb,
    [property: Id(3)] byte[]? RfwDescription = null,
    [property: Id(4)] byte[]? RfwData = null,
    [property: Id(5)] string? Service = null,
    [property: Id(6)] IReadOnlyList<string>? Scopes = null,
    [property: Id(7)] EvolutionBlueprintHint? EvolutionBlueprint = null)
{
    public static SynapseResult Ok(string verb, string payload = "")
        => new(true, payload, verb);

    public static SynapseResult Error(string verb, string message)
        => new(false, message, verb);

    public static SynapseResult AuthRequired(string service, IReadOnlyList<string> scopes)
        => new(false, string.Empty, "auth_required", Service: service, Scopes: scopes);

    public static SynapseResult NeedsEvolution(string baseId, string purpose, string hint)
        => new(false, hint, "needs_evolution",
            EvolutionBlueprint: new EvolutionBlueprintHint(baseId, purpose, hint));
}

[GenerateSerializer]
public sealed record EvolutionBlueprintHint(
    [property: Id(0)] string BaseId,
    [property: Id(1)] string Purpose,
    [property: Id(2)] string Hint);
```

- [ ] **Step 4: Run test → pass.**

- [ ] **Step 5: Commit.** `feat(core): SynapseResult RFW bytes + AuthRequired + NeedsEvolution factories`

## Task 2.3: `ToolFacade` — per-neuron sandboxed grain access

**Files:**
- Create: `features/ino-new/InoNew.Core/Runtime/ToolFacade.cs`
- Create: `features/ino-new/InoNew.Tests/ToolFacadeTests.cs`

The facade's job: given a `ToolRefs` whitelist, expose `Tools.SomeThing.Method(...)` that resolves to `Grains.GetGrain<ISomeThing>(...)` — BUT throws on anything outside the whitelist.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Tools_resolves_allowed_grain_interface()
{
    var grains = new FakeGrainFactory();
    var tools = new ToolFacade(grains, toolRefs: new[] { "IShell" }, userId: "u1");

    var shell = tools.Get<IShell>();
    shell.Should().NotBeNull();
    grains.Resolved.Should().Contain(typeof(IShell));
}

[Fact]
public void Tools_throws_on_unauthorized_interface()
{
    var grains = new FakeGrainFactory();
    var tools = new ToolFacade(grains, toolRefs: new[] { "IShell" }, userId: "u1");

    var act = () => tools.Get<IFileSystem>();
    act.Should().Throw<UnauthorizedToolAccessException>()
        .WithMessage("*IFileSystem*not in ToolRefs*");
}
```

- [ ] **Step 2: Run → fail.**

- [ ] **Step 3: Write the facade**

```csharp
// ToolFacade.cs
using System.Collections.Frozen;
using Orleans;

namespace InoNew.Core.Runtime;

public sealed class ToolFacade(IGrainFactory grains, IReadOnlyList<string> toolRefs, string userId)
{
    readonly FrozenSet<string> _whitelist = toolRefs.ToFrozenSet(StringComparer.Ordinal);

    public T Get<T>(string? key = null) where T : IGrainWithStringKey
    {
        var interfaceName = typeof(T).Name;
        if (!_whitelist.Contains(interfaceName))
            throw new UnauthorizedToolAccessException(
                $"Tool '{interfaceName}' not in ToolRefs for neuron (user={userId}). " +
                $"Allowed: [{string.Join(", ", _whitelist)}].");

        return grains.GetGrain<T>(key ?? userId);
    }
}

public sealed class UnauthorizedToolAccessException(string message) : Exception(message);
```

- [ ] **Step 4: Run → pass.**

- [ ] **Step 5: Commit.** `feat(core): ToolFacade with ToolRefs whitelist sandboxing`

## Task 2.4: `ChatFacade` — LLM with ModelHints baked in

**Files:**
- Create: `features/ino-new/InoNew.Core/Runtime/ChatFacade.cs`
- Create: `features/ino-new/InoNew.Tests/ChatFacadeTests.cs`

- [ ] **Step 1: Write failing test using `MockChatClient` from `iaw/Testing`.** Test: `AskAsync("question")` prepends the ModelHints.SystemPrompt as a system message and returns the LLM text.

- [ ] **Step 2: Run → fail.**

- [ ] **Step 3: Write the facade**

```csharp
using Microsoft.Extensions.AI;

namespace InoNew.Core.Runtime;

public sealed class ChatFacade(IChatClient client, ModelHints? hints)
{
    public async Task<string> AskAsync(string userPrompt, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();
        if (hints?.SystemPrompt is { Length: > 0 } sys)
            messages.Add(new(ChatRole.System, sys));
        messages.Add(new(ChatRole.User, userPrompt));

        var options = new ChatOptions { Temperature = hints?.Temperature ?? 0.2f };
        var response = await client.GetResponseAsync(messages, options, ct);
        return response.Text ?? string.Empty;
    }
}
```

- [ ] **Step 4: Run → pass.**

- [ ] **Step 5: Commit.** `feat(core): ChatFacade wrapping IChatClient with ModelHints`

## Task 2.5: `RfwBuilder` — fluent template scratchpad

**Files:**
- Create: `features/ino-new/InoNew.Core/Runtime/RfwBuilder.cs`
- Create: `features/ino-new/InoNew.Tests/RfwBuilderTests.cs`

The builder is a small holder for the RFW library description string + data dictionary that the `RfwTemplateSource` script populates. The NeuronGrain then calls `Build()` to get `(byte[] desc, byte[] data)` and stamps it onto the SynapseResult.

- [ ] **Step 1: Write failing test** — `Rfw.Description = "..."; Rfw.Data["key"] = value;` → `Build()` returns UTF-8 bytes + JSON-encoded data. Strip `\r` from description (Dart RFW parser rejects CRLF — see known-problem note in `InoService.cs:95-98`).

- [ ] **Step 2: Run → fail.**

- [ ] **Step 3: Write builder**

```csharp
using System.Text;
using System.Text.Json;

namespace InoNew.Core.Runtime;

public sealed class RfwBuilder
{
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> Data { get; } = new();

    public (byte[] Description, byte[] Data) Build()
    {
        var desc = Description.Replace("\r", "");
        return (Encoding.UTF8.GetBytes(desc),
                JsonSerializer.SerializeToUtf8Bytes(Data));
    }
}
```

- [ ] **Step 4: Run → pass.** **Step 5: Commit.** `feat(core): RfwBuilder scratchpad with CRLF-strip`

## Task 2.6: Extend `NeuronScriptGlobals`

**Files:**
- Modify: `features/ino-new/InoNew.Core/NeuronScriptGlobals.cs`

- [ ] **Step 1: Add three new properties:**

```csharp
public sealed class NeuronScriptGlobals
{
    // Existing:
    public required IGrainFactory Grains { get; init; }
    public required string NeuronId { get; init; }
    public required Synapse Synapse { get; init; }
    public required ILogger Log { get; init; }

    // New:
    public required ToolFacade Tools { get; init; }
    public required ChatFacade Chat { get; init; }
    public required RfwBuilder Rfw { get; init; }
}
```

- [ ] **Step 2: Build** — `dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj`. Expect compile errors in every existing caller that constructs `NeuronScriptGlobals`.

- [ ] **Step 3: Update `NeuronGrain.ExecuteScriptAsync`** to construct the three new facades when populating globals. The `Chat` facade's `IChatClient` comes from `ServiceProvider`, the `ModelHints` from `_state.State.Definition.ModelHints`. `Tools` is constructed from `Grains`, `Definition.ToolRefs ?? []`, and the synapse's user id.

- [ ] **Step 4: Build and run the existing `InoNew.Tests`** — expect all green.

- [ ] **Step 5: Commit.** `feat(core): NeuronScriptGlobals adds Tools/Chat/Rfw facades`

## Task 2.7: `NeuronGrain.HandleAsync` runs `RfwTemplateSource` after `ScriptSource`

**Files:**
- Modify: `features/ino-new/InoNew.Core/NeuronGrain.cs`

- [ ] **Step 1: Write failing test** — A neuron with both `ScriptSource` (returns SynapseResult.Ok without RFW bytes) and `RfwTemplateSource` (populates `Rfw.Description`/`Rfw.Data` from `Result.Payload`) should end up with RFW bytes on the returned SynapseResult.

- [ ] **Step 2: Run → fail.**

- [ ] **Step 3: Extend `HandleAsync`:**

After `ExecuteScriptAsync(scriptSource, ...)` returns a `result`, if `result.Success && result.RfwDescription is null && _state.State.Definition.RfwTemplateSource is { } rfwSource`, compile+run the RFW script with a `NeuronRfwGlobals { Result = result, Rfw = new RfwBuilder() }`, then:

```csharp
var (desc, data) = rfwGlobals.Rfw.Build();
result = result with { RfwDescription = desc, RfwData = data };
```

Cache the compiled RFW script separately from the main script (second `ScriptRunner` + second hash field).

- [ ] **Step 4: Run test → pass.** **Step 5: Commit.** `feat(core): NeuronGrain runs RfwTemplateSource after ScriptSource`

## Task 2.8: Thread `NeedsEvolution` dispatch in `NeuronGrain.HandleAsync`

**Files:**
- Modify: `features/ino-new/InoNew.Core/NeuronGrain.cs`
- Modify: `features/ino-new/InoNew.Core/Specialists/EvolutionHandler.cs`

- [ ] **Step 1: Write failing test** — A neuron whose script returns `SynapseResult.NeedsEvolution("home_resolver", ...)` should cause `NeuronGrain.HandleAsync` to dispatch to `EvolutionHandler` with the blueprint hint; `EvolutionHandler` creates a neuron with id `home_resolver_{userId}` and re-fires the original synapse at it.

- [ ] **Step 2: Run → fail.**

- [ ] **Step 3: Update `EvolutionHandler` to accept a `EvolutionBlueprintHint?` param** — when set, the LLM prompt uses the hint instead of the "no matching specialist" catch-all prompt. The neuron id is composed as `{baseId}_{userId}`.

- [ ] **Step 4: In `NeuronGrain.HandleAsync`,** after the script returns, if `result.Verb == "needs_evolution"` and `result.EvolutionBlueprint is { } hint`:

```csharp
var evolution = ServiceProvider.GetRequiredService<EvolutionHandler>();
var evolutionSynapse = synapse with { Payload = hint.Hint };
result = await evolution.HandleWithHintAsync(evolutionSynapse, GrainFactory, hint, ct);
```

- [ ] **Step 5: Run test → pass.** **Step 6: Commit.** `feat(core): NeedsEvolution dispatch threaded through NeuronGrain + EvolutionHandler`

## Task 2.9: `Agent<T>` reflection adapter

**Files:**
- Create: `iaw/Core/Registry/AgentToNeuronAdapter.cs`
- Modify: `iaw/Core/Registry/AgentRegistrationStartupTask.cs`

- [ ] **Step 1: Write failing test** — For a fake `Agent<IFoo>` subclass with `[LLMAgent("Foo", "does foo")]` and `[RfwCard("FooCard")]`, the adapter generates a `Neuron` record with `SynapseSchema` derived from `IFoo`'s method signatures (use Roslyn `CSharpSyntaxTree.ParseText` on a reconstructed source) and `RfwTemplateSource` looked up from an attribute-provided resource path.

- [ ] **Step 2: Run → fail.**

- [ ] **Step 3: Write the adapter**

```csharp
public static class AgentToNeuronAdapter
{
    public static Neuron BuildFromAgentType(Type agentType)
    {
        var llmAttr = agentType.GetCustomAttribute<LLMAgentAttribute>()
            ?? throw new InvalidOperationException($"{agentType.Name} missing [LLMAgent]");
        var rfwAttr = agentType.GetCustomAttribute<RfwCardAttribute>();

        var interfaceType = agentType.BaseType!.GetGenericArguments()[0];
        var schema = BuildInterfaceSource(interfaceType);
        var toolRefs = ExtractToolRefsFromPrompt(llmAttr.Prompt);
        var hints = new ModelHints(llmAttr.Model, llmAttr.Prompt);
        var rfwSource = rfwAttr is null ? null : LoadEmbeddedResource(agentType.Assembly, rfwAttr.ResourcePath);

        return new Neuron(
            Id: llmAttr.Name.ToLowerInvariant(),
            Name: llmAttr.Name,
            Purpose: llmAttr.Description,
            Capabilities: [],
            CreatedAt: DateTimeOffset.UtcNow,
            Metadata: new Dictionary<string, string> { ["source"] = "compile-time-adapter" },
            SynapseSchema: schema,
            ScriptSource: null,
            ToolRefs: toolRefs,
            ModelHints: hints,
            RfwTemplateSource: rfwSource,
            AuthorId: "compile-time",
            DomainId: ExtractDomain(agentType));
    }
    // ... helpers
}
```

- [ ] **Step 4: Update `AgentRegistrationStartupTask`** to call `BuildFromAgentType` per discovered agent and register the shadow Neuron via `INeuronRegistry.CreateAsync`.

- [ ] **Step 5: Run tests including existing `AgentRegistrationStartupTaskTests`.** Fix any failures.

- [ ] **Step 6: Commit.** `feat(core): AgentToNeuronAdapter — shadow Neuron records for Agent<T> subclasses`

## Task 2.10: Phase 2 checkpoint

- [ ] **Step 1:** `dotnet test ino.slnx` — all green.
- [ ] **Step 2:** `dotnet build ino.slnx` — clean.
- [ ] **Step 3:** Commit any stragglers. Phase 2 complete — proceed to Phase 3.

---

# Phase 3 — Solution restructure

**Goal:** Move the codebase to the production layout: `src/Core`, `src/Neurons`, `src/Gateways`, `src/Host`, `src/ServiceDefaults`, `src/Testing`, plus `Aspire/`, `clients/`, `tests/`. TripRadar stays put. `iaw/Telegram/` is absorbed into `TripRadar.Bot`. The `Orchestration → Synapse` rename rides along.

**Duration:** ~1 day. This is the most brittle phase — do it in six batches, each a separate commit, with a green build after each.

**Critical:** Verify `git status` clean before every batch. If a batch fails, `git reset --hard` the batch and retry. **Do not** mix batches into one commit.

## Task 3.1: Batch 1 — Orchestration → Synapse rename (in place)

**Files:** every reference to `CodeOrchestrator*`, `OrchestrationResult`, `iaw/Agents/Orchestration/`.

- [ ] **Step 1: Create a migration branch inside the worktree**

```bash
git switch -c restructure-batch-1-synapse-rename
```

- [ ] **Step 2: Rename the folder**

```bash
git mv iaw/Agents/Orchestration iaw/Agents/Synapse
```

- [ ] **Step 3: Scripted find/replace across all `.cs`, `.csproj`, `.md`**

Use sed (macOS/Linux) or ripgrep + sed (Windows bash):

```bash
# From repo root
rg -l "CodeOrchestratorAgent" --type cs --type csproj \
  | xargs sed -i 's/CodeOrchestratorAgent/SynapseNeuron/g'
rg -l "ICodeOrchestrator" --type cs --type csproj \
  | xargs sed -i 's/ICodeOrchestrator/ISynapseNeuron/g'
rg -l "OrchestrationResult" --type cs --type csproj \
  | xargs sed -i 's/OrchestrationResult/SynapseResult/g'
rg -l "IAW\.Agents\.Orchestration" --type cs --type csproj \
  | xargs sed -i 's/IAW\.Agents\.Orchestration/IAW.Agents.Synapse/g'
```

**Name-collision caveat:** `SynapseResult` already exists in `features/ino-new/InoNew.Core/SynapseResult.cs` (from Phase 2). The old `OrchestrationResult` lives in `iaw/Core/Contracts/OrchestrationResult.cs`. **Merge the two into one `SynapseResult` type in `features/ino-new/InoNew.Core/`** and have the old file delete itself. Validate no signature drift by comparing the two types before merge — if they differ, carry the delta forward.

- [ ] **Step 4: Rename the filename**

```bash
git mv iaw/Core/Contracts/OrchestrationResult.cs /dev/null  # delete, merged into InoNew.Core
git mv iaw/Agents/Synapse/CodeOrchestratorAgent.cs iaw/Agents/Synapse/SynapseNeuron.cs
git mv iaw/Core/Contracts/ICodeOrchestrator.cs iaw/Core/Contracts/ISynapseNeuron.cs
```

- [ ] **Step 5: Build**

```bash
dotnet build ino.slnx
```

Expected: builds succeed. If not, fix individual compile errors (they're almost always leftover `Orchestration` strings in comments or attribute values that the sed missed).

- [ ] **Step 6: Run full test suite**

```bash
dotnet test ino.slnx
```

Expected: green.

- [ ] **Step 7: Commit**

```bash
git commit -am "refactor(rename): CodeOrchestrator → Synapse; Orchestration folder → Synapse

$(cat <<'EOF'
Mechanical rename pass for known-problem #1. No behavioral changes.
CodeOrchestratorAgent → SynapseNeuron
ICodeOrchestrator     → ISynapseNeuron
OrchestrationResult   → SynapseResult (merged into InoNew.Core, deduped)
iaw/Agents/Orchestration/ → iaw/Agents/Synapse/
EOF
)"
```

## Task 3.2: Batch 2 — `iaw/Core` + `features/ino-new/InoNew.Core` → `src/Core/`

- [ ] **Step 1: Create** `src/` directory at repo root.

```bash
mkdir -p src
```

- [ ] **Step 2: `git mv`** iaw/Core → src/Core

```bash
git mv iaw/Core src/Core
```

- [ ] **Step 3: Merge `features/ino-new/InoNew.Core/` into `src/Core/Neurons/`**

```bash
mkdir -p src/Core/Neurons
git mv features/ino-new/InoNew.Core/*.cs src/Core/Neurons/
git mv features/ino-new/InoNew.Core/Specialists src/Core/Neurons/Specialists
git mv features/ino-new/InoNew.Core/Runtime src/Core/Neurons/Runtime
git mv features/ino-new/InoNew.Core/Skills src/Core/Neurons/Skills
rm -r features/ino-new/InoNew.Core
```

- [ ] **Step 4: Update namespaces — `InoNew.Core` → `Ino.Core.Neurons`** via scripted find/replace.

- [ ] **Step 5: Update `ino.slnx`** to point at the new `src/Core/Core.csproj` path + delete the `features/ino-new/InoNew.Core/InoNew.Core.csproj` reference.

- [ ] **Step 6: Update all project references in other `.csproj` files** (`InoNew.Core.csproj` → `Core.csproj`).

- [ ] **Step 7: Build** `dotnet build ino.slnx`. Fix compile errors.

- [ ] **Step 8: Run tests.** `dotnet test ino.slnx`. The `features/ino-new/InoNew.Tests` project may need its `InoNew.Core` ref updated.

- [ ] **Step 9: Commit.** `refactor(restructure): fold iaw/Core + features/ino-new/InoNew.Core → src/Core`

## Task 3.3: Batch 3 — `iaw/Agents*`, `iaw/MCP`, `iaw/Agents.Host` moves

Three sub-commits, one per folder. Verify build after each.

- [ ] **Step 3.3a:** `git mv iaw/Agents src/Neurons` — scripted namespace `IAW.Agents` → `Ino.Neurons`. Move by sub-folder: `src/Neurons/System` (Orchestration/Infrastructure/Web), `src/Neurons/Coding` (moved from `iaw/Agents.CSharp`). Build + test + commit.

- [ ] **Step 3.3b:** `git mv iaw/Agents.CSharp src/Neurons/Coding` (merge into existing). Scripted namespace `IAW.Agents.CSharp` → `Ino.Neurons.Coding`. Build + test + commit.

- [ ] **Step 3.3c:** `git mv iaw/MCP src/Gateways/Mcp`. Namespace `IAW.MCP` → `Ino.Gateways.Mcp`. Build + test + commit.

- [ ] **Step 3.3d:** `git mv iaw/Agents.Host src/Host`. Namespace `IAW.Agents.Host` → `Ino.Host`. Update entrypoint `Program.cs` path references (silo startup wiring). Build + test + commit.

## Task 3.4: Batch 4 — Aspire unification

**Files:**
- `git mv iaw/Aspire.Hosting Aspire/ino.Hosting`
- `git mv iaw/Aspire.Client Aspire/ino.Client`
- `git mv iaw/Aspire Aspire/ino.AppHost`
- `git mv iaw/Testing src/Testing`

- [ ] **Step 1: Move the folders** as above. Update `ino.slnx` paths.

- [ ] **Step 2: Merge TripRadar's `builder.AddTripRadar()` into the unified AppHost.**

Edit `Aspire/ino.AppHost/AppHost.cs` (formerly `iaw/Aspire/AppHost.cs`) to match the spec section 5.3 code exactly:

```csharp
// (See spec section 5.3 for the full code listing)
var builder = DistributedApplication.CreateBuilder(args);

var googleClientId     = builder.AddParameter("google-client-id");
var googleClientSecret = builder.AddParameter("google-client-secret", secret: true);
var telegramBotToken   = builder.AddParameter("telegram-bot-token", secret: true);
var jwtSigningKey      = builder.AddParameter("jwt-signing-key", secret: true);
var dpMasterKey        = builder.AddParameter("dp-master-key", secret: true);

var tripradar = builder.AddTripRadar(config => config
    .WithPostgres()
    .WithRedis()
    .WithElasticsearch()
    .WithKafka()
    .WithStripe()
    .WithCloudflaredTunnel());

var inoSilo = builder.AddProject<Projects.ino_Host>("ino-silo")
    .WithReference(tripradar.Postgres)
    .WithReference(tripradar.Redis)
    .WithEnvironment("JWT__SIGNING_KEY", jwtSigningKey)
    .WithEnvironment("DP__MASTER_KEY", dpMasterKey);

var inoMcp = builder.AddProject<Projects.ino_Gateways_Mcp>("ino-mcp")
    .WithReference(inoSilo);

tripradar.Bot
    .WithReference(inoSilo)
    .WithEnvironment("GOOGLE__CLIENT_ID", googleClientId)
    .WithEnvironment("GOOGLE__CLIENT_SECRET", googleClientSecret)
    .WithEnvironment("TELEGRAM__BOT_TOKEN", telegramBotToken)
    .WithEnvironment("JWT__SIGNING_KEY", jwtSigningKey)
    .WithEnvironment("DP__MASTER_KEY", dpMasterKey)
    .WithInoFrontend("clients/ino.flutter/build/web");

builder.Build().Run();
```

- [ ] **Step 3: Reference the TripRadar projects from `ino.slnx`.** Add explicit `<Project Path="domains/travel/TripRadar/src/Aspire/..." />` entries for `Aspire`, `Hosting.TripRadar`, `Bot`, `Server.Domain`, `Server.Application`, `Server.Infrastructure`, `Server.Db`, `Server.API.Contracts`, `ServiceDefaults`. The `TripRadar.slnx` keeps all of these too — projects in two solutions simultaneously is fine.

- [ ] **Step 4: Create `WithInoFrontend` extension** at `Aspire/ino.Hosting/InoHostingExtensions.cs`:

```csharp
public static class InoHostingExtensions
{
    public static IResourceBuilder<ProjectResource> WithInoFrontend(
        this IResourceBuilder<ProjectResource> bot, string flutterWebDir)
    {
        var absDir = Path.GetFullPath(flutterWebDir);
        return bot.WithEnvironment("INO_FLUTTER_WEB_DIR", absDir);
    }
}
```

The bot reads `INO_FLUTTER_WEB_DIR` at startup and wires `PhysicalFileProvider` + SPA fallback + browser gRPC-Web + native gRPC dual listener (Task 3.6).

- [ ] **Step 5: Build**. `dotnet build ino.slnx`.

- [ ] **Step 6: `aspire start`** — verify every resource boots green. Use `mcp__aspire__list_resources`.

- [ ] **Step 7: Commit.** `refactor(restructure): Batch 4 — unify Aspire AppHost with TripRadar backbone`

## Task 3.5: Batch 5 — clients/, tests/, aspire.config.json

- [ ] **Step 1:**

```bash
mkdir clients
git mv ino.flutter clients/ino.flutter
git mv ino.windows clients/ino.windows
git mv test tests
```

- [ ] **Step 2: Update paths in**
  - `ino.slnx` (test project references)
  - `aspire.config.json` (AppHost path)
  - `README.md` (any path references — full doc rewrite in Phase 6)
  - `CLAUDE.md` (full doc rewrite in Phase 6, just fix the paths that break)
  - `.gitattributes`, `.gitignore` if they mention paths

- [ ] **Step 3: Build + test.** `dotnet build ino.slnx && dotnet test ino.slnx`. `aspire start` verify.

- [ ] **Step 4: Commit.** `refactor(restructure): Batch 5 — clients/, tests/, path updates`

## Task 3.6: Batch 6 — Bot consolidation (three sub-phases)

**Phase 6.a — Absorb ino endpoints into TripRadar.Bot (parallel bots)**

- [ ] **Step 1:** Copy these handlers from `iaw/Telegram/Services/` (now `src/Gateways/Grpc/`) and `iaw/Telegram/Program.cs` (the OTLP bridge + `/ino` + wwwroot hosting) into `domains/travel/TripRadar/src/TripRadar.Bot/`:
  - `InoService` (gRPC) → `TripRadar.Bot/Ino/InoService.cs`
  - `/ino` command endpoint → `TripRadar.Bot/Ino/InoCommandEndpoint.cs`
  - `/otlp/v1/traces` and `/otlp/v1/logs` endpoints → `TripRadar.Bot/Ino/OtlpBridge.cs`
  - Flutter wwwroot static hosting → `TripRadar.Bot/Ino/InoFrontendExtensions.cs` (reads `INO_FLUTTER_WEB_DIR` env var)
  - New OAuth callback endpoints: `TripRadar.Bot/Auth/GoogleCallbackEndpoint.cs`, `.../UberCallbackEndpoint.cs` (empty scaffolds — implemented in Phase 4)

- [ ] **Step 2:** Wire into `TripRadar.Bot/Program.cs`:

```csharp
// Two Kestrel listeners: HTTP/2-only for native gRPC, HTTP/1.1+2 for browser
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, o => o.Protocols = HttpProtocols.Http2);
    options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

app.UseGrpcWeb();
app.MapGrpcService<InoService>().EnableGrpcWeb();

// Flutter static files + SPA fallback
var flutterDir = Environment.GetEnvironmentVariable("INO_FLUTTER_WEB_DIR");
if (Directory.Exists(flutterDir))
{
    app.UseFileServer(new FileServerOptions
    {
        FileProvider = new PhysicalFileProvider(flutterDir),
        EnableDirectoryBrowsing = false,
    });
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(flutterDir),
    });
}

// Ino endpoints
app.MapInoCommands();
app.MapOtlpBridge();
app.MapGoogleOAuthCallback();
app.MapUberOAuthCallback();
```

- [ ] **Step 3:** Add Orleans cluster client via `AddOrleansClient` → `.WithReference(inoSilo)` wiring in AppHost is already done in Batch 4. The bot's `InoService` resolves `IClusterClient` from DI.

- [ ] **Step 4:** Build + test + `aspire start`. Both bots should boot (iaw/Telegram also still boots its webhook at this stage). Verify Flutter miniapp loads from the TripRadar.Bot endpoint by curling `http://{tripradar-bot-url}/` and seeing `index.html`.

- [ ] **Step 5: Commit.** `refactor(bot): absorb ino endpoints into TripRadar.Bot (parallel)`

**Phase 6.b — Switch the Telegram webhook**

- [ ] **Step 1:** Re-register the Telegram bot webhook to point at TripRadar.Bot's `/api/telegram/webhook`:

```bash
# Manual, via curl or BotFather setwebhook:
curl -X POST "https://api.telegram.org/bot<TOKEN>/setWebhook?url=<tripradar-bot-cloudflared-url>/api/telegram/webhook"
```

- [ ] **Step 2:** Verify by sending `/start` to the bot; it should respond via the TripRadar.Bot-hosted handler.

- [ ] **Step 3:** `iaw/Telegram` is now dead code receiving nothing.

- [ ] **Step 4: Commit a no-op marker commit:** `chore(bot): webhook switched to TripRadar.Bot; iaw/Telegram is dead code`

**Phase 6.c — Delete iaw/Telegram and rename the bot**

- [ ] **Step 1:**

```bash
git rm -r iaw/Telegram
git mv domains/travel/TripRadar/src/TripRadar.Bot domains/travel/TripRadar/src/ino.Bot
```

- [ ] **Step 2:** Update `TripRadar.slnx` project path from `TripRadar.Bot.csproj` → `ino.Bot.csproj`. Rename the csproj file and any namespaces (`TripRadar.Bot` → `Ino.Bot`). Scripted find/replace.

- [ ] **Step 3:** Update `ino.slnx` references to the renamed project.

- [ ] **Step 4:** Update `domains/travel/TripRadar/CLAUDE.md` — the bot rename is a TripRadar-side edit. Mention that ino.Bot is the unified ecosystem bot, absorbing TripRadar's travel bot responsibilities.

- [ ] **Step 5:** Build + test + `aspire start`.

- [ ] **Step 6: Commit.** `refactor(bot): delete iaw/Telegram, rename TripRadar.Bot → ino.Bot`

## Task 3.7: Phase 3 checkpoint

- [ ] **Step 1:** `iaw/` directory should no longer exist at the repo root. Verify: `ls iaw 2>/dev/null` → empty.
- [ ] **Step 2:** `dotnet build ino.slnx` + `dotnet test ino.slnx` — all green.
- [ ] **Step 3:** `dotnet build domains/travel/TripRadar/TripRadar.slnx` — standalone travel build still works.
- [ ] **Step 4:** `aspire start` — full ecosystem boots. `mcp__aspire__list_resources` shows every expected resource Healthy.
- [ ] **Step 5:** Proceed to Phase 4.

---

# Phase 4 — Auth cascade wiring

**Goal:** Wire real identity + OAuth cascade end-to-end. Flutter reads Telegram initData, posts to the existing `TelegramLoginCommand`, receives JWT. Scripts in neurons call `Tools.AuthVault` which dispatches to the MediatR commands from Phase 1. Google callback + UberMock callback land on the consolidated bot.

**Duration:** ~1 day. Depends on Phases 1, 2, 3.

## Task 4.1: Flutter — Telegram initData JS interop

**Files:**
- Create: `clients/ino.flutter/lib/auth/telegram_init_data.dart`

- [ ] **Step 1:** Context7 — `rive-flutter` is only needed later. For this task: check Telegram Mini App JS spec (not a C7 library, just https://core.telegram.org/bots/webapps — note in comment).

- [ ] **Step 2: Write the interop**

```dart
// clients/ino.flutter/lib/auth/telegram_init_data.dart
import 'dart:js_interop';

@JS('Telegram.WebApp.initData')
external String? get _telegramInitData;

@JS('Telegram.WebApp.ready')
external void _telegramReady();

class TelegramInitData {
  static String? read() {
    try {
      _telegramReady();
      return _telegramInitData;
    } catch (_) {
      return null;  // not running in Telegram
    }
  }
}
```

- [ ] **Step 3: Commit.** `feat(flutter): Telegram initData JS interop`

## Task 4.2: Flutter — `TelegramAuthFlow`

**Files:**
- Create: `clients/ino.flutter/lib/auth/telegram_auth_flow.dart`
- Create: `clients/ino.flutter/lib/auth/session_storage.dart`
- Modify: `clients/ino.flutter/pubspec.yaml` — add `flutter_secure_storage`

- [ ] **Step 1: Add dependency**

```bash
cd clients/ino.flutter && flutter pub add flutter_secure_storage
```

- [ ] **Step 2: Write `TelegramAuthFlow`** — POST to `{base}/api/telegram/auth/session` with `{ "initData": "..." }`, receive `{ "accessToken": "...", "refreshToken": "...", "userId": 12345 }`, persist in `flutter_secure_storage`.

- [ ] **Step 3: Write widget-level test** using `mocktail` for the HTTP client.

- [ ] **Step 4: Commit.** `feat(flutter): TelegramAuthFlow + secure session storage`

## Task 4.3: Flutter — gRPC JWT bearer interceptor

**Files:**
- Modify: `clients/ino.flutter/lib/grpc/ino_client.dart`

- [ ] **Step 1:** Add a `CallOptions` interceptor that pulls the access token from `SessionStorage` and attaches `authorization: Bearer <token>` to every gRPC call.

- [ ] **Step 2:** Bloc test asserting the header is present.

- [ ] **Step 3: Commit.** `feat(flutter): attach JWT bearer to gRPC CallOptions`

## Task 4.4: ino.Bot — `TelegramSessionEndpoint` (if not already wired to existing `AuthSessionSyncHandler`)

**Files:**
- Modify: `domains/travel/TripRadar/src/ino.Bot/Auth/` (where `AuthSessionSyncHandler.cs` lives)

- [ ] **Step 1:** Verify the existing `POST /api/telegram/auth/session` endpoint in `TripRadar.Bot/AuthSessionSyncHandler.cs` accepts the Flutter payload shape. If not, add an `InitDataAuthRequest { string InitData }` variant endpoint that calls `TelegramLoginCommand(TelegramAuthDataDTO.FromRawInitData(initData))`.

- [ ] **Step 2:** Write integration test: POST with a valid test initData (signed with the dev bot token), expect JWT + userId back.

- [ ] **Step 3: Commit.** `feat(auth): Telegram session endpoint accepts raw initData`

## Task 4.5: ino silo — `AuthToolAdapter` (implements `IAuthVault` grain contract for scripts)

**Files:**
- Create: `src/Core/Auth/IAuthVault.cs` (grain interface)
- Create: `src/Core/Auth/AuthVaultGrain.cs` (cache-over-MediatR)

- [ ] **Step 1: Write failing test** — grain `GetTokenAsync("google")` for user with no token returns null; store via `StoreTokenAsync(...)` persists to DB via `IMediator.Send(StoreOAuthTokenCommand(...))`; subsequent `GetTokenAsync` within the same activation returns from local cache.

- [ ] **Step 2: Write interface + implementation**

```csharp
public interface IAuthVault : IGrainWithStringKey
{
    Task<OAuthToken?> GetTokenAsync(string service, CancellationToken ct = default);
    Task StoreTokenAsync(OAuthToken token, CancellationToken ct = default);
    Task RemoveTokenAsync(string service, CancellationToken ct = default);
}

public sealed class AuthVaultGrain(IMediator mediator) : Grain, IAuthVault
{
    readonly Dictionary<string, OAuthToken> _cache = new();
    long _userId = 0;

    public override Task OnActivateAsync(CancellationToken ct)
    {
        _userId = long.Parse(this.GetPrimaryKeyString());
        return base.OnActivateAsync(ct);
    }

    public async Task<OAuthToken?> GetTokenAsync(string service, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(service, out var cached) && !cached.IsExpired())
            return cached;

        var token = await mediator.Send(new GetOAuthTokenQuery(_userId, service), ct);
        if (token is not null) _cache[service] = token;
        return token;
    }

    public async Task StoreTokenAsync(OAuthToken token, CancellationToken ct = default)
    {
        await mediator.Send(new StoreOAuthTokenCommand(_userId, token), ct);
        _cache[token.Service] = token;
    }

    public async Task RemoveTokenAsync(string service, CancellationToken ct = default)
    {
        await mediator.Send(new RevokeOAuthTokenCommand(_userId, service), ct);
        _cache.Remove(service);
    }
}
```

- [ ] **Step 3: Run test → pass.** **Step 4: Commit.** `feat(auth): AuthVaultGrain — cache-over-MediatR-backed by TripRadar`

## Task 4.6: `GoogleOAuthInitiator` neuron/service

**Files:**
- Create: `src/Core/Auth/GoogleOAuthInitiator.cs`
- Create: `src/Core/Auth/IOAuthStateStore.cs` (Redis-backed)
- Create: `src/Core/Auth/RedisOAuthStateStore.cs`

- [ ] **Step 1: Context7** — `google.apis.auth` → "OAuth 2.0 authorization code flow with PKCE S256"

- [ ] **Step 2: Write failing test for `GoogleOAuthInitiator.StartFlowAsync(userId, scopes, returnTo)`** — generates a state GUID, stores state object in Redis with 10-min TTL, returns Google auth URL containing the state + PKCE challenge.

- [ ] **Step 3: Write `GoogleOAuthInitiator`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Ino.Core.Auth;

public sealed class GoogleOAuthInitiator(IOAuthStateStore states, IConfiguration config)
{
    public async Task<string> StartFlowAsync(long userId, IReadOnlyList<string> scopes, string returnTo, CancellationToken ct = default)
    {
        var state = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = GeneratePkcePair();

        await states.StoreAsync(state,
            new OAuthStateEntry(userId, "google", scopes, returnTo, verifier, DateTimeOffset.UtcNow.AddMinutes(10)),
            ct);

        var clientId = config["GOOGLE:CLIENT_ID"] ?? throw new InvalidOperationException("GOOGLE:CLIENT_ID not set");
        var redirect = config["GOOGLE:REDIRECT_URI"] ?? throw new InvalidOperationException("GOOGLE:REDIRECT_URI not set");

        var query = new Dictionary<string, string>
        {
            ["client_id"]             = clientId,
            ["redirect_uri"]          = redirect,
            ["response_type"]         = "code",
            ["scope"]                 = string.Join(' ', scopes),
            ["state"]                 = state,
            ["code_challenge"]        = challenge,
            ["code_challenge_method"] = "S256",
            ["access_type"]           = "offline",
            ["prompt"]                = "consent",
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" +
            string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    static (string verifier, string challenge) GeneratePkcePair()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: `RedisOAuthStateStore`** uses `StackExchange.Redis` — serialize to JSON, TTL 10 min.

- [ ] **Step 5: Run test → pass.** **Step 6: Commit.** `feat(auth): GoogleOAuthInitiator with PKCE S256 + Redis state`

## Task 4.7: `GoogleCallbackEndpoint` on ino.Bot

**Files:**
- Create: `domains/travel/TripRadar/src/ino.Bot/Auth/GoogleCallbackEndpoint.cs`

- [ ] **Step 1: Write integration test** — POST a mock Google response to `/api/auth/google/callback?code=test-code&state=known-state`; expect a row in `UserOAuthTokens` + a Telegram bot message to the user + a 200 response.

- [ ] **Step 2: Implement endpoint**

```csharp
// GoogleCallbackEndpoint.cs
public static class GoogleCallbackEndpoint
{
    public static IEndpointRouteBuilder MapGoogleOAuthCallback(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/google/callback", async (
            [FromQuery] string code,
            [FromQuery] string state,
            IOAuthStateStore states,
            IMediator mediator,
            ITelegramBotClient bot,
            IHttpClientFactory httpClients,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var entry = await states.TakeAsync(state, ct);
            if (entry is null || entry.ExpiresAt < DateTimeOffset.UtcNow)
                return Results.BadRequest("Invalid or expired state.");

            // Exchange code for tokens
            var http = httpClients.CreateClient();
            var response = await http.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"]          = code,
                    ["client_id"]     = config["GOOGLE:CLIENT_ID"]!,
                    ["client_secret"] = config["GOOGLE:CLIENT_SECRET"]!,
                    ["redirect_uri"]  = config["GOOGLE:REDIRECT_URI"]!,
                    ["grant_type"]    = "authorization_code",
                    ["code_verifier"] = entry.CodeVerifier,
                }), ct);

            if (!response.IsSuccessStatusCode)
                return Results.BadRequest(await response.Content.ReadAsStringAsync(ct));

            var payload = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty token response");

            await mediator.Send(new StoreOAuthTokenCommand(entry.UserId,
                new OAuthToken("google",
                    AccessToken: payload.AccessToken,
                    RefreshToken: payload.RefreshToken,
                    Scopes: string.Join(',', entry.Scopes),
                    ExpiresAt: DateTime.UtcNow.AddSeconds(payload.ExpiresIn))), ct);

            // Send rejoin message
            var botUrl = config["INO_BOT_PUBLIC_URL"];
            var rejoinUrl = $"{botUrl}/?q={Uri.EscapeDataString(entry.ReturnTo)}";
            await bot.SendTextMessageAsync(
                entry.UserId,
                "✅ Google connected. Tap below to continue.",
                replyMarkup: new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithWebApp("Open ino", new WebAppInfo { Url = rejoinUrl })),
                cancellationToken: ct);

            return Results.Ok();
        });
        return app;
    }

    sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn,
        [property: JsonPropertyName("token_type")]    string TokenType);
}
```

- [ ] **Step 3: Wire `MapGoogleOAuthCallback()` in `ino.Bot/Program.cs`.**

- [ ] **Step 4: Run integration test → pass.**

- [ ] **Step 5: Commit.** `feat(auth): GoogleCallbackEndpoint — code exchange + Store + Telegram rejoin`

## Task 4.8: `UberOAuthInitiator` + `UberMockCallbackEndpoint`

**Files:**
- Create: `src/Core/Auth/UberOAuthInitiator.cs` (real URL, real state, real redirect)
- Create: `domains/travel/TripRadar/src/ino.Bot/Auth/UberMockCallbackEndpoint.cs`

**Same structure as Google, except:**
- Uber URL: `https://login.uber.com/oauth/v2/authorize`
- Token exchange: for the demo, POST to a local `/api/auth/uber/mock-exchange` endpoint that returns a canned token JSON shaped like Uber's real response.

- [ ] **Step 1: Write initiator** (copy from `GoogleOAuthInitiator`, change URL/scopes).
- [ ] **Step 2: Write `UberMockCallbackEndpoint`** — dispatches `StoreOAuthTokenCommand` with a canned token.
- [ ] **Step 3: Write `/api/auth/uber/mock-exchange`** — returns JSON with a hardcoded `access_token`/`expires_in`.
- [ ] **Step 4: Test.** **Step 5: Commit.** `feat(auth): Uber OAuth initiator + mock token exchange`

## Task 4.9: `AuthRequestCardTemplate` RFW template

**Files:**
- Create: `src/Core/Rfw/Templates/AuthRequestCardTemplate.cs`

This is the shared template rendered whenever a neuron returns `SynapseResult.AuthRequired(service, scopes)`. The template takes `service` + `scopes` as data, builds an RFW library description for the card, and returns `(byte[] desc, byte[] data)`.

- [ ] **Step 1: Write failing test** — `AuthRequestCardTemplate.Build("google", ["calendar"], returnTo: "brief me")` returns non-empty RFW bytes; Flutter RFW parser can parse them (round-trip via `parseLibraryFile`).

- [ ] **Step 2: Write the template** — follow the pattern from `domains/travel/Ino.Travel/UI/FlightCardTemplate.cs` (existing). Build an RFW library source string for the card layout (icon, title, subtitle, CTA button that fires `start_oauth` synapse), serialize data as JSON.

Reference the Flutter widget mockup from spec section 4.3 for the visual target.

- [ ] **Step 3: Test → pass.** **Step 4: Commit.** `feat(rfw): AuthRequestCardTemplate (shared across all services)`

## Task 4.10: Wire `NeuronGrain` to invoke `AuthRequestCardTemplate` on `AuthRequired`

**Files:**
- Modify: `src/Core/Neurons/NeuronGrain.cs`

- [ ] **Step 1:** After `ExecuteScriptAsync` returns a `SynapseResult` with `Verb == "auth_required"`, if `RfwDescription` is null, auto-populate it via `AuthRequestCardTemplate.Build(result.Service!, result.Scopes!, returnTo: synapse.Payload)`.

- [ ] **Step 2: Unit test:** a neuron returning `SynapseResult.AuthRequired("google", [...])` ends up with populated RFW bytes via the shared template.

- [ ] **Step 3: Commit.** `feat(core): NeuronGrain auto-renders AuthRequestCard on AuthRequired`

## Task 4.11: E2E test — `BriefMeScenario` (full flow through stubbed Google)

**Files:**
- Create: `tests/E2E.Tests/Auth/BriefMeScenarioTest.cs`

- [ ] **Step 1: Test script:** User fires `brief me on today` via gRPC → BriefingNeuron (scaffolded stub for now, full impl in Phase 5) → returns AuthRequired(google, ...). Simulate the Google callback via `POST /api/auth/google/callback` with a mocked Google token response (DI-swap `IHttpClientFactory` with a handler that returns the canned payload). Re-fire `brief me on today` → BriefingNeuron sees the token → returns a BriefingCard (with stub data).

- [ ] **Step 2: Run → fail (BriefingNeuron stub returns fixed AuthRequired both times, doesn't re-check the vault).** Add minimal `BriefingNeuron` stub that actually checks the vault.

- [ ] **Step 3: Run → pass.**

- [ ] **Step 4: Commit.** `test(e2e): BriefMeScenario — Google OAuth cascade end-to-end with mocks`

## Task 4.12: E2E test — `RideHomeScenario` (UberMock cascade)

Same structure as 4.11 but for Uber. Same file location.

- [ ] **Step 1–3: standard test-first cycle.**
- [ ] **Step 4: Commit.** `test(e2e): RideHomeScenario — Uber mock cascade end-to-end`

## Task 4.13: Phase 4 checkpoint

- [ ] **Step 1:** `dotnet test ino.slnx --filter "FullyQualifiedName~Auth"` — all auth tests green.
- [ ] **Step 2:** `aspire start`, drive a real Telegram session: open the cloudflared URL in a browser simulating initData, follow the Google OAuth link to completion, verify a row appears in `UserOAuthTokens`.
- [ ] **Step 3:** Proceed to Phase 5.

---

# Phase 5 — Demo neurons + Flutter UX

**Goal:** Build the five demo neurons, rewrite `HomeScreen`, integrate Rive, ship all RFW templates, and wire the evolution trigger.

**Duration:** ~1.5 days. Depends on Phase 4.

## Task 5.1: `BriefingNeuron`

**Files:**
- Create: `src/Neurons/Briefing/BriefingNeuron.cs`
- Create: `src/Neurons/Briefing/BriefingCard.rfw` (embedded resource)

- [ ] **Step 1: Write the Agent<T> subclass**

```csharp
[LLMAgent("briefing",
    "Gives a morning/afternoon/evening summary: weather, next events, urgent emails. Uses Google Calendar and Gmail.",
    Model = "gpt-54-mini")]
[RfwCard("BriefingCard.rfw")]
public class BriefingNeuron : Agent<IBriefing>
{
    // The six facets are derived at reflection time by AgentToNeuronAdapter.
    // ScriptSource is null — typed methods handle dispatch.
    // ToolRefs pulled from [ToolRef] attributes on the class.
    [ToolRef("IAuthVault")]
    [ToolRef("IGoogleCalendar")]
    [ToolRef("IGoogleGmail")]
    [ToolRef("IWeather")]
    public override IEnumerable<Tool> DefineTools() => [];  // typed-dispatch only

    public async Task<BriefingResult> BriefAsync(string timeOfDay, string userId)
    {
        var vault = Grains.GetGrain<IAuthVault>(userId);
        var token = await vault.GetTokenAsync("google");
        if (token is null) return BriefingResult.NeedsAuth;

        var calendar = Grains.GetGrain<IGoogleCalendar>(userId);
        var events = await calendar.GetUpcomingAsync(token, 3);

        var gmail = Grains.GetGrain<IGoogleGmail>(userId);
        var unreadCount = await gmail.CountUnreadAsync(token);

        var weather = Grains.GetGrain<IWeather>(userId);
        var current = await weather.CurrentAsync();

        return new BriefingResult(current, events, unreadCount);
    }
}
```

- [ ] **Step 2:** Create the RFW template file `BriefingCard.rfw` (embedded resource, loaded by `AgentToNeuronAdapter.LoadEmbeddedResource`).

- [ ] **Step 3: Test:** unit test `BriefAsync` with mocked grains; integration test via `ChatAsync("brief me on today")` in the E2E fixture.

- [ ] **Step 4: Commit.** `feat(neuron): BriefingNeuron (six-facet compile-time)`

## Task 5.2: `GoogleCalendarNeuron`, `GoogleGmailNeuron`, `WeatherNeuron`

Each is a thin wrapper around the respective HTTP API. For the demo, stub implementations returning canned data are acceptable — the important thing is the typed grain interface so `BriefingNeuron` compiles.

- [ ] **Step 1 (per neuron):** Define interface (`IGoogleCalendar`, `IGoogleGmail`, `IWeather`).
- [ ] **Step 2:** Implement grain that either calls the real API (if `token` passed in) or returns canned data (dev mode).
- [ ] **Step 3:** Unit test.
- [ ] **Step 4:** Commit per neuron.

## Task 5.3: `UberMockNeuron` + `NeedsEvolution` trigger for `home_resolver`

**Files:**
- Create: `src/Neurons/Integrations/UberMockNeuron.cs`
- Create: `src/Neurons/Locations/HomeResolverSeed.cs` (the evolution prompt template)

- [ ] **Step 1:** Define `IUberRide` interface with `GetRideEstimateAsync(from, to, userId)`.

- [ ] **Step 2: `UberMockNeuron` implements it.** In the script body (following the spec section "How `home_resolver` evolution is triggered"):

```csharp
public async Task<SynapseResult> HandleAsync(Synapse synapse, ...)
{
    var vault = Grains.GetGrain<IAuthVault>(synapse.UserId);
    var token = await vault.GetTokenAsync("uber");
    if (token is null)
        return SynapseResult.AuthRequired("uber", new[] { "profile", "request" });

    var endText = synapse.Args.GetString("destination");
    var resolverId = "home_resolver_" + synapse.UserId;

    string? endCoords = null;
    try
    {
        var resolver = Grains.GetGrain<INeuron>(resolverId);
        var resolved = await resolver.HandleAsync(
            new Synapse { Verb = "resolve", Payload = endText, UserId = synapse.UserId }, ct);
        endCoords = resolved.Success ? resolved.Payload : null;
    }
    catch (NeuronNotFoundException) { endCoords = null; }

    if (endCoords is null)
    {
        return SynapseResult.NeedsEvolution(
            baseId: "home_resolver",
            purpose: "Store and recall a user's saved locations (home, work, gym, ...)",
            hint: $"User wants '{endText}' resolved to coordinates; no resolver exists");
    }

    // Return canned Uber estimate
    return SynapseResult.Ok("ride_estimate", JsonSerializer.Serialize(new
    {
        productId = "uberx",
        duration = 360,
        distance = 2.3,
        price = 4.20,
        currency = "USD",
        from = synapse.Args.GetString("origin"),
        to = endCoords,
    }));
}
```

- [ ] **Step 3:** `HomeResolverSeed.cs` is a static class with a constant `PromptTemplate` string that the `EvolutionHandler` uses when it sees a `EvolutionBlueprintHint(BaseId = "home_resolver")`. The template tells the LLM exactly what `ScriptSource` + `SynapseSchema` + `RfwTemplateSource` to emit.

- [ ] **Step 4:** Unit test: `UberMockNeuron` without `home_resolver` returns `NeedsEvolution`; with a stub `home_resolver` returns `ride_estimate`.

- [ ] **Step 5: Commit.** `feat(neuron): UberMockNeuron + home_resolver evolution trigger`

## Task 5.4: Update `EvolutionHandler` for per-user ID scoping + blueprint hints

**Files:**
- Modify: `src/Core/Neurons/Specialists/EvolutionHandler.cs`

- [ ] **Step 1: Write failing test** — `HandleWithHintAsync(synapse, grains, hint, ct)` where `hint.BaseId == "home_resolver"` creates a neuron with id `home_resolver_{synapse.UserId}`, uses the `HomeResolverSeed.PromptTemplate` for the LLM prompt, and re-fires the synapse at the new neuron.

- [ ] **Step 2: Update `EvolutionHandler`:**
  - Add `HandleWithHintAsync(Synapse synapse, IGrainFactory grains, EvolutionBlueprintHint hint, CancellationToken ct)` method.
  - If `hint.BaseId` matches a known seed (e.g. `home_resolver`), use the seed's template; otherwise use the general catch-all prompt.
  - After LLM emits a blueprint, prefix the ID with the user ID: `{hint.BaseId}_{synapse.UserId}`.
  - Register, activate, re-fire.

- [ ] **Step 3: Run test → pass.**

- [ ] **Step 4: Commit.** `feat(evolution): per-user ID scoping + blueprint hint routing`

## Task 5.5: `LocationPromptCard` + `SelfImprovementMicroCard` + `RideCard` RFW templates

Three small templates, each following the pattern from `AuthRequestCardTemplate` (Task 4.9) and the existing `FlightCardTemplate` in `domains/travel/Ino.Travel/UI/`.

- [ ] **Step 1–3 per template:** test first, build, commit.

## Task 5.6: Flutter — `HomeScreen` rewrite

**Files:**
- Modify: `clients/ino.flutter/lib/screens/home/home_screen.dart` (massive rewrite)
- Create: `clients/ino.flutter/lib/screens/home/card_zone.dart`
- Create: `clients/ino.flutter/lib/screens/home/empty_state.dart`
- Create: `clients/ino.flutter/lib/screens/home/micro_card.dart`
- Create: `clients/ino.flutter/lib/screens/chat_history/chat_history_screen.dart`

- [ ] **Step 1:** Extract the existing chat-scroll widgets (`_ChatBubble`, `_RfwContent`, `_ResultCards`) from `home_screen.dart` and move them verbatim to `chat_history_screen.dart`. Register the new route in `app.dart`: `GoRoute(path: '/chat-history', ...)`.

- [ ] **Step 2: Rewrite `home_screen.dart`** to the persona-top + card-zone layout (spec section 4.1). The body becomes:

```dart
Scaffold(
  backgroundColor: Colors.black,
  appBar: AppBar(
    backgroundColor: Colors.transparent,
    actions: [
      IconButton(icon: Icon(Icons.history), onPressed: () => context.push('/chat-history')),
      IconButton(icon: Icon(Icons.hub), onPressed: () => context.push('/brain')),
      IconButton(icon: Icon(Icons.extension), onPressed: () => context.push('/skills')),
    ],
  ),
  body: Column(
    children: [
      SizedBox(height: MediaQuery.of(context).size.height * 0.33, child: PersonaZone()),
      Expanded(child: CardZone()),
      InputBar(controller: _inputController, onSend: _sendMessage, onMicToggle: _toggleRecording),
    ],
  ),
);
```

- [ ] **Step 3: `CardZone` widget** — `BlocBuilder<InoBloc>` switching on `state.activeCard` via `AnimatedSwitcher`. Cases: `EmptyCard`, `AuthRequestCard` (RFW), `BriefingCard` (RFW), `RideCard` (RFW), `LocationPromptCard` (RFW), `MicroCard` (inline).

- [ ] **Step 4:** Delete `clients/ino.flutter/lib/screens/onboarding/onboarding_screen.dart` and its route.

- [ ] **Step 5: Build and widget-test.** `flutter test`.

- [ ] **Step 6: Commit.** `feat(flutter): HomeScreen rewrite — persona-top + card-zone layout`

## Task 5.7: Flutter — `PersonaZone` widget + Rive integration

**Files:**
- Modify: `clients/ino.flutter/lib/persona/persona_widget.dart`
- Create: `clients/ino.flutter/lib/persona/persona_zone.dart`

- [ ] **Step 1:** Drop the `ino_persona.riv` from Task 0.2 at `clients/ino.flutter/assets/rive/ino_persona.riv`. Verify `pubspec.yaml` assets list already includes `assets/rive/`.

- [ ] **Step 2:** Write `PersonaZone` that wraps a `RiveAnimation.asset('assets/rive/ino_persona.riv')` with a `StateMachineController` bound to the `persona` state machine. Expose inputs:

```dart
late final SMIEnum _emotion;
late final SMITrigger _pulse;
late final SMINumber _energy;

void _onRiveInit(Artboard artboard) {
  final controller = StateMachineController.fromArtboard(artboard, 'persona')!;
  artboard.addController(controller);
  _emotion = controller.findInput<bool>('emotion') as SMIEnum;
  _pulse   = controller.findInput<bool>('pulse')   as SMITrigger;
  _energy  = controller.findInput<double>('energy') as SMINumber;
}
```

- [ ] **Step 3:** `BlocListener<PersonaBloc>` → map `state.emotion` to the enum value, call `_pulse.fire()` on `TimelineBloc` signal count change, set `_energy.value = state.energy`.

- [ ] **Step 4: Fallback** — in `persona_widget.dart`, if the Rive asset fails to load (catch exception), fall back to the existing `_PersonaPainter` CustomPaint body (lines 61-106 of the current file). Remove the `_RivePlaceholder` stub (the spinner).

- [ ] **Step 5: Widget test** — PersonaZone builds with a mocked Rive that calls `onInit`; verify controller inputs are accessed.

- [ ] **Step 6: Commit.** `feat(flutter): PersonaZone with real Rive state machine + CustomPaint fallback`

## Task 5.8: Flutter — `activeCard` state field in `InoBloc`

**Files:**
- Modify: `clients/ino.flutter/lib/state/ino_bloc.dart`

- [ ] **Step 1: Add a sealed class hierarchy:**

```dart
sealed class ActiveCard { const ActiveCard(); }
class EmptyCard extends ActiveCard { const EmptyCard(); }
class AuthRequestCardState extends ActiveCard {
  final String service;
  final List<String> scopes;
  final String returnTo;
  final Uint8List rfwDescription;
  final Uint8List rfwData;
  const AuthRequestCardState({...});
}
class RfwCard extends ActiveCard {
  final String domainKey;
  final Uint8List rfwDescription;
  final Uint8List rfwData;
  const RfwCard({...});
}
class MicroCardState extends ActiveCard {
  final String text;
  final IconData icon;
  final Color tint;
  const MicroCardState({...});
}
```

- [ ] **Step 2: Add `activeCard` to `InoBlocState`.** Handlers:
  - On `ChatResponse` with `rfw_description` → emit `RfwCard` or `AuthRequestCardState`
  - On `StreamEvents(kind=="SelfImprovementL1")` → emit `MicroCardState` transiently (3s, then back to previous)
  - On idle → `EmptyCard`

- [ ] **Step 3: Bloc test.**

- [ ] **Step 4: Commit.** `feat(flutter): InoBloc.activeCard state field + transitions`

## Task 5.9: Flutter — Brain View L1 animation

**Files:**
- Modify: `clients/ino.flutter/lib/screens/brain/brain_view_screen.dart`
- Modify: `clients/ino.flutter/lib/ui/components/neural_map.dart`

- [ ] **Step 1:** Subscribe to `StreamEvents` filtered on `kind == "SelfImprovementL1"`. On event, add a new node to `neural_map.dart`'s internal state with a slide-in animation from an off-screen position to the hashed target.

- [ ] **Step 2:** Pulse trail from the new node to the parent neuron (read from event payload `parentId`).

- [ ] **Step 3:** AppBar brain icon badge (purple dot) appears when a new neuron arrives; clears on navigation to `/brain` or after 10s.

- [ ] **Step 4:** Widget test with a fake stream emitting a SelfImprovementL1 event.

- [ ] **Step 5: Commit.** `feat(flutter): Brain View L1 self-evolve animation + AppBar badge`

## Task 5.10: E2E test — `EvolutionScenario`

**Files:**
- Create: `tests/E2E.Tests/Evolution/EvolutionScenarioTest.cs`

- [ ] **Step 1:** Test script:
  1. Seed `demo_tg_100099` with Google + Uber tokens in the vault.
  2. Chat `"get me a ride home"` → `UberMockNeuron` → `NeedsEvolution` → `EvolutionHandler` → creates `home_resolver_demo_tg_100099`.
  3. Assert `NeuronRegistry.TryGetAsync("home_resolver_demo_tg_100099")` returns the new neuron.
  4. Assert timeline has a `SelfImprovementL1` event.
  5. Chat `"Kyiv, Podil"` routed to `home_resolver_demo_tg_100099` → resolves location.
  6. Chat `"get me a ride home"` again → now returns a `ride_estimate`.

- [ ] **Step 2: Run → fix as needed.** **Step 3: Commit.** `test(e2e): EvolutionScenario — L1 self-evolve end-to-end`

## Task 5.11: Phase 5 checkpoint

- [ ] **Step 1:** `dotnet test ino.slnx` — all green.
- [ ] **Step 2:** `flutter test clients/ino.flutter` — all green.
- [ ] **Step 3:** `flutter build web --no-tree-shake-icons` in `clients/ino.flutter` → output in `build/web`.
- [ ] **Step 4:** `aspire start` → walk the demo manually on the cloudflared URL; confirm every beat plays. Take screenshots.
- [ ] **Step 5:** Proceed to Phase 6.

---

# Phase 6 — Preflight + docs

**Goal:** Ship the `DemoPreflight` harness, rewrite README + CLAUDE.md + website, then final verification.

**Duration:** ~half day.

## Task 6.1: `DemoPreflight` harness

**Files:**
- Create: `tests/E2E.Tests/DemoPreflight.cs`

- [ ] **Step 1:** Copy the harness skeleton from spec section "Demo preflight harness".

- [ ] **Step 2:** Implement helpers:
  - `AssertResource(name).IsHealthy()` via Aspire MCP or HTTP `/health` endpoint
  - `SimulateGoogleCallback(userId, scopes)` — POST to the mock Google exchange endpoint
  - `SimulateUberCallback(userId)` — same for Uber
  - `ClearCorrelation("demo")` — deletes timeline events with the "demo" correlation id
  - `RevokeAllUserTokens(userId)` — dispatches `RevokeOAuthTokenCommand` for "google" + "uber"

- [ ] **Step 3: Run** `dotnet test --filter DemoPreflight tests/E2E.Tests/` with Aspire stack running. Expect green.

- [ ] **Step 4: Commit.** `test(e2e): DemoPreflight harness`

## Task 6.2: README rewrite

**Files:** `README.md` (repo root)

- [ ] **Step 1: Write the new README** per spec section "Docs pass":
  - Hero: screenshot of the Flutter home (persona + briefing card)
  - 60-second demo GIF (recorded from Task 5.11 manual walk)
  - Three primitives callout (neurons, synapses, self-improving loop)
  - Quickstart: `dotnet build ino.slnx && aspire start`
  - Domain packs section listing TripRadar first

- [ ] **Step 2: Delete** all references to the old `iaw/*` paths.

- [ ] **Step 3: Commit.** `docs: rewrite README for prod demo + new src/ layout`

## Task 6.3: CLAUDE.md update

**Files:** `CLAUDE.md` (repo root — leave `domains/travel/TripRadar/CLAUDE.md` alone except for the bot rename note from Task 3.6.c)

- [ ] **Step 1: Update all `iaw/*` → `src/*` paths.**

- [ ] **Step 2: Add a new section "Prod base: TripRadar"** calling out the backbone integration.

- [ ] **Step 3: Add a new section "Auth cascade / OAuth vault"** summarizing the progressive auth model + `UserOAuthTokens` table + AuthRequestCard.

- [ ] **Step 4: Replace known-problem #1** (Synapse rename pending) with "Synapse rename completed 2026-04-12" and cross-reference the commit hash.

- [ ] **Step 5: Update build/test command examples** for the new paths.

- [ ] **Step 6: Commit.** `docs: update CLAUDE.md for src/ layout + TripRadar integration`

## Task 6.4: Website "How it works" rewrite

**Files:** `website/` — VitePress pages for "How it works" / architecture

- [ ] **Step 1:** Rewrite the "How it works" page to describe the six-facet Neuron + progressive auth cascade instead of the three-primitives-only framing.

- [ ] **Step 2:** Update the Brain View / Genesis growth animation to reflect the real L1 flow (not a decorative animation).

- [ ] **Step 3:** Local preview via `npm run dev` in `website/`, verify the pages render.

- [ ] **Step 4: Commit.** `docs(website): How it works — six-facet Neuron + auth cascade`

## Task 6.5: Superseded-banner pass

**Files:**
- Modify: `docs/superpowers/plans/2026-04-12-ino-prod-integration.md`
- Modify: `docs/superpowers/specs/2026-04-11-ino-200-domains-persona-design.md` (partial — only the auth-cascade section)

- [ ] **Step 1:** Add a banner at the top of each:

```markdown
> **⚠ SUPERSEDED** by `docs/superpowers/specs/2026-04-12-ino-prod-demo-design.md` and its plan at `docs/superpowers/plans/2026-04-12-ino-prod-demo.md`. This document is retained for historical context.
```

- [ ] **Step 2: Commit.** `docs: superseded banners on obsolete specs`

## Task 6.6: Full-system dry run of the live demo

- [ ] **Step 1:** `aspire start` — every resource Healthy.

- [ ] **Step 2:** `dotnet test --filter DemoPreflight tests/E2E.Tests/` → green.

- [ ] **Step 3:** Open the Telegram bot's `/app` menu button → miniapp loads.

- [ ] **Step 4:** Walk the 60-second storyboard from the spec (brief → Google → Uber → evolve → location → ride).

- [ ] **Step 5:** Verify Aspire traces show the full chain: Flutter → TripRadar.Bot → InoService → NeuronGrain → MediatR → DB. Verify `SelfImprovementL1` event in the timeline.

- [ ] **Step 6:** Record the final demo gif for the README.

## Task 6.7: Final commit + phase 6 checkpoint

- [ ] **Step 1:** `dotnet build ino.slnx && dotnet test ino.slnx` — clean, all green.
- [ ] **Step 2:** `flutter test clients/ino.flutter` — clean.
- [ ] **Step 3:** Spec's acceptance criteria (7 items in section "Acceptance criteria") — walk through each and tick.
- [ ] **Step 4:** Final commit: `chore: complete ino-prod-demo phase 6 checkpoint — all acceptance criteria met`

---

# Self-Review (done during plan authoring)

## Spec coverage

- [x] 60-second demo storyboard → Phase 5 Task 5.10 (EvolutionScenario) + Task 6.6 (manual walk)
- [x] Architecture decision: Full scope → covered by phases 1-6
- [x] Identity + auth: Progressive → Phase 4 Task 4.4 (TelegramSessionEndpoint) + Task 4.1/4.2 (Flutter interop)
- [x] Neuron shape: Six-facet → Phase 2 Tasks 2.1-2.10
- [x] Solution restructure: Prod layout → Phase 3 Tasks 3.1-3.7
- [x] TripRadar prod backbone: `UserOAuthTokens` + MediatR → Phase 1 Tasks 1.1-1.8
- [x] `WithInoFrontend` extension → Phase 3 Task 3.4 Step 4
- [x] Three-phase bot consolidation → Phase 3 Task 3.6 a/b/c
- [x] OAuthVaultGrain pivot to cache-over-DB → Phase 4 Task 4.5
- [x] Google OAuth with PKCE → Phase 4 Tasks 4.6-4.7
- [x] UberMock cascade → Phase 4 Task 4.8
- [x] AuthRequestCard RFW shared template → Phase 4 Task 4.9
- [x] BriefingNeuron + GoogleCalendarNeuron + GoogleGmailNeuron + UberMockNeuron → Phase 5 Tasks 5.1-5.3
- [x] `home_resolver` per-user evolution → Phase 5 Tasks 5.3-5.4
- [x] SynapseResult.NeedsEvolution factory → Phase 2 Task 2.2
- [x] Per-user neuron ID scoping → Phase 5 Task 5.4
- [x] HomeScreen rewrite (PersonaZone + CardZone) → Phase 5 Task 5.6
- [x] Rive persona state machine → Phase 5 Task 5.7 (+ Task 0.2 for asset)
- [x] RFW templates: Auth/Briefing/Ride/LocationPrompt/SelfImprovementMicro → Phase 4 Task 4.9 + Phase 5 Task 5.5
- [x] Brain View L1 animation → Phase 5 Task 5.9
- [x] E2E tests: BriefMe / RideHome / Evolution → Phase 4 Tasks 4.11/4.12 + Phase 5 Task 5.10
- [x] Demo preflight harness → Phase 6 Task 6.1
- [x] README + CLAUDE.md + website docs → Phase 6 Tasks 6.2-6.4

## Placeholder scan

- No "TBD" / "TODO" / "implement later" in the plan.
- A few tasks intentionally reference earlier tasks by number for code templates (e.g. Task 5.5 "follows the pattern from Task 4.9"). This is the "repeat the code — the engineer may be reading tasks out of order" red flag from the writing-plans guide. **Fix pending:** when an engineer reaches Task 5.5 without having read Task 4.9, they need the template-writing pattern inline. For the demo-scope plan this is a minor risk — the tasks are short and building templates uses the same `Ino.Rfw` builder API for all of them. If executed subagent-driven, each task's subagent will read the spec's section 4.3 "RFW templates shipped this iteration" table which contains the concrete shape per template.

## Type / signature consistency

- `SynapseResult.AuthRequired(service, scopes)` — signature is `(string, IReadOnlyList<string>)` in Task 2.2 and used identically in Task 4.10 (`result.Service!, result.Scopes!`), Task 4.11 (`SynapseResult.AuthRequired("google", ...)`), Task 5.3 (`SynapseResult.AuthRequired("uber", new[] { "profile", "request" })`). Consistent.
- `SynapseResult.NeedsEvolution(baseId, purpose, hint)` — signature `(string, string, string)` in Task 2.2 and used in Task 5.3 identically. Consistent.
- `OAuthToken(Service, AccessToken, RefreshToken?, Scopes, ExpiresAt)` — positional record in Task 1.3, used identically in 1.4/1.5/1.8/4.7/4.8. Consistent.
- `IAuthVault.GetTokenAsync(service, ct)` — signature in Task 4.5, used in Task 5.1 (`vault.GetTokenAsync("google")`) and Task 5.3 (`vault.GetTokenAsync("uber")`). Consistent.
- `ToolFacade.Get<T>(key)` — Task 2.3 defines `T : IGrainWithStringKey`. All `Tools.Xxx` usages in script examples pass type via reflection so the constraint holds.
- `SynapseResult.Verb == "needs_evolution"` string constant — used in Tasks 2.2 (factory), 2.8 (NeuronGrain dispatch), 5.10 (E2E assertion). Single string, consistent.
- `NeuronGrain.HandleAsync` behavior change — defined in Task 2.7 (run RfwTemplateSource after ScriptSource) and Task 2.8 (NeedsEvolution dispatch) and Task 4.10 (auto AuthRequestCard render). Three separate edits to the same method — sequential and additive, not contradictory. Integration test in 4.10 should cover the three-way interaction.

## Scope check

The plan is 6 phases, ~50 tasks, ~5-6 days of engineering work. It's at the upper bound of "single implementation plan" — but the phases are strictly sequential (4 depends on 1+2+3; 5 depends on 4; 6 depends on 5), so splitting into multiple plans would fragment the acceptance criteria. **Keep as one plan**; execute phase-by-phase with checkpoints.

If execution runs over budget, Phase 5.2 (GoogleCalendar/Gmail/Weather stubs) and Phase 6.4 (website rewrite) are the first candidates for deferral — the demo itself works with stub data in those neurons and the website doc can lag by a few days without affecting acceptance.

---

# Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-12-ino-prod-demo.md`.

**Two execution options:**

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for a restructure of this size because (a) each subagent gets a clean context window for its task, (b) review between tasks catches compile/test regressions before they compound, (c) a batched restructure like Phase 3 benefits from per-batch verification without context pollution from earlier phases.

2. **Inline Execution** — Execute tasks in this session using `executing-plans`, batch execution with checkpoints. Best when you want to watch every step live in one thread.

**Which approach?**
