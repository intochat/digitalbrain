# CONTINUATION — Multi-User Identity: Stage S2 (Identity Spine) + Stage S3 (Salesforce Per-User)

Status: DESIGN APPROVED-PENDING (owner decisions D-MU1–D-MU6 already made in
`docs/CONTINUATION-MULTIUSER-IDENTITY.md`; one new open decision below, D-MU7). No implementation yet.
Repo: `E:\brain`. Prior art: `docs/CONTINUATION-MULTIUSER-IDENTITY.md` (design + invariants — read that
doc's §2 "Prior-art verdict" and §3 "Invariants" before this one; they still govern everything below and
are not re-derived here). Date: 2026-07-04.

This is the session handoff for MULTIUSER stages **S2** and **S3**, picking up immediately after **S1**
(grain-routed Salesforce OAuth callback, merged to master `722a462`, fixed P1 permanently). Every claim
below was re-verified against the current tree post-S1, not assumed — including confirming S1 did not
accidentally touch anything S2/S3 depend on.

---

## 0. What S1 changed (and didn't)

S1 touched exactly: `DigitalBrain.Core/Synapse.cs`, `DigitalBrain.Kernel/Program.cs`,
`DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs`, `DigitalBrain.Salesforce/ISalesforceAuthNeuron.cs`,
`DigitalBrain.Salesforce/SalesforceClientFactory.cs`, and 3 Salesforce test files. `SalesforceAuthNeuron`
now has `CompleteOAuthAsync`, but it — like every other method on that grain — still hardcodes
`SalesforceClientFactory.DefaultScope` ("default") on every `IPackConfigStore` call, including the new
one (`SalesforceAuthNeuron.cs:121,122,150,151`). **S3 is a clean, unstarted slice** — S1 did not
partially do S3's job.

`UserSessionNeuron.cs`, `GatewayService.cs`, `HomeFeedBus.cs`, and `SalesforceCrmNeuron.cs` are
byte-for-byte unchanged by S1. `NeuronScope`, `PackConfigScopes`, and `IGrainContextAccessor` — all
three proposed in the original doc — have **zero references anywhere in the codebase** (grepped
repo-wide); they exist only as prose in `docs/CONTINUATION-MULTIUSER-IDENTITY.md`.

---

## 1. Confirmed problems, current evidence (S2 scope)

**P3 (from original doc, still true).** `UserSessionNeuron` (`DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`,
grain key `"session-main"`, `[GrainType("digitalbrain.user-session.v1")]`) journals `LoginSucceeded`/
`UserSessionCreated` and exposes:
```csharp
public Task<UserSessionState?> GetSessionAsync(string sessionId)
```
(lines 85-124, `IUserSessionNeuron` interface in `DigitalBrain.Ui.Contracts/UiNeuronContracts.cs:3-7`).
**Nothing calls it from the gateway.** Grepped `GatewayService.cs` for `GetSessionAsync` — zero hits.

**Username charset validation — still missing.** `NormalizeUsername` exists
(`UserSessionNeuron.cs:332-333`):
```csharp
private static string NormalizeUsername(string value) =>
    (value ?? string.Empty).Trim().ToLowerInvariant();
```
It trims and lowercases only — no rejection of `/`, embedded whitespace, or quotes. Registration is
inline inside `HandleAsync(LoginRequest)` (lines 24-71): a new user is created via
`CreateLocalUser(username, request.Password)` (line 51) and journaled as `LocalUserRegistered`
(`Synapse.cs:110-116`) with zero additional validation. This must be fixed **before** any grain key ever
embeds a raw username (S2 prerequisite, same reasoning as the original doc's §4.1 "Prerequisite" note).

**P6 (from original doc, both halves still true, exact evidence):**

- **P6a — `HomeFeedBus` is unfiltered.** `DigitalBrain.Kernel/Ui/HomeFeedBus.cs`, full file 93 lines.
  `Subscribe()` (lines 26-32) is parameterless. `FanLocal` (lines 35-40):
  ```csharp
  public void FanLocal(RfwCard card)
  {
      if (IsDuplicate(card)) return;
      foreach (var (_, channel) in _subscribers)
          channel.Writer.TryWrite(card);
  }
  ```
  Every subscriber gets every card, filtered only by content-hash dedup — never by identity.
  `GatewayService.WatchHomeFeed` (`GatewayService.cs:239-254`) calls `homeFeedBus.Subscribe()` with no
  arguments and never reads any field off `WatchHomeFeedRequest` besides using it as a typed marker —
  no `sessionId` is extracted, validated, or used at all today.

- **P6b — gateway trusts client-supplied identity fields.** Exact sites in `GatewayService.cs` (525
  lines total):
  - `InstallFromMarketplace` branch (lines 63-69): `buyerId`/`userId`/`sessionId` read straight off the
    request payload, no validation.
  - `ConfigurationProvided` branch (lines 116-131): `scope` is client-controllable via an explicit
    `"scope"` field, defaulting to `"default"` but **never whitelisted** — matches the original doc's
    §8 risk note verbatim: *"the gateway's `ConfigurationProvided` branch keeps honoring explicit
    `scope` but must whitelist: only `"default"` (admin-gated later) or the caller's own
    `user:{userId}`."*
  - `InoRequest` branch (lines 150-157): `sessionId` read off payload, forwarded into `InoRequest`
    unchecked.
  - `LoginRequest`/`LogoutRequest` branches (lines 160-181): client-supplied `sessionId` fired into
    `LogoutRequest` with no prior lookup against `session-main`.

---

## 2. Confirmed problems, current evidence (S3 scope)

**P2 (from original doc, still true, Salesforce-specific evidence).** Every `SalesforceAuthNeuron`
method — including S1's new `CompleteOAuthAsync` — hardcodes scope
`SalesforceClientFactory.DefaultScope`:
- `CompleteOAuthAsync`, `SalesforceAuthNeuron.cs:121`: `store.GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName)`
- `SalesforceAuthNeuron.cs:122`: same for `OAuthPendingPackName`
- `SalesforceAuthNeuron.cs:150,151`: same on write-back and pending-clear

**`SalesforceCrmNeuron`** (`DigitalBrain.Kernel/Salesforce/SalesforceCrmNeuron.cs`, full file, 18 lines,
grain key `"salesforce-main"`, `[GrainType("digitalbrain.salesforce.crm.v1")]`):
```csharp
public class SalesforceCrmNeuron(
    ILogger<SalesforceCrmNeuron> logger,
    NeuronJournals journals,
    ISalesforceApiClient client)
    : Neuron(logger, journals), ISalesforceCrmNeuron
{
    public Task<string[]> QueryAsync(string soql, CancellationToken ct = default) => client.QueryAsync(soql, ct);
    public Task<string[]> ListAccountsAsync(int maxResults = 20, CancellationToken ct = default) => client.ListAccountsAsync(maxResults, ct);
}
```
`client` is a **constructor-injected scoped DI dependency** — built in `Program.cs:154-158`:
```csharp
// Salesforce CRM REST API client: built from the encrypted "salesforce"/"default" pack config scope that
// the Salesforce credential prompt stores. Scoped for the same per-grain-activation reason as Google.
builder.Services.AddScoped<DigitalBrain.Salesforce.ISalesforceApiClient>(sp =>
    DigitalBrain.Salesforce.SalesforceClientFactory
        .CreateApiClientAsync(sp.GetRequiredService<DigitalBrain.Core.Config.IPackConfigStore>())
        .GetAwaiter()
        .GetResult());
```
`CreateApiClientAsync` (`SalesforceClientFactory.cs:39-45`) defaults `scope` to `DefaultScope`, and the
DI registration never overrides it. Because this is `AddScoped` + constructor injection (not a lazy
`Func<Task<T>>`), Orleans resolves the client — and therefore runs `CreateApiClientAsync(...)
.GetAwaiter().GetResult()`, which throws if config is missing — **at grain activation time**, before any
method call. This is the exact "eager-throw-on-activation" pattern the original doc's §4.4 describes,
confirmed byte-for-byte (the Google client factory right above it, lines 138-150, uses the identical
shape — same comment style, same `GetAwaiter().GetResult()` — confirming this is a repo-wide idiom, not
Salesforce-specific, so the fix in §4 below should be written once and reused for Google too when S4
lands).

---

## 3. New open decision: there is no `IGrainContextAccessor` — do not build one

The original doc's §4.4 proposed: *"Factories resolve `IGrainContextAccessor` from the per-activation DI
scope, parse `NeuronScope` from the activating grain's key, read `user:{userId}` scope."* Verified: this
type does not exist in Orleans (no reference anywhere in this codebase's Orleans usage) or in this repo.
Building an ambient-context accessor from scratch to let a DI-resolved factory *lambda* (which only has
`IServiceProvider sp`, not the grain instance) discover "which grain is activating me" is unnecessary
complexity — Orleans grain instance code already has this for free via `this.GetPrimaryKeyString()`
(exactly what `Neuron.Self` uses, `Neuron.cs:35`).

**D-MU7 (new, proposed for this session)** — Skip `IGrainContextAccessor` entirely. Use the original
doc's own documented fallback instead: switch from eager constructor-injected clients to an **explicit,
grain-driven lazy factory** —
```csharp
public interface ISalesforceApiClientFactory
{
    Task<ISalesforceApiClient> CreateAsync(NeuronScope scope);
}
```
registered as a plain singleton (no DI ambient magic needed), and `SalesforceCrmNeuron` calls
`await apiClientFactory.CreateAsync(Self.AsScope())` explicitly inside `QueryAsync`/`ListAccountsAsync`,
using its own already-available `Self` (parsed via `NeuronScope.TryParse`). This is simpler than the
original proposal, achieves the same outcome (per-user, lazy, no activation-time throw), and needs no
new Orleans-adjacent infrastructure. Confirm this substitution before starting S3 — it changes §4.4's
shape from "ambient ap the codebase, no." to "grain calls it".

---

## 4. Design

### 4.1 Identity spine (`DigitalBrain.Core`) — unchanged from original doc, still the correct shape

```csharp
public readonly record struct NeuronScope(UserId UserId, string? ThreadId)
{
    public static bool TryParse(string grainKey, out NeuronScope scope) { /* split on first '/' */ }
    public string ToKey() => ThreadId is null ? UserId.Value : $"{UserId.Value}/{ThreadId}";
}

public static class PackConfigScopes
{
    public const string App = "default";
    public static string ForUser(UserId userId) => $"user:{userId.Value}";
}
```
Place both in a new file `DigitalBrain.Core/NeuronScope.cs` (do not add to `Synapse.cs` — neither type
crosses a grain-interface boundary as a wire record; they're plain value helpers used inside grain code
and the gateway, matching `UserId`'s own placement — check `UserId`'s current file before creating this
one, so the new file sits next to its closest sibling type rather than in the god-file).

Prerequisite (do this in Task 1 of the S2 plan, before anything else): fix
`UserSessionNeuron.NormalizeUsername` (`UserSessionNeuron.cs:332-333`) to reject `/`, whitespace, and
quotes at registration time — write the failing test first (register a username containing `/`, assert
`HandleAsync(LoginRequest)` rejects it) since usernames become grain-key segments in S3.

### 4.2 Gateway identity resolution + feed filtering (fixes P3, P6)

`GatewayService.WatchHomeFeed` (`GatewayService.cs:239-254`) needs a `sessionId` read off
`WatchHomeFeedRequest` (check the proto contract — `Kernel.proto` or wherever `WatchHomeFeedRequest` is
defined — for whether the field already exists unused, or needs adding), resolved once via a new
gateway-side helper:
```csharp
private async Task<UserSessionState?> ResolveSessionAsync(string? sessionId)
{
    if (string.IsNullOrWhiteSpace(sessionId)) return null;
    var session = grains.GetGrain<IUserSessionNeuron>("session-main");
    return await session.GetSessionAsync(sessionId);
}
```
`HomeFeedBus.Subscribe()` (`HomeFeedBus.cs:26-32`) needs a filtering parameter — the least invasive
change is an optional `Func<RfwCard, bool>` predicate or a `sessionId`/`userId` string passed to
`Subscribe`, checked in `FanLocal` (`HomeFeedBus.cs:35-40`) alongside the existing dedup check: a card
addressed to a specific `sessionId`/`userId` (need a convention for "addressed" — e.g. a well-known prop
key on `RfwCard`, check `RfwCard`'s current shape first) reaches only the matching subscriber;
unaddressed cards stay broadcast. Every other call site of `Subscribe()` — check
`HomeFeedCrossSiloTests.cs` and any production caller besides `GatewayService.cs` — needs updating to
pass through (or explicitly opt out of) filtering.

Delete trust in payload identity fields per P6b: `InstallFromMarketplace` and `InoRequest` branches
(`GatewayService.cs:63-69,150-157`) should resolve `userId` via `ResolveSessionAsync` instead of trusting
the payload; `ConfigurationProvided`'s `scope` field (`GatewayService.cs:116-131`) must whitelist
`PackConfigScopes.App` or the caller's own `PackConfigScopes.ForUser(resolvedUserId)` — reject anything
else with a clear error, per the original doc's §8 risk note.

### 4.3 Salesforce per-user (fixes P2, S3)

- `SalesforceAuthNeuron` and `SalesforceCrmNeuron` grain keys move from the literal singletons
  (`"salesforce-auth-main"`, `"salesforce-main"`) to `{userId}` — every call site needs updating:
  `GatewayService.cs:96,99` (auth), `InoNeuron.cs:381` (crm), plus every test file that hardcodes these
  keys (`SalesforceAuthNeuronTests.cs`, `SalesforceOAuthCrossSiloTests.cs`, `SalesforceCrmNeuronTests.cs`,
  `GatewayServiceTests.cs:179`).
- Every `SalesforceClientFactory.DefaultScope` call site inside `SalesforceAuthNeuron` (5 sites, listed
  in §2 above) becomes `PackConfigScopes.ForUser(Scope.Value.UserId)` — connected-app config
  (`client_id`/`client_secret`) stays `PackConfigScopes.App` ("default"); only tokens/pending-PKCE move
  to the per-user scope. Read `StartOAuthAsync` (`SalesforceAuthNeuron.cs`, still private, dispatched via
  the `IsOAuthStart` heuristic on `Signal`) carefully — it reads connected-app config from the SAME pack
  name (`SalesforceClientFactory.PackName`) that tokens are written to today; splitting scope by tier
  means reading connected-app config from `App` and merging pending/token writes into `ForUser(...)`,
  not blindly moving the whole pack.
- Per D-MU7 above: replace `Program.cs:154-158`'s eager `AddScoped<ISalesforceApiClient>` with an
  `ISalesforceApiClientFactory` singleton; `SalesforceCrmNeuron` calls it explicitly with its own
  `Self`-derived `NeuronScope` inside each method, not via constructor injection. This turns
  "user hasn't connected yet" from an activation-time throw into a normal per-call condition the neuron
  can handle (surface the connect-Salesforce button, same pattern `SalesforceAuthNeuron` already uses
  for `PublishCredentialFormAsync`).

---

## 5. Owner decisions carried forward from `docs/CONTINUATION-MULTIUSER-IDENTITY.md`

Still binding, unchanged: **D-MU1** (composite grain key `{userId}`/`{userId}/{threadId}` +
`NeuronScope`), **D-MU4** (pack-config scope convention, `"default"`/`"user:{userId}"`), **D-MU6**
(`session-main` stays a singleton for now). **D-MU2** (encrypted OAuth `state`) and **D-MU3** (pending
PKCE storage tier) are S3-adjacent but their full shape (DataProtection-encrypted state) is still S4
scope per the original doc's own stage split — S3 only needs the pack-config scope split (D-MU4), not
D-MU2's state encryption. **D-MU7** (this doc, §3) is new: confirm before starting S3.

Still open from the original doc, now blocking: **threadId format** and **default-thread semantics**
were flagged "decide before S5" — not relevant to S2/S3, no action needed here.

---

## 6. Stages (from the original doc's §6, reproduced verbatim for this handoff's scope)

**S2 — Identity spine.** Username charset validation; `NeuronScope` + `PackConfigScopes` in Core;
gateway-side session resolution helper; `HomeFeedBus` per-session filtering; delete trust in payload
identity fields. **Acceptance:** unauthenticated `WatchHomeFeed` sees only login + unaddressed system
cards; user A's client never receives a card addressed to user B.

**S3 — Salesforce per-user.** Auth + CRM neurons keyed `{userId}`; tokens under `user:{userId}`;
connected-app config stays `"default"`; lazy client factory per D-MU7 (supersedes the original doc's
`IGrainContextAccessor`-based wording — same outcome, simpler mechanism). **Acceptance:** two-user
interleaved OAuth test (below) green.

---

## 7. Test story

- **Feed isolation (S2):** two authenticated `WatchHomeFeed` streams (mirror `HomeFeedCrossSiloTests.cs`'s
  `InitialSilosCount => 2` + per-silo `IGrainFactory`/service-resolution shape, or single-silo with two
  subscriptions if cross-silo isn't needed for this specific test); card addressed to A's session never
  appears on B's stream.
- **Username validation (S2):** registering `"alice/bob"` (or any username containing `/`, whitespace, or
  quotes) is rejected by `HandleAsync(LoginRequest)`.
- **Cross-contamination (S3):** users A and B run interleaved `StartOAuth` → callback flows; assert A's
  nonce/verifier/tokens are unreadable from every B-keyed grain and every `user:B` store read, and vice
  versa; wrong-user callback (A's state, B's code) fails closed. Model this on
  `SalesforceOAuthCrossSiloTests.cs`'s shape (`NeuronTestBase`, `FakeSalesforceTokenHandler`,
  `ExtractQueryValue`) but key by **two different user grain keys** instead of two silos of the same key.
  The test-only `ISalesforceConnectedAppConfigWriter.ReadPackAsync(scope, pack)`
  (`SalesforceAuthNeuronTests.cs`) already takes `scope` as a parameter — it's ready to be pointed at
  `"user:{userId}"` today, no harness change needed there.

---

## 8. Risks / notes for the implementing session

- Filesystem MCP quirk: do not `list_directory` the repo root; use targeted subdirectory listings +
  `read_multiple_files` batches.
- `[GenerateSerializer]` + sequential `[Id(n)]` + concrete arrays on every new synapse/cross-grain record
  — non-negotiable, same as S1.
- `SalesforceCrmNeuronTests.cs` and `GatewayServiceTests.cs` both hardcode the old singleton grain keys
  — grep for `"salesforce-auth-main"` and `"salesforce-main"` across `Tests/` before declaring S3 done;
  a stale key in a test is a silent false-pass, not a compile error.
- Google's client factory (`Program.cs:138-150`) uses the identical eager-throw shape as Salesforce's —
  tempting to fix both at once, but stay in scope: S3 is Salesforce-only per the original doc's
  incremental principle (I5); leave Google's factory alone until S4 explicitly picks it up, note the
  duplication for whoever writes S4's plan.
- `WatchHomeFeedRequest`'s proto contract may or may not already carry an unused `sessionId` field —
  check before assuming a proto change is needed.
- `RfwCard`'s current shape needs checking before deciding how "addressed to a session/user" is encoded
  on a card (new field vs. reusing an existing prop bag) — don't invent a new convention without
  confirming one doesn't already exist half-used somewhere.
