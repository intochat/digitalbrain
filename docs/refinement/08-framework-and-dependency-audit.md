# 08 — Framework and Dependency Audit

Dependency-by-dependency assessment: why it exists, whether it is used correctly, and a retain/upgrade/replace/consolidate/delete recommendation. Versions are from `Directory.Packages.props` and `app/pubspec.yaml`.

## Documentation-gap disclosure (important)

**Context7's monthly quota was exhausted early in this assessment and stayed exhausted for every subagent.** Framework verification therefore fell back to **Microsoft Learn MCP** (for .NET/Orleans/Aspire) and **official pub.dev/GitHub docs via web fetch** (for Flutter/Dart), or is recorded as an explicit gap. The most consequential gap: **`Microsoft.Orleans.Journaling 10.2.1-preview.1.alpha.1`** — the durable-log API the entire kernel depends on — **has no public version-pinned documentation.** Microsoft Learn documents only the *classic* event-sourcing model (`JournaledGrain`/`LogConsistencyProvider`), which this code does **not** use. Correctness judgments about `NeuronJournals`/`DurableGrain`/`IDurableList` therefore rest on **source reading plus the code's own `#pragma warning disable ORLEANSEXP005` (alpha/experimental) markers**, not on authoritative docs. These are labeled below and in `kernel-runtime:FRAME-050`, `core`, and `connectors:FRAME-400`.

## .NET / runtime

| Dependency | Version | Assessment | Rec |
|---|---|---|---|
| Target framework | `net11.0` | **Preview.** The brief assumed .NET 10; the repo is on .NET 11 preview across all projects, with an `aspnet:11.0-preview` container base and CI `dotnet-quality: preview` (`mcp-hosts-build:PROD-500`). No servicing/LTS guarantee for a product core. | **Decide deliberately:** pin to a supported LTS (.NET 10) for the trusted core, or formally accept + document the preview risk with a servicing plan. |

## Orleans (actor runtime + journaling)

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `Microsoft.Orleans.Core/Server/Serialization/Streaming/Reminders/Persistence.Memory` | `10.2.1-preview.1` | Preview. Core actor runtime; used correctly for grains/serialization as far as source shows. **Version skew:** clustering/persistence packages are pinned at stable `10.2.0` (see below) — mixing preview core with stable clustering. | Align versions; move core off preview if feasible. |
| `Microsoft.Orleans.Clustering.AzureStorage/Redis`, `Persistence.AzureStorage`, `Reminders.AzureStorage`, `TestingHost` | `10.2.0` | Stable. **Double configuration** with Aspire keyed clients risks a boot-time throw under managed identity (`kernel-hosting:FRAME-200`). | Reconcile to one configuration path; add a managed-identity boot test. |
| `Microsoft.Orleans.Journaling` + `.AzureStorage` | `10.2.1-preview.1.**alpha.1**` | **Alpha, experimental (`ORLEANSEXP005`).** The trusted core's entire durability/replay/rollback story rests on this. **Doc gap** (above). Combined with unbounded journals (`kernel-runtime:PERF-100`) and additive checkpoints (`ARCH-051`), the durability model is both undocumented-externally and unsound-internally. | **Highest-risk dependency.** Plan a migration path (or a hardened wrapper with compaction + real snapshot/restore); do not treat as stable. Track upstream API churn. |

**Serialization correctness (verified vs Microsoft Learn):** `[GenerateSerializer]`/`[Alias]` usage is broadly correct, but ~40 records rely on **implicit positional `[Id]`** ordering (`core:REL-001`) — Orleans docs warn this breaks version tolerance on reorder. **Rec:** explicit `[Id(n)]` on every field + contract-freeze tests.

## Aspire (orchestration)

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `Aspire.Hosting*`, `Aspire.Hosting.Orleans/Redis/Azure.Storage`, `Aspire.StackExchange.Redis`, `Aspire.Azure.*`, `Aspire.Hosting.Testing` | `13.4.6` | Topology wiring reviewed against Microsoft Learn; broadly idiomatic. Single-replica edge SPOF (`mcp-hosts-build:PROD-501`) is a deployment choice, not a misuse. | Retain. Resolve the Orleans double-config; plan multi-replica. |
| `CommunityToolkit.Aspire.Hosting.Ollama`, `.OllamaSharp` | `13.4.0` | Local model hosting. Version lag vs Aspire 13.4.6. | Retain; align version. |

## AI / LLM abstractions (consolidation opportunity)

| Package | Version | Real consumer | Rec |
|---|---|---|---|
| `Microsoft.Extensions.AI` + `.OpenAI` | `10.7.0` | Primary `IChatClient` abstraction; used well (`AddChatClient`/`AsIChatClient`, bounded-concurrency+timeout delegating client, OTel sensitive-data off) — the LLM layer is the healthy part of Foundry (`foundry` LLM notes). | **Retain as the single abstraction.** |
| `Microsoft.Agents.AI` | `1.13.0` | `ChatClientAgent` for INO's generic chat path. Young, fast-moving (repo comment flags re-verify-before-bump). Doc-gap: could not verify via Context7. | Retain but treat bumps as breaking; pin. |
| `Anthropic` | `12.35.1` | First-party `AsIChatClient()` provider. | Retain. |
| `OpenAI` | `2.12.0` | Marketplace economics + provider. | **Consolidation candidate** — overlaps `Microsoft.Extensions.AI.OpenAI` and `Azure.AI.OpenAI`. Confirm each has a distinct consumer or collapse. |
| `Azure.AI.OpenAI` | `2.1.0` | Azure-hosted OpenAI. | Same consolidation review. |

**Rec:** three OpenAI-family packages + Anthropic + MEAI is a lot of surface. Verify each has a live, distinct consumer (grep-confirm) and consolidate to MEAI + one provider SDK per provider actually used.

## Roslyn (code generation/execution — security-relevant)

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `Microsoft.CodeAnalysis*` (incl. `CSharp.Scripting`) | `5.6.0` | Powers Foundry codegen/exec. **The security problem is architectural, not the package** — see [06](06-security-threat-model.md): in-process full-trust execution + bypassable gate. Doc-gap: exact 5.6.0 scripting API unverified (Context7 quota). | Retain package; **fix the execution model** (out-of-process). |

## Persistence / crypto / secrets

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `Azure.Extensions.AspNetCore.DataProtection.Blobs` | `1.5.3` | Cluster-wide key ring. **Persisted without `ProtectKeysWith*`** → unencrypted keys beside ciphertext (`kernel-hosting:SEC-200`, verified vs MS Learn). | Add key encryption (Key Vault / managed identity). |
| `Azure.Identity` | `1.21.0` | Managed-identity storage path. | Retain. |
| `Azure.Storage.Blobs` | `12.28.0` | Blob storage. | Retain. |
| `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` | `10.0.9` / `3.0.3` | Only consumer is `SqliteSchemaInspector` — **registered but unused dead code** (`kernel-hosting:CLEAN-101/200`). | **Delete package + code** unless a real consumer is added. |
| `ClosedXML` | `0.105.0` | Excel ingestion for chat attachments — consumer (`TabularDataParser`/`ChatUploadClassifier`) is **test-only dead code** with a zip-bomb exposure (`kernel-hosting:PERF-100`). Doc-gap: usage unverified. | **Delete** with the dead upload stratum, or wire + harden. |

## gRPC / protobuf

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `Grpc.AspNetCore` + `.Web`, `Grpc.Net.*`, `Grpc.Tools`, `Google.Protobuf` | `2.71.0` / `3.31.0` | V2 UI transport uses it correctly (bounded messages, detailed-errors off). **The legacy `digitalbrain.proto` is compiled `Both` but never served** (a test pins its absence) with three-way stub drift (`kernel-hosting:ARCH-100`). | Retain; **stop generating the dead proto**; delete it and the stale Dart stubs. |

## Observability

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `OpenTelemetry.*` (`1.16.0`/`1.15.x`), `Azure.Monitor.OpenTelemetry.AspNetCore` `1.5.0` | — | Correctly wired via ServiceDefaults; sensitive data off on the LLM path. | Retain. |

## Provider SDKs

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `Google.Apis.Gmail.v1` `1.74.0.4162`, `Drive.v3`/`Calendar.v3` `1.75.x`, `Google.Apis.Auth` `1.75.0` | — | Gmail used; **Drive/Calendar pulled in but not exercised** by shipped capabilities (verify consumers). Google OAuth lacks PKCE (`connectors:SEC-400`). Doc-gap: Auth usage unverified (Context7 quota). | Keep Gmail; **drop Drive/Calendar** unless wired; add PKCE. |
| `DeveloperForce.Force` | `2.1.0` | Salesforce REST client; best mutation pipeline in the repo. Doc-gap: usage unverified. | Retain. |
| `Newtonsoft.Json` | `13.0.4` | Pulled by Force SDK; otherwise prefer `System.Text.Json`. | Retain (transitive); don't add new usage. |

## Testing

| Package | Version | Assessment | Rec |
|---|---|---|---|
| `xunit` `2.9.3`, `xunit.runner.visualstudio` `3.1.5`, `Microsoft.NET.Test.Sdk` `18.6.0`, `coverlet.collector` `10.0.1` | — | Standard, correct. | Retain. |
| `Reqnroll` (+ xUnit + MsBuild) `3.3.4` | — | **Three packages for one vacuous BDD feature** (`dotnet-tests:TEST-600`). | **Delete Reqnroll** and the theatre feature, or write real scenarios. |
| `Aspire.Hosting.Testing` `13.4.6`, `Xunit.SkippableFact` `1.5.23` | — | AppHost model tests. | Retain. |

## Flutter / Dart

| Package | Constraint | Assessment | Rec |
|---|---|---|---|
| Flutter / Dart SDK | `>=3.41.0` / `^3.11.0` | Recent. macOS/Android platform config is broken for networking (`platform-and-skills:PROD-1000/1001`). | Fix entitlements/manifests. |
| `widgetbook` | **`any`** in **production `dependencies`** | Unpinned dev tool in prod deps — supply-chain + reproducibility risk (`flutter-sdk-and-tests:FRAME-900`). | **Pin + move to `dev_dependencies`.** |
| `rfw` | `^1.0.0` (latest 1.1.3) | Server-driven UI; dictionary is a genuine allowlist, but the action/URL surface is under-defended (`flutter-ui:SEC-800/802`). | Retain; constrain action surface. |
| `bloc`/`flutter_bloc` `^9.1.1`, `bloc_test` `^10.0.0` | — | State mgmt fine; `bloc_test` declared but **never used** (`flutter-sdk-and-tests:TEST-902`). | Use it or drop it. |
| `media_kit` + `media_kit_video` + `media_kit_libs_video`, `youtube_player_iframe`, `lottie`, `flutter_earth_globe` | — | Heavy media/visual deps; several anchor **dead visual code** (`flutter-ui:ARCH-800`, 13.3 MB Lottie `flutter-sdk-and-tests:CLEAN-900`). | Delete deps whose only consumers are dead widgets/assets. |
| `openid_client` `^0.4.10+1` | — | Web path uses deprecated **implicit flow** (`flutter-runtime:SEC-700`). | Move to auth-code+PKCE. |
| ~9 dead pubspec deps | — | `flutter-sdk-and-tests:FRAME-901`. | Delete. |

## Summary recommendations

- **Retain and standardize on:** MEAI (`IChatClient`), Orleans core, Aspire, gRPC (V2 only), OpenTelemetry, the connector SDKs actually used (Gmail, Force), xunit.
- **Highest-risk dependency:** Orleans **Journaling alpha** — plan a migration or a hardened wrapper; it is undocumented and underpins durability.
- **Deliberate decision needed:** `net11.0` preview toolchain for a product core.
- **Consolidate:** the OpenAI-family packages; verify distinct consumers.
- **Delete (dependency + dead consumer):** SQLite, ClosedXML, the dead proto generation, Reqnroll, `bloc_test` (if unused), ~9 dead Flutter deps, the media/lottie deps tied to dead widgets, Drive/Calendar SDKs if unwired.
- **Pin:** `widgetbook`, the `codegraph` npm build step (`mcp-hosts-build:SEC-500`), all `@latest` MCP/codex tools.
- **No framework churn recommended** beyond the above — the core framework choices (Orleans, Aspire, MEAI, gRPC, Flutter) are sound; the problems are usage, version discipline, and preview/alpha exposure, not the frameworks themselves.
