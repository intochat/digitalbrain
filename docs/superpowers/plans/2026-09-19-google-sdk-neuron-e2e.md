# Google module + SDK Auth/Webhook + neuron/e2e split

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Google is the first complete module: neuron tests prove `IGmail` publishes `MailReceived`; e2e tests POST a Gmail-style webhook over HTTP and observe the same signal. `DigitalBrainSimulation` has no HTTP. Auth and webhooks live in a reusable SDK.

**Architecture:** Three layers stay distinct. (1) **Neuron tests** = `InProcessTestCluster` only — grains, `PublishAsync`, `Observe<T>`. (2) **Module e2e** = same in-process cluster **plus** a Kestrel host that maps `IModule.Configure(endpoints)` — today’s `UseHttp` block, moved into `DigitalBrain.E2ETesting`, not into Simulation. (3) **App e2e** = Aspire AppHost (Azurite, Flutter) stays IntoChat’s job. Google does **not** need Aspire.Hosting for this vertical. SDK (`DigitalBrain.Sdk`) owns Auth + Webhook helpers; Google maps `/google/gmail/watch` with the webhook helper and publishes `MailReceived`.

**Tech Stack:** Orleans `InProcessTestCluster`, ASP.NET Core `WebApplication` for module e2e, current kernel (`INeuron`, `Signal`, `PublishAsync`). No `DigitalBrain.Abstractions` / `Command` / `Accepted<T>` on the new Gmail surface.

**Out of scope:** Porting the full Gmail command API (drafts, MCP tools, token refresh). Real Google OAuth against accounts.google.com. Time e2e. Putting Kestrel back into `DigitalBrainSimulation`.

---

## Locked decisions

| Decision | Choice |
|---|---|
| Where HTTP lives | Module e2e + production IntoChat `MapDigitalBrainModules`. **Not** `DigitalBrainSimulation`. |
| Google test projects | `Tests/` = neuron. `E2E/` = webhook HTTP. |
| SDK shape | **One** project `DigitalBrain.Sdk` with folders `Auth/` and `Webhooks/` (rename of `DigitalBrain.Http`). Not two NuGet packages yet. |
| First Gmail signal | `MailReceived` on `IGmail` after a watch push. Auth route can be mapped as a stub using SDK Auth **after** webhook e2e is green. |
| Kernel | New `IGmail` methods on `DigitalBrain.Contracts.INeuron`. Old Command-based `IGmail` stays Compile-Remove until a later port. |
| `UseReminders` | Unchanged in this plan (Time-only). |
| `SimulationOptions.Configuration` | Delete if still unused when Simulation is touched. |

---

## Target tree

```
src/Modules/DigitalBrain/Sdk/          # was Http/
  DigitalBrain.Sdk.csproj
  Auth/                                # BrowserLogin*, OAuthTokens, TokenHandoff, IHttpSurface
  Webhooks/                            # MapWatch helper, signature/header utilities
  HttpSurfaces/                        # UseModuleHttpSurfaces (pipeline hook)

src/Modules/Google/
  Contracts/Gmail/IGmail.cs
  Contracts/Gmail/Signals/MailReceived.cs
  Google/Gmail/GmailNeuron.cs
  Google/Gmail/GmailWatchEndpoint.cs   # Configure(IEndpointRouteBuilder)
  Google/GoogleModule.cs
  Tests/                               # neuron — Observe<MailReceived>
  E2E/                                 # POST webhook → Observe or HTTP proof

src/Testing/
  DigitalBrain.NeuronTesting/          # StartAsync: silo only
  DigitalBrain.E2ETesting/
    ModuleWebHost.cs                   # the old UseHttp block
```

---

### Task 1: Strip HTTP from `DigitalBrainSimulation`

**Files:**
- Modify: `src/Testing/DigitalBrain.NeuronTesting/DigitalBrainSimulation.cs`
- Modify: `src/Testing/DigitalBrain.NeuronTesting/SimulationOptions.cs`
- Modify: `src/Testing/DigitalBrain.NeuronTesting/Host/SimulatedBrain.cs`
- Modify: `src/Testing/DigitalBrain.NeuronTesting/Host/BrainSimulationExtensions.cs`
- Create: `src/Testing/DigitalBrain.E2ETesting/ModuleWebHost.cs`
- Modify: `src/Modules/DigitalBrain/Tests/ModuleEndpointFacts.cs`
- Modify: `src/Modules/Flutter/Tests/InboxFacts.cs`

- [ ] **Step 1:** Add `ModuleWebHost` in E2ETesting (this **is** the current `UseHttp` block):

```csharp
namespace DigitalBrain.E2ETesting;

public sealed class ModuleWebHost : IAsyncDisposable
{
    public HttpClient Client { get; }
    // owns WebApplication

    public static async Task<ModuleWebHost> StartAsync(
        IClusterClient clusterClient,
        IReadOnlyList<IModule> modules,
        CancellationToken cancellationToken = default)
    {
        var webBuilder = WebApplication.CreateBuilder();
        webBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        webBuilder.Logging.ClearProviders();
        webBuilder.Services.AddSingleton<IGrainFactory>(clusterClient);
        webBuilder.Services.AddSingleton<IClusterClient>(clusterClient);
        var web = webBuilder.Build();
        foreach (var module in modules) { module.Configure(web); }
        await web.StartAsync(cancellationToken);
        var http = new HttpClient { BaseAddress = new Uri(web.Urls.Single() + "/") };
        return new ModuleWebHost(web, http);
    }
}
```

- [ ] **Step 2:** Rewrite `ModuleEndpointFacts` to: `StartAsync` **without** `UseHttp`, then `await using var http = await ModuleWebHost.StartAsync(brain.Grains() as IClusterClient ?? …, [echo])`. `SimulatedBrain.Grains` is `cluster.Client` (`IClusterClient`). Expose it: `brain.Grains()` already returns `IGrainFactory`; `IClusterClient` is the same object — cast or add `ClusterClient()` on extensions used only from e2e/kernel HTTP tests.

Kernel `ModuleEndpointFacts` currently lives in Runtime.Tests which references **NeuronTesting**, not E2ETesting. **Move** `ModuleEndpointFacts` to a small HTTP test in `DigitalBrain.E2ETesting` tests **or** add a NeuronTesting→E2ETesting reference (wrong direction). Correct: Runtime.Tests may reference E2ETesting for this one fact, **or** delete Echo HTTP from kernel tests and only cover HTTP in Google E2E.

**Locked:** Delete `ModuleEndpointFacts` from Runtime.Tests. Google E2E is the HTTP proof. InboxFacts: call `IInbox.Read()` / grain only in Flutter neuron tests; HTTP `/ui/inbox` stays IntoChat/Flutter e2e.

- [ ] **Step 3:** Remove `UseHttp`, `web`, `http`, `Http()` from Simulation / SimulatedBrain / SimulationOptions. Delete unused `Configuration` dictionary if still unreferenced.

- [ ] **Step 4:** Run `dotnet test src/Modules/DigitalBrain/Tests/DigitalBrain.Runtime.Tests.csproj -p:CodeGraphRefresh=false`  
  Expected: pass without ModuleEndpointFacts (or that fact moved). Flutter InboxFacts grain-only.

- [ ] **Step 5:** Commit `Remove Kestrel from DigitalBrainSimulation; HTTP hosts live in E2E ModuleWebHost.`

---

### Task 2: Rename Http → `DigitalBrain.Sdk` (Auth + Webhooks folders)

**Files:**
- Rename project: `src/Modules/DigitalBrain/Http/DigitalBrain.Http.csproj` → `src/Modules/DigitalBrain/Sdk/DigitalBrain.Sdk.csproj`
- Move existing types into `Sdk/Auth/` (`BrowserLogin*`, `OAuthTokens`, `TokenHandoff*`, `LoginPage`, `McpHttpSession` if auth-related)
- Keep `IHttpSurface` + `UseModuleHttpSurfaces` in `Sdk/HttpSurfaces/` (used by IntoChat pipeline)
- Create: `src/Modules/DigitalBrain/Sdk/Webhooks/WebhookExtensions.cs`

- [ ] **Step 1:** `git mv` the Http project directory to `Sdk`. Update slnx, all `ProjectReference`s (`Google`, IntoChat if any, Aspire.Hosting Google).

- [ ] **Step 2:** Namespace: keep `DigitalBrain.Http` **or** switch to `DigitalBrain.Sdk.Auth` / `DigitalBrain.Sdk.Webhooks`. Prefer **`DigitalBrain.Sdk`** single namespace to avoid a using riot; folders are for humans.

- [ ] **Step 3:** Webhook helper (Gmail-agnostic):

```csharp
public static class WebhookExtensions
{
    public static RouteHandlerBuilder MapJsonWebhook<TBody>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TBody, IGrainFactory, CancellationToken, Task<IResult>> handle)
        => endpoints.MapPost(pattern, handle);
}
```

Do **not** invent HMAC until Google’s push format is wired (Task 4). Empty helper that is just `MapPost` is acceptable if the first Gmail route uses it.

- [ ] **Step 4:** `dotnet build` Google + Sdk. Commit `Rename DigitalBrain.Http to DigitalBrain.Sdk with Auth and Webhooks folders.`

---

### Task 3: Google folders + new-kernel `IGmail` slice (neuron)

**Files:**
- Create: `src/Modules/Google/Contracts/Gmail/IGmail.cs`
- Create: `src/Modules/Google/Contracts/Gmail/Signals/MailReceived.cs`
- Create: `src/Modules/Google/Google/Gmail/GmailNeuron.cs` (new file; old `GmailNeuron.cs` Compile Remove)
- Modify: `GoogleModule.cs` — `Configure(ISiloBuilder)` registers nothing extra for this slice (grain discovered); `Configure(IEndpointRouteBuilder)` deferred to Task 4
- Modify: contracts/impl csproj `Compile Remove` on old Command types that use Abstractions so **Google compiles** on this branch
- Create: `src/Modules/Google/Tests/Gmail/MailReceivedFacts.cs`

Old `IGmail` is Command-based and will not compile. Compile-Remove the Abstractions contracts; replace with:

```csharp
namespace DigitalBrain.Google;

public interface IGmail : INeuron
{
    Task AcceptWatchPush(GmailWatchPush push);
}

[GenerateSerializer, Alias("gmail.watch-push")]
public sealed record GmailWatchPush(
    [property: Id(0)] string HistoryId,
    [property: Id(1)] string EmailAddress);

[GenerateSerializer, Alias("gmail.mail-received")]
public sealed record MailReceived(
    [property: Id(0)] string EmailAddress,
    [property: Id(1)] string HistoryId) : Signal;

[GrainType("gmail")]
public sealed class GmailNeuron : Neuron, IGmail
{
    public Task AcceptWatchPush(GmailWatchPush push)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(push.EmailAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(push.HistoryId);
        return PublishAsync(new MailReceived(push.EmailAddress, push.HistoryId));
    }
}
```

Neuron test:

```csharp
[Fact]
public async Task WatchPushPublishesMailReceived()
{
    await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new GoogleModule()] }, ct);
    var gmail = brain.Get<IGmail>("me");
    await using var mail = await brain.Observe<MailReceived>(gmail, ct);
    await gmail.AcceptWatchPush(new("123", "user@gmail.com"));
    var received = await mail.NextAsync(ct: ct);
    Assert.Equal("user@gmail.com", received.EmailAddress);
    Assert.Equal("123", received.HistoryId);
}
```

- [ ] Run `dotnet test src/Modules/Google/Tests/DigitalBrain.Modules.Google.Tests.csproj -p:CodeGraphRefresh=false`  
  Expected: this fact green. Google module **builds**.

- [ ] Commit `Add IGmail MailReceived on the live kernel; neuron test for watch push.`

Do not implement OAuth connect in this task.

---

### Task 4: Google webhook endpoint + Google E2E project

**Files:**
- Create: `src/Modules/Google/Google/Gmail/GmailWatchEndpoint.cs` (or map inside `GoogleModule.Configure(IEndpointRouteBuilder)`)
- Create: `src/Modules/Google/E2E/DigitalBrain.Modules.Google.E2E.csproj`
- Create: `src/Modules/Google/E2E/GmailWatchWebhookFacts.cs`
- Modify: `DigitalBrain.slnx` add E2E project

Google maps:

```csharp
public void Configure(IEndpointRouteBuilder endpoints)
{
    endpoints.MapJsonWebhook<GmailWatchPush>("/google/gmail/watch", async (push, grains, ct) =>
    {
        await grains.GetGrain<IGmail>(push.EmailAddress).AcceptWatchPush(push);
        return Results.Accepted();
    });
}
```

Gmail push bodies in production are Pub/Sub envelopes (`message.data` base64). **First e2e uses our `GmailWatchPush` JSON** so we are not blocked on Google’s envelope. A later task can unwrap Pub/Sub.

E2E test:

```csharp
[Fact]
public async Task GmailWatchHttpPublishesMailReceived()
{
    await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new GoogleModule()] }, ct);
    await using var web = await ModuleWebHost.StartAsync(brain.Grains() /* IClusterClient */, [new GoogleModule()], ct);
    await using var mail = await brain.Observe<MailReceived>(brain.Get<IGmail>("user@gmail.com"), ct);
    var response = await web.Client.PostAsJsonAsync("google/gmail/watch", new GmailWatchPush("123", "user@gmail.com"), ct);
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.Equal("123", (await mail.NextAsync(ct: ct)).HistoryId);
}
```

E2E csproj: `Module.Tests.props` + `ProjectReference` to `DigitalBrain.E2ETesting` + Google module. **Not** Aspire AppHost.

- [ ] Run Google neuron tests + Google E2E tests.  
  Expected: both green (in-process; no Docker).

- [ ] Commit `Add Gmail watch webhook and Google module e2e.`

---

### Task 5: Auth SDK used by Google (map only)

**Files:**
- Modify: `GoogleModule.Configure(IEndpointRouteBuilder)` to also map a login callback using existing `IHttpSurface` / `BrowserLoginSurface` **if** those types compile after Sdk rename.
- If Gmail OAuth still depends on Abstractions, **skip mapping real OAuth** and add a failing-or-skipped note: Auth SDK is relocated; Gmail OAuth port is a follow-up. Do **not** half-wire `AddGmailAuthentication` if it does not compile.

Minimum for “auth endpoint exists”:

```csharp
endpoints.MapGet("/google/gmail/oauth/callback", () => Results.Ok());
```

E2E: `GET google/gmail/oauth/callback` → 200. Proves module HTTP + SDK folder, not Google’s token exchange.

- [ ] Commit `Map Gmail OAuth callback stub via module HTTP.`

Full OAuth against Google is **not** this plan.

---

### Task 6: Simulation leftovers

- [ ] Confirm `UseHttp` / `Configuration` gone. Runtime + Time + Behaviors + Google Tests + Google E2E all pass:

```
dotnet test --project src/Modules/DigitalBrain/Tests/DigitalBrain.Runtime.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Time/Tests/DigitalBrain.Modules.Time.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Behaviors/Tests/DigitalBrain.Behaviors.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Google/Tests/DigitalBrain.Modules.Google.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Google/E2E/DigitalBrain.Modules.Google.E2E.csproj -p:CodeGraphRefresh=false
```

- [ ] Update `src/Testing/README.md`: neuron = no HTTP; module e2e = `ModuleWebHost`; app e2e = Aspire.

---

## Order

```
Task 1 Simulation without HTTP
  → Task 2 Sdk rename
  → Task 3 IGmail MailReceived neuron
  → Task 4 webhook + Google E2E
  → Task 5 auth stub
  → Task 6 verify matrix + README
```

## Explicit non-goals

- Aspire Google hosting / Pub/Sub emulator  
- Porting drafts, MCP Gmail tools, `GmailNativeTools`  
- `UseHttp` on `DigitalBrainSimulation`  
- Two NuGet packages for Auth vs Webhooks  

## Spec coverage

| Ask | Task |
|---|---|
| Neuron vs e2e separation | 1, 3, 4 |
| Elon-style webhook in e2e | 4 (`ModuleWebHost`) |
| Google two test projects | 3 Tests, 4 E2E |
| SDK Auth + Webhook | 2, 5 |
| Google subfolders like Time | 3 |
| Webhook → IGmail signal | 3 + 4 |
| Strip UseHttp from Simulation | 1 |
