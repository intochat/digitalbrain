# Brain as a Neuron Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the ambient workspace scoping with an addressable `IBrain` neuron, rename the whole surface (`BrainScope`, `BrainId`, `/brains/{brainId}` routes), and move host endpoint files into their modules.

**Architecture:** The brain id derivation (`workspace-<sha256(owner\0name)>`) is preserved so no stored state migrates. `CallerContext` (already stamped by `AccountSession` middleware onto the Orleans RequestContext) becomes the only source of the current brain; every `WorkspaceScope.Current(auth, ws)` call site and every `IOptions<BasicAuthOptions>` endpoint parameter is deleted. The HTTP membership filter is applied once per route group through a shared `BrainRoutes.Group(...)` helper. Endpoint files leave the IntoChat host for their modules in the same pass that renames them.

**Tech Stack:** .NET 11 / Orleans grains, ASP.NET Core minimal APIs, xUnit (`UnitTest.Create().WithModule<...>()`), Flutter/Dart shell.

**Spec:** `docs/superpowers/specs/2026-09-30-brain-as-neuron-design.md`

## Global Constraints

- Grain key derivation stays byte-identical: `"workspace-" + Convert.ToHexStringLower(SHA256(owner + "\0" + name))`. Never change it; never rename the stored prefix.
- Persisted records: rename C# property names only; `[Id(n)]` slots never renumber. Concrete arrays, not interface lists, in grain state.
- Wire-shape changes in C# must be mirrored in the Dart shell **in the same task**.
- No boilerplate `/// <summary>`; names carry meaning; comments only for what code cannot say.
- Build/test per project (`dotnet test src/<path>`), never the `.slnx`. `src/Applications/IntoChat/Tests/Unit` has 5 known pre-existing failures (HostedDeployment ×2, PathTruth, ModuleConfigurationContract ×2) — not yours.
- Flutter checks: `flutter analyze` and `flutter test` from `src/Modules/Google/Flutter/app/shell`.
- Flutter-module window-layout types (`IWorkspace`, `WorkspaceNeuron`, `WorkspaceStore`, `WorkspaceApp`, `WorkspacePersistence`…) are EXEMPT from the rename — they are the UI concept and keep their names.
- Final smoke: `aspire run` from `src/Applications/IntoChat/AppHost`, all resources Healthy. Skip the 18-minute IntoChat E2E.

## Review Focus

1. **Unstamped caller reaching a brain route** → must be 401 from the group filter, never a 500 from `BrainScope.CurrentId()` throwing. Pinned in Task 3.
2. **Brain id with path separators or control chars in the route** (`/brains/..%2Fx/apps`) → 400 before any grain call. Pinned in Task 3.
3. **Authenticated non-member hitting another user's brain** → 403 from the single group filter even though the endpoint itself no longer checks. Pinned in Task 5.
4. **`POST /agent` carries brainId in the body, not the route** → membership must still be decided (`BrainAccessFilter.Decide`), and the stamped context must scope to the *body's* brain, not the route default. Pinned in Task 9.
5. **Old `/workspaces/...` URL after the rename** → 404 (no silent alias); the Dart shell must ship the new paths in the same change so the packaged app never emits old routes. Pinned in Task 10 (Dart tests run against renamed paths only).

---

### Task 1: `BrainScope` in the kernel

**Files:**
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain/Enforcement/BrainScope.cs`
- Delete: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/WorkspaceScope.cs`
- Test: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Tests.Unit/Enforcement/BrainScopeFacts.cs` (create; put beside existing kernel unit tests — find the project with `Glob src/Modules/DigitalBrain/Kernel/*Tests.Unit*/*.csproj`; if the kernel has no unit test project, put the facts in `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.Unit/BrainScopeFacts.cs`)

**Interfaces:**
- Consumes: `CallerContextStamper.TryGet/Require` (`src/Modules/DigitalBrain/Kernel/DigitalBrain/Enforcement/CallerContextStamper.cs`), `CallerContext.BrainId` (renamed in this task's step 3).
- Produces: `BrainScope` record used by every later task:

```csharp
namespace DigitalBrain.Core.Enforcement;

public sealed record BrainScope(string Id, string Owner, string Name)
{
    public static string CurrentId();                       // stamped caller → Create(caller.AccountId, caller.BrainId).Id; throws InvalidOperationException when unstamped
    public static BrainScope Create(string owner, string name);
    public static bool IsValidId([NotNullWhen(true)] string? value);
}
```

- [ ] **Step 1: Write the failing facts**

```csharp
using DigitalBrain.Core.Enforcement;
using Xunit;

public sealed class BrainScopeFacts
{
    [Fact]
    public void TheDerivationIsStableAndDistinctPerOwnerAndName()
    {
        var id = BrainScope.Create("alice", "one").Id;
        Assert.StartsWith("workspace-", id); // storage artifact, preserved on purpose
        Assert.Equal(id, BrainScope.Create("alice", "one").Id);
        Assert.NotEqual(id, BrainScope.Create("bob", "one").Id);
        Assert.NotEqual(id, BrainScope.Create("alice", "two").Id);
    }

    [Fact]
    public void AnUnstampedCallCannotResolveABrain()
        => Assert.Throws<InvalidOperationException>(() => BrainScope.CurrentId());

    [Fact]
    public void InvalidIdsAreRejected()
    {
        Assert.False(BrainScope.IsValidId(null));
        Assert.False(BrainScope.IsValidId("a/b"));
        Assert.False(BrainScope.IsValidId("a\\b"));
        Assert.False(BrainScope.IsValidId(new string('x', 201)));
        Assert.True(BrainScope.IsValidId("personal"));
    }
}
```

- [ ] **Step 2: Run, verify failure** — `dotnet test <chosen test project> --filter BrainScopeFacts`. Expected: compile error, `BrainScope` not defined.

- [ ] **Step 3: Rename `CallerContext.WorkspaceId` → `BrainId`** in `src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts/Enforcement/CallerContext.cs` (keep its `[Id(n)]` if serialized). Fix all compile errors this causes mechanically (`grep -rl "\.WorkspaceId" src --include=*.cs` — but ONLY the CallerContext member accesses; `Member.WorkspaceId` and `SessionView.WorkspaceId` are renamed in Task 4, so if the same file touches both, rename only the CallerContext use now).

- [ ] **Step 4: Implement `BrainScope`** — move the body of the deleted `WorkspaceScope` into the kernel, dropping the `Current(BasicAuthOptions, ...)` overload:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Core.Enforcement;

public sealed record BrainScope(string Id, string Owner, string Name)
{
    public static string CurrentId()
    {
        var caller = CallerContextStamper.Require();
        return Create(caller.AccountId, caller.BrainId).Id;
    }

    public static BrainScope Create(string owner, string name)
    {
        if (!IsValidId(name))
        { throw new ArgumentException("A brain ID must contain 1–200 characters without path separators.", nameof(name)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        // "workspace-" prefix is a stored artifact of the previous naming; renaming it means a data
        // migration and is explicitly deferred (see the spec).
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(owner + "\0" + name));
        return new("workspace-" + Convert.ToHexStringLower(digest), owner, name);
    }

    // Brain, thread and run ids travel in routes and grain keys: 1-200 characters, no control
    // characters and no path separators.
    public static bool IsValidId([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(c => char.IsControl(c) || c is '/' or '\\');
}
```

If `CallerContextStamper.Require()` does not exist (check `CallerContextStamper.cs`), add it: `public static CallerContext Require() => TryGet(out var caller) ? caller : throw new InvalidOperationException("No caller context is stamped on this call.");`

- [ ] **Step 5: Delete `WorkspaceScope.cs`.** Leave callers broken — Tasks 4–9 fix them file by file. To keep this task green in isolation, add a temporary shim in the Identity module **only if** you want per-task builds: `public static class WorkspaceScope { public static BrainScope Create(string owner, string name) => BrainScope.Create(owner, name); public static bool IsValidId(string? v) => BrainScope.IsValidId(v); }` — and note it MUST be deleted in Task 11. (Callers of `WorkspaceScope.Current` are converted in their own tasks; the shim deliberately omits `Current`.)

- [ ] **Step 6: Run the facts** — expected PASS. Also `dotnet build` the kernel project.

- [ ] **Step 7: Commit** — `git commit -m "Move brain scoping into the kernel as BrainScope resolved from the stamped caller."`

---

### Task 2: `IBrain` neuron

**Files:**
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts/IBrain.cs`
- Create: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/BrainNeuron.cs`
- Modify: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/Directory/` (the `IIdentityDirectory` implementation grain — registration activates the brain)
- Test: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.Unit/BrainFacts.cs`

**Interfaces:**
- Consumes: `INeuron` (`src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts/INeuron.cs`), `BrainScope.Create` (Task 1), `IIdentityDirectory.RegisterAsync`.
- Produces:

```csharp
[Alias("brain"), Orleans.Metadata.DefaultGrainType("brain")]
public interface IBrain : INeuron
{
    Task<BrainSnapshot> Read();
    Task<BrainSnapshot> Establish(EstablishBrain request);   // idempotent; called on registration and brain creation
}

[GenerateSerializer, Alias("brain.snapshot")]
public sealed record BrainSnapshot
{
    [Id(0)] public required string BrainId { get; init; }    // the sha-derived grain key
    [Id(1)] public required string Name { get; init; }       // the human name ("personal")
    [Id(2)] public required string OwnerAccountId { get; init; }
    [Id(3)] public DateTimeOffset EstablishedAt { get; init; }
}

[GenerateSerializer, Alias("brain.establish")]
public sealed record EstablishBrain([property: Id(0)] string Name, [property: Id(1)] string OwnerAccountId);
```

- [ ] **Step 1: Write the failing facts**

```csharp
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

public sealed class BrainFacts
{
    [Fact]
    public async Task RegisteringAMemberEstablishesTheirBrainNeuron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var member = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        var snapshot = await brain.Get<IBrain>(BrainScope.Create(member.AccountId, member.BrainId).Id).Read();
        Assert.Equal(member.BrainId, snapshot.Name);
        Assert.Equal(member.AccountId, snapshot.OwnerAccountId);
    }

    [Fact]
    public async Task EstablishIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var neuron = host.Get<IBrain>(BrainScope.Create("acct", "personal").Id);
        var first = await neuron.Establish(new("personal", "acct"));
        var second = await neuron.Establish(new("personal", "acct"));
        Assert.Equal(first.EstablishedAt, second.EstablishedAt);
    }
}
```

Note: this fact uses `member.BrainId`, which exists after Task 4's `Member` rename. Order tolerance: if executing strictly in order, write the fact with `member.WorkspaceId` and flip it in Task 4's rename sweep — Task 4 lists this file.

- [ ] **Step 2: Run, verify failure** — `dotnet test src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.Unit --filter BrainFacts`. Expected: `IBrain` not defined.

- [ ] **Step 3: Implement the contract** (code above) in `IBrain.cs`.

- [ ] **Step 4: Implement `BrainNeuron`** in the Identity module, following the style of existing neurons (look at `WorkspaceNeuron.cs` in the Flutter module for grain shape):

```csharp
using DigitalBrain.Contracts;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Identity;

[GenerateSerializer, Alias("brain.state")]
public sealed class BrainState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public string OwnerAccountId { get; set; } = "";
    [Id(2)] public DateTimeOffset EstablishedAt { get; set; }
}

internal sealed class BrainNeuron([PersistentState("brain")] IPersistentState<BrainState> state) : Grain, IBrain
{
    public Task<BrainSnapshot> Read() => Task.FromResult(Snapshot());

    public async Task<BrainSnapshot> Establish(EstablishBrain request)
    {
        if (state.State.EstablishedAt == default)
        {
            state.State.Name = request.Name;
            state.State.OwnerAccountId = request.OwnerAccountId;
            state.State.EstablishedAt = DateTimeOffset.UtcNow;
            await state.WriteStateAsync();
        }
        return Snapshot();
    }

    private BrainSnapshot Snapshot() => new()
    {
        BrainId = this.GetPrimaryKeyString(),
        Name = state.State.Name,
        OwnerAccountId = state.State.OwnerAccountId,
        EstablishedAt = state.State.EstablishedAt,
    };
}
```

Match the repo's actual grain base/persistence pattern (open one existing Identity grain first and copy its storage-provider usage exactly).

- [ ] **Step 5: Call `Establish` from registration and brain creation** — in the `IIdentityDirectory` grain's `RegisterAsync` (and the create-workspace path used by `POST /identity/workspaces`), after the member record is written:

```csharp
await GrainFactory.GetGrain<IBrain>(BrainScope.Create(member.AccountId, member.BrainId).Id)
    .Establish(new(member.BrainId, member.AccountId));
```

(Directory grain → different grain: no self-call hazard.)

- [ ] **Step 6: Run BrainFacts + the whole Identity unit suite** — `dotnet test src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.Unit`. Expected: PASS (plus AccountFacts still green).

- [ ] **Step 7: Commit** — `git commit -m "Make the brain an addressable neuron established at registration."`

---

### Task 3: `BrainAccessFilter` and the `BrainRoutes.Group` helper

**Files:**
- Rename: `src/Modules/DigitalBrain/Kernel/DigitalBrain/Enforcement/WorkspaceAccessFilter.cs` → `BrainAccessFilter.cs`
- Rename: `src/Modules/DigitalBrain/Kernel/DigitalBrain/Enforcement/WorkspaceAccess.cs` → `BrainAccess.cs` (`IWorkspaceAccess` → `IBrainAccess`)
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain/Enforcement/BrainRoutes.cs`
- Modify: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/Directory/DirectoryWorkspaceAccess.cs` → `DirectoryBrainAccess.cs`, and its registration in `IdentityModule.Configure(ISiloBuilder)` (`services.TryAddSingleton<IBrainAccess, DirectoryBrainAccess>()`)
- Test: add to the kernel/Identity facts file used in Task 1

**Interfaces:**
- Consumes: `IIdentityDirectory.CanAccessAsync(principal, brainId)`, `CallerContextStamper.TryGet`.
- Produces (every endpoint task below builds on this):

```csharp
namespace DigitalBrain.Core.Enforcement;

public static class BrainRoutes
{
    // The one place brain-scoped HTTP routes are rooted: /brains/{brainId} with route-id
    // validation and the membership filter applied once.
    public static RouteGroupBuilder Group(IEndpointRouteBuilder endpoints, string suffix = "");
}

public static class BrainAccessFilter
{
    public static ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next);
    public static ValueTask<IResult?> Decide(HttpContext http, string brainId); // body-carried case
}

public interface IBrainAccess
{
    ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write the failing facts** (route-level; use the repo's minimal-API test host pattern — look at how `AccountFacts` builds `DefaultHttpContext`, and at any existing endpoint fact for the WebApplication pattern; if none exists, test `BrainAccessFilter.Decide` directly):

```csharp
[Fact]
public async Task AnInvalidBrainIdIsRejectedBeforeAnyGrainCall()
{
    var http = new DefaultHttpContext();
    http.Request.RouteValues["brainId"] = "..%2Fescape/";
    // Group's validation filter answers 400 without needing services.
    var result = await BrainRoutes.ValidateId(http);
    Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result!).StatusCode);
}

[Fact]
public async Task AnUnstampedAuthenticatedRequestIsUnauthorized()
{
    var http = new DefaultHttpContext();
    http.Request.Headers.Authorization = "Basic abc";
    var denied = await BrainAccessFilter.Decide(http, "personal");
    Assert.Equal(StatusCodes.Status401Unauthorized, ((IStatusCodeHttpResult)denied!).StatusCode);
}
```

- [ ] **Step 2: Run, verify failure.**

- [ ] **Step 3: Implement.** Rename the filter (route values `brain`/`brainId` instead of `workspace`/`workspaceId`; keep `RequiresMembership` logic — open posture stays open). Add `BrainRoutes`:

```csharp
public static class BrainRoutes
{
    public static RouteGroupBuilder Group(IEndpointRouteBuilder endpoints, string suffix = "")
    {
        var group = endpoints.MapGroup("/brains/{brainId}" + suffix);
        group.AddEndpointFilter(async (context, next) =>
            await ValidateId(context.HttpContext) is { } invalid ? invalid : await next(context));
        group.AddEndpointFilter(BrainAccessFilter.EnforceAsync);
        return group;
    }

    internal static ValueTask<IResult?> ValidateId(HttpContext http)
        => ValueTask.FromResult<IResult?>(
            BrainScope.IsValidId(http.Request.RouteValues["brainId"] as string) ? null : Results.BadRequest());
}
```

Swap `IWorkspaceAccess` → `IBrainAccess` (parameter `workspaceId` → `brainId`), `DirectoryWorkspaceAccess` → `DirectoryBrainAccess`, update `IdentityModule.Configure`.

- [ ] **Step 4: Run facts + build the kernel and Identity projects.** Expected: PASS; other modules still compile against old names → fix only where the rename forces it (Files/Flutter/host call sites are rewritten properly in Tasks 5–9; here just make them compile by renaming the filter reference, keeping their existing per-endpoint `AddEndpointFilter(BrainAccessFilter.EnforceAsync)` until their own task deletes it).

- [ ] **Step 5: Commit** — `git commit -m "Root brain routes in one validated, membership-filtered group."`

---

### Task 4: Identity surface rename (`Member.BrainId`, `/identity/brains`, claims, `AccountSession`)

**Files:**
- Modify: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Contracts/Accounts.cs`
- Modify: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/IdentityEndpoints.cs`
- Modify: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity/AccountSession.cs`
- Modify: `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.Unit/AccountFacts.cs`, `BrainFacts.cs` (Task 2 note)
- Test: extend `AccountFacts.cs`

**Interfaces:**
- Consumes: `CallerContext.BrainId` (Task 1), `IBrain.Establish` (Task 2).
- Produces: `Member.BrainId` (same `[Id]` slot as the old `WorkspaceId`), `IIdentityDirectory.CanAccessAsync(principal, brainId)` / `ShareBrainAsync(accountId, brainId, principalId, displayName, role, ct)`, route `POST /identity/brains`, claim `intochat.brain`, `SessionView(string PrincipalId, string AccountId, string BrainId, string Role)`.

- [ ] **Step 1: Write the failing fact** (extend AccountFacts):

```csharp
[Fact]
public async Task SharingABrainGrantsAccessOnlyToThatBrain()
{
    var ct = TestContext.Current.CancellationToken;
    await using var host = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
    var directory = host.Get<IIdentityDirectory>(IdentityGrains.Directory);
    var alice = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
    var bob = await directory.RegisterAsync("bob", "another-password", "Bob", ct);
    Assert.False(await directory.CanAccessAsync("bob", alice.BrainId, ct));
    await directory.ShareBrainAsync(alice.AccountId, alice.BrainId, "bob", "Bob", MemberRole.Member, ct);
    Assert.True(await directory.CanAccessAsync("bob", alice.BrainId, ct));
    Assert.False(await directory.CanAccessAsync("bob", bob.BrainId + "-not-shared", ct));
}
```

(`MemberRole` value: use whatever the existing `ShareWorkspaceAsync` tests use — check `CanAccessAnswersOnlyForOwnedOrSharedWorkspaces` in AccountFacts.)

- [ ] **Step 2: Run, verify failure** (`BrainId`/`ShareBrainAsync` not defined).

- [ ] **Step 3: Rename.** In `Accounts.cs`: `Member.WorkspaceId` → `BrainId` keeping its `[Id(n)]`; `ShareWorkspaceAsync` → `ShareBrainAsync`; `CanAccessAsync` parameter `workspaceId` → `brainId`; update the comment ("A principal may reach a brain only when it holds a member record for it…"). In `IdentityEndpoints.cs`: `WorkspaceClaim = "intochat.brain"`; `POST /identity/workspaces` → `POST /identity/brains`; `SessionView.WorkspaceId` → `BrainId`; the create endpoint (`CreateWorkspaceAsync` → `CreateBrainAsync`) calls `IBrain.Establish` after the directory write (mirror Task 2 step 5). In `AccountSession.cs`: `Build` reads route value `"brainId"` (was `"workspaceId"`), assigns `CallerContext.BrainId`; rename local `workspaceId` → `brainId`; `claimWorkspace` → `claimBrain`.

- [ ] **Step 4: Sweep the Identity test files** — `alice.WorkspaceId` → `alice.BrainId` etc.; flip Task 2's BrainFacts note if needed.

- [ ] **Step 5: Run the Identity unit suite** — expected PASS.

- [ ] **Step 6: Commit** — `git commit -m "Rename the identity surface from workspaces to brains."`

---

### Task 5: Files module endpoints on `BrainScope` (first mechanical conversion — the template)

**Files:**
- Modify: `src/Modules/Files/DigitalBrain.Modules.Files/FilesEndpoints.cs`
- Test: the Files module's existing endpoint facts (Glob `src/Modules/Files/*Tests*` — run whatever exists), plus one new access fact below.

**Interfaces:**
- Consumes: `BrainRoutes.Group(endpoints, "/apps")` (Task 3), `BrainScope.CurrentId()` (Task 1).
- Produces: THE conversion recipe every later endpoint task repeats:
  1. Group: `endpoints.MapGroup("/workspaces/{workspaceId}/apps").AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync)` → `BrainRoutes.Group(endpoints, "/apps")`.
  2. Lambda params: delete `string workspaceId` and `IOptions<BasicAuthOptions> auth` from every handler (`{brainId}` stays in the route template for the filter; handlers no longer bind it).
  3. Scope: `WorkspaceScope.Current(auth.Value, workspaceId).Id` (or local `Scope(auth,ws)` helpers) → `BrainScope.CurrentId()`.
  4. Delete the now-unused `using Microsoft.Extensions.Options;` / `DigitalBrain.Identity.Configuration` imports and any private `Scope` helper.

- [ ] **Step 1: Write the failing access fact** (in the Files test project; if the module has no HTTP-level facts, pin the recipe at the unit level instead — assert `FilesEndpoints` maps under `/brains/`):

```csharp
[Fact]
public async Task AnAuthenticatedNonMemberIsForbiddenFromAnotherBrainsFiles()
{
    // Arrange the repo's endpoint-test host with IdentityModule + FilesModule; register alice and bob;
    // authenticate as bob; GET /brains/{alice's brain}/apps/files → 403.
    // Copy the host arrangement from the nearest existing endpoint fact (search: "MapDigitalBrainModules" in tests).
}
```

Write it fully against whatever harness exists — the assertion that matters: bob gets 403, alice gets 200, and neither handler signature mentions auth options.

- [ ] **Step 2: Run, verify failure** (route not found under `/brains/` yet).

- [ ] **Step 3: Apply the recipe** to all 10 handlers in `FilesEndpoints.cs`; delete `private static string Scope(BasicAuthOptions auth, string workspace) => ...` (line 71).

- [ ] **Step 4: Run the Files module suite** — expected PASS.

- [ ] **Step 5: Commit** — `git commit -m "Serve files under the brain route group with caller-resolved scope."`

---

### Task 6: Flutter module `UiHttp` + shell-state routes

**Files:**
- Modify: `src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter/Shared/UiHttp.cs` (two `AddEndpointFilter(WorkspaceAccessFilter...)` sites at :23 and :28)
- Modify: any Flutter-module C# endpoints that carry `/workspaces/{workspaceId}` route templates (search the module: `grep -rn "workspaces/{" src/Modules/Google/Flutter --include=*.cs`)
- Test: `src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.Unit` suite

**Interfaces:**
- Consumes: recipe from Task 5.
- Produces: `UiHttp.MapGet/MapPost` mapping under `BrainRoutes.Group`; window-layout *types* (`IWorkspace`, `WorkspaceNeuron`) untouched.

- [ ] **Step 1: Apply the Task-5 recipe** to `UiHttp` and any in-module route templates. Type names stay; only routes, filters, and scope resolution change.
- [ ] **Step 2: Run** `dotnet test src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.Unit` — expected PASS (fix any fact that hard-codes `/workspaces/`).
- [ ] **Step 3: Commit** — `git commit -m "Route the Flutter module's HTTP surface through the brain group."`

---

### Task 7: CSharp endpoints move into the CSharp module (with sharing)

**Files:**
- Delete: `src/Applications/IntoChat/IntoChat/CSharp/CSharpEndpoints.cs`
- Move: `src/Applications/IntoChat/IntoChat/Packages/CSharpSharing.cs` and `ShareCSharpRequest.cs` → `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Authoring/`
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/CSharpModule.cs` (`Configure(ISiloBuilder)` gains the service registrations; `Configure(IEndpointRouteBuilder)` at :50 gains the routes)
- Modify: `src/Applications/IntoChat/IntoChat/Program.cs` (delete `AddCSharp`/`MapCSharp` calls), `src/Applications/IntoChat/IntoChat/Packages/PackageEndpoints.cs` (delete the `/csharp/{id}/share` map at :59 — it moves)
- Test: the CSharp module unit suite + IntoChat unit suite

**Interfaces:**
- Consumes: `BrainRoutes.Group(endpoints, "/csharp")`, `BrainScope.CurrentId()`, `CSharpToolService.ForScope(string)`, `ScopedCSharpTools`, `WriteCSharpFileRequest` (all already in the module, `Authoring/CSharpTools.cs`).
- Produces: routes `GET|PUT|POST|DELETE /brains/{brainId}/csharp[...]` and `POST /brains/{brainId}/csharp/{id}/share` owned by `CSharpModule`; the developer-mode gate as a module-owned policy:

```csharp
// CSharpModule.cs
public sealed class CSharpAuthoringOptions
{
    public const string SectionName = "DigitalBrain:CSharpAuthoring";
    public bool DeveloperMode { get; set; }
}
```

The host's `IntoChat:DeveloperMode` config key moves to `DigitalBrain:CSharpAuthoring:DeveloperMode` — update `src/Applications/IntoChat/AppHost/appsettings.json` and any launch config that sets the old key (`grep -rn "IntoChat:DeveloperMode\|DeveloperMode" src/Applications/IntoChat --include=*.json --include=*.cs`).

- [ ] **Step 1: Write the failing fact** in the CSharp module tests: mapping the module's endpoints exposes `/brains/{brainId}/csharp` returning `{items, canRun}` when developer mode is on and 404 when off (copy the current filter behavior from `CSharpEndpoints.cs:25-45` — exception mapping ArgumentException→400, KeyNotFoundException→404, InvalidOperationException→409, TimeoutException→504 comes with it).
- [ ] **Step 2: Run, verify failure.**
- [ ] **Step 3: Move the code.** `MapCSharp` body → `CSharpModule.Configure(IEndpointRouteBuilder)` using `BrainRoutes.Group(endpoints, "/csharp")`; the scoped `ScopedCSharpTools` factory → `Configure(ISiloBuilder)` via `silo.Services`, resolving with `BrainScope.CurrentId()` instead of route+auth:

```csharp
services.AddScoped(sp => sp.GetRequiredService<CSharpToolService>().ForScope(BrainScope.CurrentId()));
```

`AddCSharpAuthoring()`/`AddHttpContextAccessor()` move into `Configure(ISiloBuilder)` too (or stay in the module's hosting extension — match where the module registers its other services). Move `CSharpSharing` + `ShareCSharpRequest` (namespace → `DigitalBrain.Microsoft.CSharp`), drop its `IOptions<BasicAuthOptions>` constructor dependency, and map `POST /brains/{brainId}/csharp/{id}/share` beside the other csharp routes.
- [ ] **Step 4: Clean the host** — remove `AddCSharp`/`MapCSharp` from `Program.cs`, delete the endpoint file, remove the share map from `PackageEndpoints.cs`.
- [ ] **Step 5: Run** the CSharp module suite and `dotnet test src/Applications/IntoChat/Tests/Unit` (5 known failures excepted). Expected PASS.
- [ ] **Step 6: Commit** — `git commit -m "Move the C# authoring endpoints and sharing into the CSharp module."`

---

### Task 8: Compute, Apps, and Applications endpoints move to their modules

Three moves, same recipe as Task 7. One commit each.

**Files/destinations:**

| Move | From (host) | To |
|---|---|---|
| Compute | `Agent/ComputeUsageEndpoints.cs` + `GET /compute/limits` in `Program.cs:51-52` | the Compute module (`Glob src/Modules/**/Compute*/*.csproj` for the exact project; endpoints into its `IModule.Configure(IEndpointRouteBuilder)`) |
| Apps | `Apps/AppEndpoints.cs`, `Apps/BuiltInAppEndpoints.cs` | the Apps module (`src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps`) — note BuiltInApps references `AssistantSurface`/`PackageService`; the assistant-activate route goes to the Assistant module instead if the Apps module cannot reference it (check project refs first; split by dependency, not by file) |
| Applications | `Applications/ApplicationEndpoints.cs` | Assistant module (`assistant/start|open`) and CustomerResearcher module (`customer-researcher/open`) |

**Interfaces:**
- Consumes: Task 5 recipe; `BrainScope.CurrentId()`; key helpers (`ComputeUsageEndpoints.IntentId`, `AgentEndpoints.ConversationKey`, `AssistantSurface.Key`, `CustomerResearcherSurface.Key`) — these take the scope id string, unchanged.
- Produces: `/brains/{brainId}/compute/usage`, `/compute/summary`, `/compute/limits`; `/brains/{brainId}/apps/...`, `/brains/{brainId}/built-in/...`; `/brains/{brainId}/applications/...` — each mapped by its module.

For each of the three moves:
- [ ] **Step 1:** Write/adapt one routing fact in the destination module's test project (module maps its routes under `/brains/`, non-member 403 via the group).
- [ ] **Step 2:** Run, verify failure.
- [ ] **Step 3:** Move the file(s), apply the recipe (group → `BrainRoutes.Group`, drop `workspaceId`+`auth` params, `BrainScope.CurrentId()`), delete from host, update `Program.cs` map list.
- [ ] **Step 4:** Run destination-module suite + IntoChat unit suite. Expected PASS.
- [ ] **Step 5:** Commit — `git commit -m "Move <area> endpoints into the <module> module."`

---

### Task 9: Packages, Agent/Voice, Workspace-data and Connections endpoints

**Files:**
- Move: `Packages/PackageEndpoints.cs` + `Packages/PackageService.cs` + the request/DTO records → the Registry/Apps module (check which module owns `IPackage`/registry contracts: `grep -rn "interface IPackage" src/Modules`); `PackageService` loses its `IOptions<BasicAuthOptions>` constructor parameter (`PackageService.cs:17`) — `AppKey(...)` becomes `BrainScope.CurrentId() + "/packages/" + id`.
- Move: `Agent/AgentEndpoints.cs`, `Agent/VoiceEndpoints.cs` → the AI module.
- Move: `Workspace/WorkspaceEndpoints.cs` (window read/close/reopen, tables, SSE events) → the Flutter module (it drives `IWorkspace` — UI domain); `Workspace/WorkspaceConnectionsEndpoints.cs` → the Connector-owning module (`IConnectors` lives in `DigitalBrain.Sdk.Connectors`; put endpoints in the module that implements the connections neuron — find it: `grep -rn "class.*: .*IConnectors" src/Modules`).
- Modify: `src/Applications/IntoChat/IntoChat/Workspace/WorkspaceConnectionRecords.cs` moves with the connections endpoints.
- Test: destination module suites; `src/Applications/IntoChat/Tests/Unit/WorkspaceConnectionsFacts.cs` follows its code.

**Interfaces:**
- Consumes: recipe; `BrainAccessFilter.Decide` for the body-carried case.
- Produces: `/packages/*` (global, NOT brain-grouped — registry browsing is cross-brain; keep its current non-workspace routes as-is), `/brains/{brainId}/packages/{owner}/{name}/*`, `/brains/{brainId}/conversations/{threadId}`, `POST /agent` (body-carried), `POST /brains/{brainId}/voice`, `/brains/{brainId}` window state + `/brains/{brainId}/connections`.

- [ ] **Step 1: Write the failing body-carried fact** (Review Focus #4), in the AI module tests:

```csharp
[Fact]
public async Task TheAgentRouteDecidesMembershipFromTheBodyBrain()
{
    // POST /agent with body { workspaceId → brainId: <alice's brain> } as bob → 403.
    // As alice → accepted, and the run's scope equals BrainScope.Create(alice.AccountId, brain).Id.
}
```

Write it fully against the harness found in Task 5; the `AgentInput.WorkspaceId` property renames to `BrainId` (wire shape — Dart mirror lands in Task 10, same PR).
- [ ] **Step 2: Run, verify failure.**
- [ ] **Step 3: Move + convert all four areas** (one sub-commit each is fine). In `AgentEndpoints`, the body-carried check stays explicit: `if (await BrainAccessFilter.Decide(http, input.BrainId) is { } denied) ...`, and scope resolution must use the body value — `BrainScope.Create(CallerContextStamper.Require().AccountId, input.BrainId).Id`, NOT `CurrentId()` (the route has no brainId; the stamped context's BrainId defaults). Add that as an inline comment.
- [ ] **Step 4: Delete `Operations/OperationsEndpoints.cs` + `Operations/ProblemReport.cs`** (dead — never mapped) and Task-1's `WorkspaceScope` shim if it was added.
- [ ] **Step 5: Run all touched module suites + IntoChat unit suite.** Expected PASS.
- [ ] **Step 6: Commit** — `git commit -m "Move packages, agent, voice, window and connection endpoints into their modules."`

---

### Task 10: Dart shell mirror

**Files:**
- Modify: `src/Modules/Google/Flutter/app/core/lib/src/ui_client.dart` (every request path containing `/workspaces/` → `/brains/`; `defaultWorkspaceId` → `defaultBrainId`; the agent input's `workspaceId` JSON key → `brainId`)
- Modify: `src/Modules/Google/Flutter/app/shell/lib/...` call sites of the renamed client members (`grep -rn "defaultWorkspaceId\|/workspaces/" src/Modules/Google/Flutter/app --include=*.dart`)
- Test: `src/Modules/Google/Flutter/app/shell/test/` + `app/core` tests — update path expectations

**Interfaces:**
- Consumes: the C# wire shapes produced by Tasks 5–9 (`/brains/{brainId}/...`, `AgentInput.BrainId`).
- Produces: a shell that emits only the new routes (Review Focus #5 — no `/workspaces/` string remains under `app/`).

- [ ] **Step 1: Update failing tests first** — change path expectations in the client/shell tests to `/brains/`, run `flutter test`, verify the failures point at the client.
- [ ] **Step 2: Rename in `ui_client.dart` and call sites.** UI-domain Dart names (`WorkspaceStore`, `WorkspaceApp`, `reconcileWorkspace`, persistence keys like `intocaht.workspace.v1`) stay — persistence keys especially, or saved shell state is orphaned.
- [ ] **Step 3: Verify** — `flutter analyze && flutter test` from `src/Modules/Google/Flutter/app/shell`; then `grep -rn "/workspaces/" src/Modules/Google/Flutter/app --include=*.dart` → zero hits.
- [ ] **Step 4: Commit** — `git commit -m "Mirror the brain route rename into the Dart shell."`

---

### Task 11: Final sweep, straggler rename, smoke

**Files:** repo-wide.

- [ ] **Step 1: Sweep** — each must return zero (or only spec/plan docs and the preserved storage prefix):
  - `grep -rn "WorkspaceScope" src --include=*.cs` (tests renamed to `BrainScope`)
  - `grep -rn "WorkspaceAccessFilter\|IWorkspaceAccess" src --include=*.cs`
  - `grep -rn "workspaces/{" src --include=*.cs`
  - `grep -rn "IOptions<BasicAuthOptions>" src --include=*.cs` → only `AccountSession.cs` and options registration (`IntoChatConfiguration.cs:12`) may remain
  - `grep -rn "intochat.workspace\b" src --include=*.cs` (claim)
  - E2E test files (`WorkspaceRestoreFacts`, `AgentWorkflowFacts`, `GoldenJourneyFacts`, `ReceiptJourneyFacts`, `LocalAppsJourneyFacts`, `LocalAppNeuronFacts`, `SupabaseTableDisplayFacts`, `AgentTableJourneyFacts`, `WorkspaceDataFacts`, `WorkspaceConnectionsFacts`): `WorkspaceScope.Create` → `BrainScope.Create` — derivation unchanged so only the name moves.
- [ ] **Step 2: Run the affected unit suites** — Identity, Kernel, CSharp, Files, Flutter, Apps, AI/Compute modules, IntoChat Tests/Unit (5 known failures only).
- [ ] **Step 3: Smoke** — `aspire run` from `src/Applications/IntoChat/AppHost`; all resources Healthy; open the shell, confirm login → brain loads → an app opens (network tab shows `/brains/` calls).
- [ ] **Step 4: Update `CLAUDE.md`** — the four-word section gains the brain sentence: brains are neurons, one or more per user, registry per brain; endpoints belong to modules under `BrainRoutes.Group`.
- [ ] **Step 5: Commit** — `git commit -m "Finish the brain rename: sweep stragglers, document the brain neuron."`
