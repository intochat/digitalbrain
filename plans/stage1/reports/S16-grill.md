# S1.6-GRILL — Gmail strangler (typed path deleted)

**Subject:** `66d8bb56` S1.6 Gmail strangler  
**Green report:** `plans/stage1/reports/S16-gmail.md`  
**Brief:** `plans/stage1/briefs/S16-gmail.md`  
**Role:** GRILL (judge only; no production edits; no git writes)  
**Attack surface:** deletion completeness, trap 2/6, parity honesty, overreach, config, Salesforce isolation, Flutter fixtures

---

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  2 Warning(s) — AppHost node NO_COLOR/FORCE_COLOR noise only (not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 165, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 180.869s
```

Known ticketed flakes (ChartVocabularyProofs timeout, restart proofs under parallel pressure) did **not** fire this run. Suite green alone does not authorize APPROVE — deletion + fixture honesty do.

Flutter (grill criterion 6):

```
flutter analyze   # repo root
  → shell: 9 errors in behavior_demo_fixtures.dart (see F1)

flutter analyze   # src/Modules/UI/Flutter/core
  → 1 pre-existing error: activateControl (Claude.md ticketed; not S1.6)
```

---

## (1) Trap 2 / trap 6 — deletion leaves no routeless ghosts

| Check | Evidence | Judgment |
|-------|----------|----------|
| Typed `gmail.*` / `IGmail` emitters gone | `rg` over `src/**/*.cs`: no `IGmail`, `GmailSearch*`, `GmailGet*`, `GmailAuthRail`, `GoogleSignIn`, `DurableGoogleTokenStore` except test string pins | **PASS** |
| Handler / ghost catalog | `typeof(GoogleModule).Assembly` has zero concrete `INeuron` types (`GmailMcpStranglerProofs.EmptyGoogleContractsAssemblyReflectsNoGmailVocabulary`) | **PASS** |
| Contracts assembly emptied | `Contracts/Gmail/*` deleted; no `.cs` under Contracts; project kept empty (Salesforce shape) | **PASS** |
| ComposedModules contracts list | `DigitalBrainComposition.cs` contracts array has **no** Google contracts assembly; implementations still load `GoogleModule` only (`:9–31`) | **PASS** |
| Manifest reflection | `ModuleReflection.ManifestOf(IMcp)` has `db.mcp.list-tools` / `db.mcp.call-tool`; proof asserts no fact ContractId containing `gmail` | **PASS** |
| Zero-receiver / trap 2 | No surviving emitter of deleted typed synapses; Gmail surface is only MCP gateway verbs | **PASS** |

Trap 8 not re-introduced: no new `IHandle<T>` for deleted Gmail types.

---

## (2) Parity honesty — Theory data + fake catalog

### Rail Theory suite actually covers both keys

Read `OAuthRailProofs.cs` InlineData (not the report table alone):

| Test | salesforce | google.gmail |
|------|------------|--------------|
| `ProviderAuthorizeUrlAlwaysCarriesPkceS256` | `:32–38` host `login.salesforce.com` | `:39–45` host `accounts.google.com` + real Gmail scopes |
| `McpTokenPurposesKeyByPrincipalNotNeuronIdentity` | `:100` | `:101` + asserts no `google/oauth/` |
| `CompletedAuthorizationCodeIsOneShotAndReplayIsRefused` | `:193` | `:194` |
| `AuthorizationPendingBindsTheLocalUserPrincipal` | `:263` | `:264` |
| `PrincipalTokenSlotsIsolateUserAFromUserB` | `:304` | `:305` |

**Judgment: PASS.** These are real Theory dual-runs (owner keys isolated via `BrainFor($"…-{serverKey.Replace('.', '-')}")`), not a salesforce-only suite with a Gmail comment.

Gap vs brief wording “expiry”: no Theory named/covering TTL expiry for either key in this file. Pre-existing S1.3 shape (no new Gmail-only hole). Report’s in-process table does **not** falsely claim an expiry Theory — honest enough. **MINOR residual**, not a parity lie.

### Fake Gmail catalog shape

`FakeMcpTransport.cs:12–32` for `google.gmail`:

- Tools: `search_threads` (read), `get_thread_messages` (read), `create_draft` (Destructive: true)
- Not SOQL echo (`soqlQuery` / `updateSobjectRecord` only on non-gmail branch)
- Gateway proofs assert Gmail list excludes `soqlQuery` (`McpGatewayProofs.cs:136–138`) and call journals actor + IntegrationSubject (`:142–167`)
- Call payload is threads-shaped (`{"threads":[…]}`), not CRM records

Official Gmail MCP tool list (Developer Preview docs): `search_threads`, `get_thread`, `create_draft`, `list_*`, `label_*`, …  
Fake uses `get_thread_messages` instead of official `get_thread` — approximate, still Gmail-shaped.

**Judgment: PASS with MINOR** — meaningfully shaped, not a trivial echo. Not exact official names.

---

## (3) Deletion completeness vs overreach

### Completeness

| Artifact | Status |
|----------|--------|
| `Gmail/`, `Auth/`, `Contracts/Gmail/*` | Deleted |
| `Google.Apis.Auth` / `Google.Apis.Gmail.v1` pins | Removed from `Directory.Packages.props` + Google csproj |
| Typed package refs | Dropped from Google project |
| `GoogleModule` | Definition + `ExternalServerCapability` only (`GoogleModule.cs:25–38`) |
| Leftover typed production code | Only `GoogleModule.cs` under `Google/Google/`; Aspire hosting for OAuth params |

### Overreach — `GoogleSignIn` / chat sign-in

- `GoogleSignIn` was **GmailAuthRail-only** OAuth helper (deleted with typed path). Chat sign-in never called it.
- `ChatSignInOfferProofs` still fires `AuthorizationRequired` / `BeginMcpAuthorization` with **Salesforce** display names and asserts “Sign in via Salesforce” button into main chat (`ChatSignInOfferProofs.cs:13–68`). Still a meaningful generic-rail proof.
- Chat path remains: MCP rail → `AuthorizationRequired` emit/send → chat button offer. Deleting `GoogleSignIn` is **not** overreach.

**Judgment: PASS.**

---

## (4) Config honesty — official Gmail MCP shape

| Item | Code | Official (developers.google.com Workspace Gmail MCP) | Match? |
|------|------|------------------------------------------------------|--------|
| Endpoint | `https://gmailmcp.googleapis.com/mcp/v1` (`GoogleModule.cs:17`) | Same global MCP endpoint | **Yes** |
| Scopes | `gmail.readonly` + `gmail.compose` (`:19–23`) | Same two scopes on Data Access setup | **Yes** |
| Client secret | `requiresClientSecret: true` (`:36`) | OAuth client ID **and** secret in MCP client config | **Yes** |
| Authorize URL | `accounts.google.com/o/oauth2/v2/auth` + PKCE S256 + `access_type=offline` + `prompt=consent` (`McpAuthorizationRail.cs:239–255`) | Google OAuth for Web clients | **Yes** (sensible defaults) |
| Token endpoint | `https://oauth2.googleapis.com/token` (`McpTokenExchange.cs:104–106`) | Standard Google token endpoint | **Yes** |
| Secrets committed | Aspire params `google-client-id` / `google-client-secret` (secret: true); no literals in repo | Secret never in source | **Yes** |
| Test reachability | Fixture definition uses `http://localhost:1/mcp` (`BrainClusterFixture.cs:92–101`); FakeMcpTransport never opens real host | Brief: never hardcode live reachability in tests | **Yes** |

Server key `google.gmail` (not brief example `google/gmail`) is correctly forced by `IdentityPart` — report documents it; neuron id `mcp:dev/google.gmail` validated.

**Judgment: PASS.**

---

## (5) Salesforce path untouched and green

- `git diff HEAD~1 -- src/Modules/Salesforce/`: **empty**
- Salesforce module still definition + capability only (`SalesforceModule.cs`)
- CRM gateway proofs still use fixture key `crm` + SOQL fake tools; OAuth Theories retain `salesforce` InlineData
- Suite 165/165 includes full Salesforce/MCP/OAuth surface

SDK changes are additive Google defaults on the shared rail (`IsGoogleGmailServer`) — parallel to existing Salesforce hardcodes; do not alter Salesforce branch logic beyond shared PKCE path.

**Judgment: PASS.**

---

## (6) Flutter fixture edits compile

**FAIL — introduced by this commit.**

`behavior_demo_fixtures.dart` wraps C# program source in a Dart raw triple-quote string:

```dart
static const accountEnrichmentProgramSource = r"""
...
System.Text.Json.JsonDocument.Parse("""{"query":"in:inbox"}""").RootElement));
...
""";
```

The C# raw string `"""{"query":…}"""` **terminates the Dart `r"""` at line 72**. Analyzer reports 9 errors on shell alone (`expected_token`, `missing_identifier`, `expected_class_member` through `:102`). Pre-S1.6 fixture had no nested `"""` and parsed.

Core-only analyze: only pre-existing `activateControl` drift (`ui_client_test.dart:231`) — ticketed in Claude.md; **not** S1.6.

Secondary honesty note (would ride after fix): embedded program was reduced to list-tools + one `search_threads` call with `_ = research/salesforce` discards — demo choreography is thinner than metadata claims, but still MCP-shaped.

---

## Scorecard

| Attack vector | Result |
|---------------|--------|
| (1) Trap 2/6 / manifest clean | **PASS** |
| (2) Theory dual-key + fake catalog | **PASS** (MINOR name drift / no expiry Theory) |
| (3) Deletion vs overreach | **PASS** |
| (4) Config vs official Gmail MCP | **PASS** |
| (5) Salesforce untouched + green | **PASS** |
| (6) Flutter fixtures compile | **FAIL** |
| .NET gate | **PASS** 165/165 |

---

## Findings (file:line · severity)

1. **`src/Modules/UI/Flutter/shell/lib/behaviors/behavior_demo_fixtures.dart:72` · MAJOR (introduced regression / hard REJECT)**  
   Nested C# `"""…"""` inside Dart `r"""…"""` terminates the outer raw string early. `flutter analyze` on shell fails with 9 parse errors. Pre-commit fixture compiled; S1.6 `CallMcpTool` JSON argument broke it. Fix: use a non-triple-quote JSON literal in the embedded C# (e.g. `"{\"query\":\"in:inbox\"}"` or a single-quoted form) so the Dart raw string stays closed only at `:102`.

2. **`src/Tests/DigitalBrain.Tests/Harness/FakeMcpTransport.cs:23` · MINOR**  
   Tool name `get_thread_messages` vs official Gmail MCP `get_thread`. Catalog is still meaningfully Gmail-shaped (`search_threads` + `create_draft` match).

3. **`src/Tests/DigitalBrain.Tests/Harness/FakeMcpTransport.cs:56–65` · MINOR**  
   `CallToolAsync` ignores `tool` and always returns the threads payload (even for `create_draft`). Fine for list/audit proofs; not tool-dispatch fidelity.

4. **`src/Tests/DigitalBrain.Tests/OAuthRailProofs.cs` · MINOR residual**  
   Brief mentioned “expiry” in the S1.3 suite shape; no expiry Theory for either provider key. Not a Gmail-only honesty fail.

5. **`src/Modules/UI/Flutter/core/test/ui_client_test.dart:231` · pre-existing (out of scope)**  
   `activateControl` undefined — Claude.md known drift; not introduced by S1.6.

---

## Required before APPROVE

1. Repair `behavior_demo_fixtures.dart` string quoting so shell `flutter analyze` is clean of the S1.6-introduced parse errors (infos/warnings outside this file may ride).

Items 2–4 may ride as MINOR residuals after (1).

---

## What is solid (do not re-litigate after fixture fix)

- Typed Gmail path fully deleted; packages removed; composition/manifest clean (trap 2/6).
- Gmail = `McpServerDefinition` + `ExternalServerCapability` + shared OAuth rail defaults — Salesforce shape.
- Theory parity honestly dual-keys both providers.
- Official endpoint/scopes/client-secret posture correct; secrets not committed.
- Chat sign-in still meaningful without `GoogleSignIn`.
- Salesforce module tree untouched; full suite green.
- In-process vs live-smoke table in green report is honest.

---

## VERDICT: REJECT

**Findings:** 1 MAJOR (introduced Flutter shell parse break), 3 MINOR ride-alongs, 1 pre-existing out-of-scope.  
.NET strangler and parity are otherwise approve-ready; criterion (6) fails on a regression this commit introduced.
