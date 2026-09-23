# IntoChat Product Delivery — Master Execution Plan (Phases 0–5b)

Execution ledger (kept current as tasks complete): `docs/superpowers/plans/2026-09-23-intochat-product-delivery-verification.md` (created at first execution, separate from this plan).

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for inline implementation, or `superpowers:subagent-driven-development` when the owner chooses delegation. Every task below is a checkbox-tracked work item. Do not start a task whose dependencies are not `done`. Do not batch phases.

**Goal:** Deliver every remaining planned IntoChat capability (C01–C17) and journey (J1–J6) across Phase 0 through Phase 5b, from the current baseline (`master @ 739c6c385`) to a hosted, metered, marketplace-bearing product, autonomously, with review gates at every integration boundary.

**Architecture:** The existing ~2,000-line Orleans neuron kernel (`src/Modules/DigitalBrain/Kernel/`) is the runtime. Everything else is layered on it: a single `AgentNeuron` assistant runtime, semantic types, a My Data vault with `SecretRef`, one live-table contract, durable receipts and a Compute ledger, accounts/principals/grants enforced at one call filter, manifests for every app, discovery on the Memory module's Qdrant, an Inbox, a hosting profile, and — last — a broker and marketplace. First-party modules keep code-first `WithModule<T>()` composition plus a generated manifest; no third-party or assistant-written code runs inside the silo.

**Tech Stack:** .NET 11 (SDK `11.0.100-rc.1.26425.128`, `net11.0`), C#, Orleans 10.3.1 (Journaling `10.3.1-alpha.1`), Aspire 13.5.3, Microsoft.Extensions.AI 10.9.0, Npgsql/PostgreSQL, Qdrant 1.19.0, Flutter (Dart) web + Windows, Microsoft.Testing.Platform + xunit.v3, Playwright 1.62.0, Azure Blob/Table storage, ModelContextProtocol 2.2.0, Stripe (Checkout/Tax/Connect — gated).

**Spec:** `docs/product/highlevel.md` (product definition, §8–§17) and `docs/product/current-state.md` (baseline). Evidence notes under `docs/product/research/2026-09-23-*.md`. The 6-lens review is `docs/product/.review-round1.tmp.md`. That file is **un-dispositioned review evidence**: it has no per-finding disposition column, so it cannot be claimed that all 143 round-one findings were accepted or that all remain unresolved. The spec change log says only that v0.2 applied a 6-lens review (143 findings) as *accuracy fixes*; the review file itself carries no resolution status. Before any phase implementation, the accuracy/owner decisions named there that this plan does not already encode must be reconciled against v0.2 and given an explicit accept/reject/defer disposition in the verification ledger. Do not claim "accepted corrections folded in" for any individual finding unless it is verified against v0.2. Where this plan names a mechanism, it is a *candidate* for that epic's ADR unless the spec already fixed it (T1–T11).

---

## 0. Scope, working assumptions, and hard gates

### 0.1 Working assumptions (explicitly not ratified facts)

The owner asked to deliver **all remaining planned features autonomously**. This plan therefore proceeds on the following **working assumptions**, which are the spec's own recommendations, not ratified decisions:

- **D1–D6 as recommended** (§12 of the spec): operator-first B2B ICP with Supabase/Postgres; hosted dedicated single-silo deployment per design partner; declarative-by-default UI with one Form neuron per window and C# behaviors behind developer mode; typed values as fields of entity neurons with declared tiers and Journaling unwired; manifest-first apps with kinds `declarative`/`remote`/`process`; My Data = owner's facts/secrets only, business data belongs to the workspace/app.
- **D7–D17 and T1–T11 at their recommended values.** D16 (selling entity/markets), D9 (take rate), D8 (margin) remain *targets to ratify*, not settled.
- **Capacity assumption:** one owner reviewing at most 2 epics at once; AI coding agents implement. Appetites are owner-review weeks.
- **No invented facts.** Anywhere the spec says "counsel to confirm", "proposed", "not yet assessed", or names an unverified provider behaviour, this plan keeps it as a gate (Section 0.3).
- If the owner changes a working assumption, the affected tasks are re-planned; the gates do not move.

### 0.1a Review reconciliation required before phase implementation

`docs/product/.review-round1.tmp.md` is un-dispositioned. These review items must be explicitly reconciled against highlevel v0.2 and given an accept/reject/defer disposition in the verification ledger before the task that consumes them starts. This list is not exhaustive; it names the accuracy/owner decisions that the plan currently leaves open:

- **Capability-count accuracy** — the review's headline count correction (14 of 15 capabilities Missing/Partial) must match the v0.2 capability map before §6.1 is treated as truth.
- **C09 reachability cause** — whether ClickHouse/Gmail/GitHub are "no tools" vs "blocked by an unregistered MCP bridge" changes P2.3's acceptance.
- **Owner-format decision (distribution-first signed packs)** — the review's owner-fidelity finding says the owner's own survey recommends signed C# packs loaded into the running cluster, which conflicts with this plan's non-goal. Ratify or reject before P2.6/P4.x.
- **Marketplace/prior-art framing** — the review's D5/churn findings affect C13 and Phase 4 entry language.
- **C05 elicitation wording** — flat-subset-only and Secret-incompatible wording must be reflected before P1.1 acceptance.
- **Tier/Journaling ADR citation** — the review's accuracy finding on the 09-19 decision being superseded must be reflected before P0.3.
- **Kernel MCP removal** — confirm the review's trash-register items (Reqnroll rule, duplicate `ProcessRunner`, Downloads exposure, brain-events retry) match current source before deleting (see P0.1/P0.3 and Phase 0 exit).

### 0.2 Scope rules

- Scope is the full spec through **P5b**, not narrowed to P0–P1.
- Every epic names what it **deletes** (Principle 2). Deletions are part of done work.
- Recommendations named in tasks are **candidate mechanisms** and may be replaced by the epic's ADR without changing the outcome or acceptance criteria.
- One intent id keys receipt, trace, approval and statement line (Principle 1).

### 0.3 Hard gates — cannot be satisfied by code alone

These block the phase or task named and must be closed by the owner/legal/provider, never faked:

| # | Gate | Blocks | Closure evidence required |
|---|---|---|---|
| G-1 | **D16** — contracting entity, its country, launch markets, customer type (B2B only until P5) | any Phase 3 epic; Stripe account; VAT/OSS + US sales-tax decisions | Signed decision record in `docs/product/decisions/` |
| G-2 | **Stripe written confirmation** under its restricted-business policy that selling Compute is acceptable; D9 take rate decided; Connect payout regions | Phase 3 billing/invoices (postpaid, D7); Phase 4 payouts (T10). Prepaid wallet/top-ups are **not** blocked here — they arrive only at P5a self-serve sign-up | Written confirmation on file |
| G-3 | **Counsel sign-off**: Compute/payments law (EU PSD2→PSD3, UK PSRs, US money transmission), VAT treatment of Compute, merchant-of-record obligations, EU P2B/DSA scope | Phase 3 epic *Spend limits & billing*; Phase 4 *Marketplace* | Counsel memo per topic |
| G-4 | **Data-protection readiness**: pilot terms, privacy notice, processor DPA, sub-processor list with transfer mechanism, retention rule | first design partner connecting production data (Phase 1 exit) | Signed/ published artifacts |
| G-5 | **Design partners**: ≥3–5 signed letters of intent naming a data source; A1/A9 pass | A1/A9, Phase 1 partner sessions, Phase 2/3 exits | Signed LOIs / pilot agreements |
| G-6 | **LeadGenerator source review (D15)**: source API terms (places/map caching, LinkedIn), GDPR Art. 14 notice, outreach law (e.g. Germany UWG §7) | Phase 2 *LeadGenerator* | Counsel memo |
| G-7 | **Live-model provider credentials/accounts** for nightly golden journeys and provider reconciliation | A8, golden-prompt gates | Provider keys in private config |
| G-8 | **Penetration test of the cross-platform sandbox** | Phase 5b only | Test report |
| G-9 | **Tax adviser view on VAT** of Compute | Phase 3 exit | Written advice |
| G-10 | **Assumption tests A1–A9** (highlevel §13) that are demand/market tests | phase exits as listed | Evidence in verification ledger |

Until each gate closes, the corresponding task is `blocked`, not `done`, and its dependent features may be built behind a feature flag or on fakes but must not be exercised on a real partner/customer.

---

## 1. How this plan is executed — OpenCode CLI orchestration

Execution uses **OpenCode CLI** as the worker runtime. One **work item at a time per worker branch**, dispatched from this plan into an OpenCode session.

1. **Selection.** Take the highest-priority unblocked task whose dependencies are `done`. Respect WIP ≤ 2 epics in progress (§16 of the spec).
2. **Worker launch.** Dispatch the task prompt (goal, Files, Interfaces, Steps, acceptance) to an OpenCode CLI worker. Headless: `opencode run "<task prompt>"`; interactive: the `opencode` TUI with the task loaded. Each worker runs in its **own git worktree/branch** (`worktree/<TASK-ID>`), never on a shared tree. Verify CLI flags against `opencode --help` before scripting; do not invent flags.
3. **Parallel workers only for independent tasks.** Tasks may run concurrently only when Section 3's dependency graph marks neither as depending on the other *and* they touch disjoint files. The concurrency map in §3.3 lists the safe parallel sets. All other tasks are sequential.
4. **Integration/review gate.** Each finished branch is integrated only after: `aspire do build` and `aspire run` healthy; targeted tests green at high severity; full affected E2E green; deletion list present; migration named (T1); verification ledger updated; code review passed (per owner's code-review rule). A gate failure returns the task to `in-progress`; it does not reopen done work.
5. **Phase gate.** A phase closes only when every exit criterion in Section 4 (mirroring spec §11) is met and the phase's gate row(s) in §0.3 are closed or explicitly waived by the owner.

### 1.1 Definition of Done (applies to every task)

- `aspire do build` succeeds and `aspire run` reaches healthy (or the task's module `TestCluster` equivalent).
- Tests green at **high severity**, including the Aspire integration tests; the task's named test class(es) pass.
- Code review done; deletions listed; persisted-state migration named (T1) or the clean-break ADR cited.
- Verification ledger row updated with commands, results, and evidence links.
- Docs-only tasks need review + docs-lint only.

### 1.2 Canonical test commands

```powershell
# Full solution (CI shape). --ignore-exit-code 8 is the repo CI zero-test handling flag
# (see .github/workflows/ci.yml:70); without it a project that discovers no tests fails the run.
dotnet test --solution DigitalBrain.slnx --max-parallel-test-modules 1 -p:CodeGraphRefresh=false --ignore-exit-code 8

# A module's unit tests — concrete projects (module unit tests follow the real path pattern
# src/Modules/Group/Module/Tests/Unit/DigitalBrain.Modules.Module.Tests.Unit.csproj):
dotnet test --project src/Modules/AI/Tests/Unit/DigitalBrain.Modules.AI.Tests.Unit.csproj -p:CodeGraphRefresh=false --ignore-exit-code 8
dotnet test --project src/Modules/Google/Flutter/Tests/Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false --ignore-exit-code 8
dotnet test --project src/Modules/DigitalBrain/Kernel/Tests/Unit/DigitalBrain.Runtime.Tests.Unit.csproj -p:CodeGraphRefresh=false --ignore-exit-code 8

# Application E2E (hosts the real AppHost + Playwright)
dotnet test --project src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj -p:CodeGraphRefresh=false --ignore-exit-code 8

# Live-model golden journeys (nightly; requires G-7)
$env:DIGITALBRAIN_E2E_LIVE_MODEL=1; $env:DIGITALBRAIN_E2E_MODEL_API_KEY="<key>"
dotnet test --project src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj -- --filter-class '*GoldenJourney*'

# Playwright browser binaries (once per machine; script path is produced by the E2E build)
pwsh src/Applications/IntoChat/Tests/E2E/bin/Release/net11.0/playwright.ps1 install chromium

# Aspire lifecycle (owner rule: build → run → test)
# Syntax verified against `aspire --help`, `aspire do --help`, `aspire run --help`,
# `aspire wait --help` and `aspire docs get aspire-do-command` / `aspire-wait-command` (CLI 13.5.4):
#   * `aspire do <step>` requires a step name; confirm the exact step with `aspire do --list-steps`
#     before running. `aspire do build` fails fast if no step by that name is registered.
#   * `aspire wait` requires a resource name and targets a *running* AppHost. Start detached first.
aspire do build
aspire run --detach
aspire wait IntoChat --status healthy
aspire stop
```

---

## 2. Cross-cutting engineering constraints

These apply to every task unless the task says otherwise.

**Security & privacy.**
- The model never receives a secret; secrets travel by reference (`SecretRef`), resolved only inside the neuron that makes the outbound call. No secret in chat, signals, traces, logs, vectors, journals or read results.
- Personal and `SpecialCategory`/`Credential` values are redacted by sensitivity class, never indexed.
- Permissions, approvals and limits are enforced at the neuron boundary by **one** call filter; no rule lives only in a prompt.
- `/ui` routes become workspace-scoped; `GET /ui/textfields/{name}` and the other unscoped value routes are removed (C02/C06).
- The canary-secret test (T-CANARY) is a feature-add gate from Phase 1.

**Observability.**
- **One** Orleans propagation registration, framework logs at `Warning`, idle shell < 20 spans/min as a CI check, ~15–45 spans per intent carrying `intochat.*` attributes. A source grep finds exactly one `silo.AddActivityPropagation()` today (`src/Modules/DigitalBrain/Kernel/Aspire/DigitalBrainRuntimeHostingExtensions.cs:40`); P0.4 must first *measure* duplicate spans and locate the real source across Aspire wiring plus explicit instrumentation, then delete only a verified duplicate — not assume the call is duplicated.
- Every LLM/embedding call site emits GenAI spans with token classes; MCP and Npgsql trace sources on; trace context passed into child behavior processes.
- Receipts are durable records, **never** derived from sampled traces (Principle 8).

**Migration (T1).**
- Until the first design partner is onboarded, a breaking state change may use a clean break (new storage container name) recorded in an ADR.
- After that: every change to a persisted type or grain key ships with a versioned migration and a restore-tested backup; serialized members change only by adding/removing `[Id]`s, never by changing a member's type.
- The concrete migration queue: string kinds → enums; neuron keys → workspace-scoped grain ids; Salesforce/Gmail tokens → vault; `IConversation` → `AgentNeuron`; local-disk JSON → cluster storage; plaintext blobs → encrypted fields; Qdrant volume.

**Operations.**
- One silo per deployment until a two-silo test proves cross-silo signals and login handoff (T6).
- Hosted deployments use the product AppHost profile only; the Windows developer executor is disabled there.
- Nightly backups of grain storage, ledger and vector collections; quarterly restore drill (RPO 24 h, RTO 4 h); `/agent` + window-read SLO 99.5 % monthly.

---

## 3. File map, consumers/producers, and dependency order

### 3.1 Workstreams and exact paths

| WS | Capability | Create | Modify | Test (new/extended) |
|---|---|---|---|---|
| **WS-A** Assistant runtime | C01 | `src/Modules/AI/AI/Agents/AgentRuntimeTools.cs`; `src/Applications/IntoChat/IntoChat/Agent/AgentReceipts.cs` | `src/Applications/IntoChat/IntoChat/Agent/AgentEndpoints.cs`; `src/Applications/IntoChat/IntoChat/Agent/ConversationCoordinator.cs` (delete); `src/Modules/AI/AI/Agents/AgentNeuron.cs`; `src/Modules/AI/AI/Agents/AgentTurnRunner.cs`; `src/Applications/IntoChat/IntoChat/Program.cs` | `src/Modules/AI/Tests/Unit/Agents/AgentRuntimeFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Agent/AgentWorkflowFacts.cs` |
| **WS-B** Workspace & instant UI | C02 | `src/Modules/Google/Flutter/Contracts/Form/IForm.cs`; `src/Modules/Google/Flutter/Flutter/Form/FormNeuron.cs`; `src/Applications/IntoChat/IntoChat/Agent/WorkspaceFormTools.cs`; `src/Modules/Google/Flutter/app/ui/lib/src/composition/renderer_registry.dart` | `src/Modules/Google/Flutter/app/ui/lib/src/composition/neuron_view.dart`; `src/Modules/Google/Flutter/app/ui/lib/src/models/ui_part.dart`; `src/Modules/Google/Flutter/Flutter/Workspace/WorkspaceNeuron.cs`; `src/Applications/IntoChat/IntoChat/Apps/AppSurfaceComposer.cs` | `src/Modules/Google/Flutter/Tests/Unit/Form/FormFacts.cs`; `src/Modules/Google/Flutter/Tests/E2E/Form/FormHttpFacts.cs`; `app/ui/test/composition/form_renderer_test.dart`; `app/ui/test/composition/renderer_registry_test.dart` |
| **WS-C** Receipts/traces | C03 | `src/Modules/Receipts/Contracts/`, `src/Modules/Receipts/Receipts/`; `docs/product/decisions/` | `src/Modules/DigitalBrain/Kernel/Aspire/DigitalBrainRuntimeHostingExtensions.cs`; `src/Modules/AI/AI/Clients/AIClients.cs`; `src/Applications/IntoChat/ServiceDefaults/ServiceDefaultsExtensions.cs`; `src/Modules/Coding/Coding/Validation/WindowsContainedProcess.cs`; `src/Modules/DigitalBrain/Behaviors/Behavior/Execution/LocalBehaviorExecutor.cs` | `src/Modules/Receipts/Tests/Unit/ReceiptFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Receipts/ReceiptJourneyFacts.cs`; trace-budget CI test |
| **WS-D** My Data & secrets | C04 | `src/Modules/MyData/Contracts/`, `MyData/`, `Aspire.Hosting/` (consumes `SecretRef` owned by WS-E/P1.1) | `src/Modules/Salesforce/Salesforce/Salesforce/SalesforceNeuron.cs`; `src/Modules/Salesforce/Salesforce/Salesforce/SalesforceState.cs`; `src/Modules/Google/Gmail/Gmail/Gmail/GmailNeuron.cs` | `src/Modules/MyData/Tests/Unit/VaultFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Security/CanarySecretFacts.cs` |
| **WS-E** Semantic types | C05 | `src/Modules/DigitalBrain/Kernel/Contracts/Types/` (`SemanticType.cs`, `TypeCatalog.cs`, `FieldKind.cs`, `SchemaExtensions.cs`); `Types/TypeCatalog.v0.json` | `src/Modules/Supabase/Contracts/Table/SupabaseTableColumn.cs`; `src/Modules/Google/Flutter/Contracts/TextField/ITextField.cs`; `src/Modules/AI/Contracts/Agents/AgentDefinition.cs` | `src/Modules/DigitalBrain/Kernel/Tests/Unit/Types/TypeCatalogFacts.cs`; `src/Modules/Google/Flutter/Tests/Unit/Composition/TypeParityFacts.cs` |
| **WS-F** Identity/consent | C06 | `src/Modules/Identity/Contracts/`, `Identity/`, `Aspire.Hosting/`; `src/Modules/DigitalBrain/Kernel/Kernel/Enforcement/` | `src/Applications/IntoChat/IntoChat/Auth/BasicAuthGate.cs`; `src/Applications/IntoChat/IntoChat/Workspace/WorkspaceScope.cs`; `src/Applications/IntoChat/IntoChat/Program.cs` | `src/Modules/Identity/Tests/Unit/GrantFacts.cs`; `src/Modules/DigitalBrain/Kernel/Tests/Unit/EnforcementFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Security/CrossWorkspaceFacts.cs` |
| **WS-G** Compute | C07 | `src/Modules/Compute/Contracts/`, `Compute/`; `src/Modules/Compute/Compute/PriceBook/PriceBook.cs`, `pricebook.v0.json` (single price book — P1.4 creates it, P2.2 extends the same module) | `src/Modules/AI/AI/Clients/AIClients.cs`; `src/Modules/Google/Flutter/Flutter/Workspace/WorkspaceNeuron.cs` | `src/Modules/Compute/Tests/Unit/MeterFacts.cs`; `src/Modules/Compute/Tests/Unit/LedgerFacts.cs` |
| **WS-H** Discovery/memory | C08 | `src/Modules/Discovery/Contracts/`, `Discovery/`; `src/Applications/IntoChat/IntoChat/Agent/DiscoveryTools.cs` | `src/Modules/Memory/Memory/Memory/MemoryNeuron.cs`; `src/Modules/Memory/Memory/Memory/Qdrant/QdrantVectorMemoryProvider.cs` | `src/Modules/Discovery/Tests/Unit/CatalogFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Discovery/DiscoveryGateFacts.cs` |
| **WS-I** Connections | C09 | `src/Modules/Connections/Contracts/`, `Connections/` | `src/Applications/IntoChat/IntoChat/Agent/SupabaseWorkspaceTools.cs`; `src/Modules/Supabase/Supabase/Table/SupabaseTableNeuron.cs`; `src/Modules/ClickHouse/ClickHouse/Tables/ClickHouseTableNeuron.cs`; `src/Modules/AI/AI/Tools/McpAgentTools.cs` | `src/Modules/Connections/Tests/Unit/ConnectFlowFacts.cs`; `src/Modules/Supabase/Tests/Unit/Table/SupabaseTableReadFacts.cs` |
| **WS-J** Apps/manifests | C10 | `src/Modules/Apps/Contracts/`, `Apps/`; `src/Apps/*/app.json` | `src/Modules/DigitalBrain/Kernel/Kernel/Composition/ModuleConfigurationContract.cs`; `src/Applications/IntoChat/IntoChat/Apps/AppEndpoints.cs` | `src/Modules/Apps/Tests/Unit/ManifestFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Apps/SaveAsAppFacts.cs` |
| **WS-K** Automations | C11 | `src/Modules/Automations/Contracts/`, `Automations/` | `src/Modules/Time/Time/Reminders/ReminderNeuron.cs`; `src/Applications/IntoChat/IntoChat/Behavior/BehaviorEndpoints.cs` | `src/Modules/Automations/Tests/Unit/JobFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Automations/AutomationRestartFacts.cs` |
| **WS-L** Isolation/broker | C12 | `src/Modules/Broker/Contracts/`, `Broker/`; `src/Modules/DigitalBrain/Kernel/Contracts/Enforcement/ICallFilter.cs` | `src/Applications/IntoChat/IntoChat/Behavior/BehaviorToolService.cs`; `src/Modules/DigitalBrain/Kernel/Kernel/Hosting/BrainHosting.cs` | `src/Modules/Broker/Tests/Unit/GatewayFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Security/WorkerGatewayDeniedFacts.cs` |
| **WS-M** Marketplace | C13 | `src/Modules/Marketplace/Contracts/`, `Marketplace/` | `src/Modules/Apps/Apps/`; `src/Modules/Compute/Compute/` | `src/Modules/Marketplace/Tests/Unit/CertificationFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Marketplace/PublishRemoteFacts.cs` |
| **WS-N** Runtime/durability | C14 | `docs/product/decisions/0000-stored-state-migration.md` | `src/Modules/DigitalBrain/Kernel/Kernel/Neuron.cs`; `src/Modules/DigitalBrain/Kernel/Aspire/AzureOrleansJournalHosting.cs` (delete); `Directory.Packages.props`; `global.json`; `src/Modules/Google/Flutter/Flutter/Workspace/WorkspaceNeuron.cs`; `src/Modules/Coding/Coding/Drafts/CodeDraftStore.cs`; `src/Modules/DigitalBrain/Behaviors/Behavior/Programs/BehaviorProgramStore.cs`; `src/Applications/IntoChat/IntoChat/Behavior/BehaviorCatalogStore.cs`; `src/Modules/DigitalBrain/Behaviors/Behavior/Execution/BehaviorLogStore.cs`; `src/Modules/Memory/Aspire.Hosting/Configuration/QdrantHostingOptions.cs` | `src/Modules/DigitalBrain/Kernel/Tests/Unit/PersistenceFacts.cs`; two-silo `TestCluster` test; restore-from-backup test |
| **WS-O** Dev kit & decisions | C15 | `docs/product/decisions/`, `docs/product/epics/`, `docs/operations/` | `CONTEXT.md`; `README.md`; `continuation.md`; `.github/workflows/ci.yml`; `src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml`; `src/Applications/IntoChat/IntoChat/Dockerfile`; `src/Applications/IntoChat/IntoChat/docker-entrypoint.sh` | docs-lint; `src/Modules/DigitalBrain/Kernel/Tests/Unit/ManifestCompletenessFacts.cs` |
| **WS-P** Inbox | C16 | `src/Modules/Inbox/Contracts/`, `Inbox/` | `src/Modules/Google/Flutter/Flutter/Inbox/InboxNeuron.cs`; `src/Modules/Google/Flutter/Flutter/Inbox/InboxEndpoints.cs`; `app/shell/lib/inbox_banner.dart` (delete) | `src/Modules/Inbox/Tests/Unit/InboxFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Inbox/StructuredInboxFacts.cs` |
| **WS-Q** Operations | C17 | `src/Applications/IntoChat/AppHost/Profiles/`; `docs/operations/runbook.md`; OTel Collector config `ops/otel/collector.yaml` | `src/Applications/IntoChat/AppHost/AppHost.cs`; `src/Applications/IntoChat/AppHost/IntoChat.AppHost.csproj`; `src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml`; `src/Applications/IntoChat/IntoChat/Dockerfile`; `src/Applications/IntoChat/IntoChat/docker-entrypoint.sh` | `src/Applications/IntoChat/Tests/E2E/Hosting/RestoreDrillFacts.cs` (harness-gated) |
| **WS-R** First-party apps | C02/C09/C10 | `src/Apps/customer-tables/app.json`; `src/Apps/forms/app.json`; `src/Apps/mydata/app.json`; `src/Apps/leadgenerator/app.json`; `src/Apps/image-editor/app.json` | `src/Applications/IntoChat/IntoChat/LocalFiles/*`; `src/Applications/IntoChat/IntoChat/Apps/*` | `src/Applications/IntoChat/Tests/E2E/Apps/FirstPartyManifestFacts.cs` |
| **WS-S** Trash & truth | all | — | see Task P0.1/P0.2 | `src/Applications/IntoChat/Tests/Unit/HygieneFacts.cs` |

### 3.2 Producers/consumers (key seams)

- `AgentNeuron` (WS-A) **produces** `AgentTurnEvent` and usage; WS-C **consumes** them to write receipts; WS-G **consumes** usage for meters.
- `TypeCatalog` (WS-E) **produces** schemas; WS-B renderer, WS-A tool schemas, WS-I table columns and WS-J manifests **consume** them.
- `ICallFilter` (WS-F/WS-L) **consumes** caller context stamped at trusted edges; WS-G **feeds** allowance checks into the same filter.
- The `SecretRef` **type** is owned by WS-E/P1.1; **values** are produced by My Data (WS-D) and consumed only inside the outbound-call neuron; WS-I connection tokens move behind it.
- `app.json` manifest (WS-J) **feeds** WS-H discovery, WS-M listings, WS-A tool schemas and WS-B launcher.
- `ReminderNeuron` (WS-K) **produces** durable ticks; Jobs **consume** them idempotently.
- `IInbox` (WS-P) **consumes** events from automations, Compute alerts, approvals, connections, kill switch.

### 3.3 Dependency graph and safe parallelism

```
P0: P0.1 dead code → P0.3 profiles/journal → P0.4 tracing → P0.5 metering → P0.6 capture.
    Parallel to P0.1: P0.2 (docs/CI/Docker truth) and P0.7 (WorkspaceNeuron rename).
    P0.8 (behavior tools developer-only) runs after P0.1, concurrent with P0.3 (disjoint files).
P1: P1.1 (types + SecretRef owner) → {P1.5, P1.6, P1.7};
    P0.5 + P1.1 → P1.2 → {P1.3, P1.4}; P1.5 → P1.9; {P1.3, P1.5} → P1.8.
P2: P2.1 → {P2.2, P2.3, P2.4, P2.6}; P2.4 → P2.5;
    {P2.2, P2.6} → {P2.7, P2.9}; P2.7 → P2.8; {P2.7, P2.8, P2.9} → P2.10.
P3: P3.1 needs P2.2 + P2.5; P3.2 needs P3.1 + P2.10.
P4: P4.1 needs P2.1 + P2.6; P4.2 needs P4.1 + P3.1; P4.3 needs P4.2.
P5a: P5a.1 needs P4.2 + P3.1.
P5b: P5b.1 needs P4.1 + P4.2, after G-8 penetration test.
```

**Safe parallel sets** (task ids; disjoint files *and* no dependency between members). Every other pair is sequential.

- **P0:** `{P0.1, P0.2, P0.7}`. P0.7's Dart scope is label strings only, in files *not* listed under P0.1 (`workspace_app.dart`, `workspace_routes.dart`, `ui_client.dart` belong to P0.1). P0.8 may join once P0.1 is done (it edits `Agent/ConversationCoordinator.cs`, which no other P0 task touches). Kicked out of any parallel set: P0.3 and P0.4 share `DigitalBrainRuntimeHostingExtensions.cs`; P0.4, P0.5 and P0.6 share `AIClients.cs` (and P0.6 also shares `ServiceDefaultsExtensions.cs` with P0.4); P0.3 and P0.1 share `AppHost.cs`. Serial order inside P0: P0.3 → P0.4 → P0.5 → P0.6.
- **P1:** after P1.1, `{P1.5, P1.6, P1.7}` are disjoint (Form, MyData, workspace-scoping). P1.3/P1.4 are disjoint after P1.2. P1.9 follows P1.5 (shared renderer files). P1.1 owns `SecretRef.cs`; P1.6 consumes it.
- **P2:** after P2.1, `{P2.2, P2.3, P2.4, P2.6}` are disjoint. `{P2.7, P2.9}` may run together after P2.2 + P2.6. **No `{WS-Q, WS-N-persistence}` set**: P2.5 depends on P2.4 and both touch the persistence/hosting story, so they are sequential.
- **P3–P5b:** sequential per the graph above.

**Never parallelize tasks that both edit `AIClients.cs`, `AppHost.cs`, `Program.cs`, `WorkspaceNeuron.cs`, `DigitalBrainRuntimeHostingExtensions.cs`, `ServiceDefaultsExtensions.cs` or `Directory.Packages.props`.** `DigitalBrainRuntimeHostingExtensions.cs` is in no parallel set.

---

## 4. Phases, epics and tasks

Each task is feature-sized: one reviewable branch. "Acceptance" is the observable outcome; the test command proves it.

### Phase 0 — Clear the desk (2 weeks · C01 C02 C03 C07 C14 C15)

**Epic E0.1 — Trash & truth (WS-S, WS-O).** Deletions and doc truth, with net lines reported.

**Task P0.1 — Delete dead server and client code.**
- **Files (delete):** `src/Applications/IntoChat/IntoChat/Http/ActivityEndpoints.cs`, `EdgeResults.cs`, `GraphEndpoints.cs`, `NeuronNameFilter.cs`, `Http/Projections/**`, `SessionNeuron.cs`, `SessionStream.cs`, `SurfaceEndpoints.cs`, `TableEndpoints.cs`, `UiEndpoints.cs`, `WorkspaceAgentTools.cs`, `WorkspaceArtifactStore.cs`, `Http/WorkspaceEndpoints.cs`, `IntoChatHost.cs`; `src/Modules/AI/AI/ConversationalAgent.cs`; the Kernel MCP host `src/Modules/DigitalBrain/Kernel/Mcp/` (`DigitalBrain.Mcp.csproj`); the empty `src/Modules/Microsoft/Aspire/Contracts/`; Dart: `app/shell/lib/workspace/workspace_programs.dart`, `artifact_editors.dart`, `workspace_table_import.dart`, `brain_graph_store.dart`, `workspace_brain_observation.dart`, `graph_artifact.dart`, `workspace_voice*.dart`, `inbox_banner.dart`.
- **Modify:** `src/Applications/IntoChat/IntoChat/IntoChat.csproj` (remove the `Compile Remove` block only — **there is no `DigitalBrain.Mcp` `<ProjectReference>` in this file; do not claim one**); `DigitalBrain.slnx` (remove the `<Project Path="src/Modules/DigitalBrain/Kernel/Mcp/DigitalBrain.Mcp.csproj" />` entry at line 34); `.gitignore` (remove the stale Reqnroll rule at lines 33–34, `# Reqnroll code-behind…` + `*.feature.cs`, since the repo has zero `.feature`/Reqnroll references); `app/shell/lib/workspace/workspace_app.dart` (`_newWorkMenu`, `_navigateRoute` `programs` branch, `InboxBanner` wiring); `workspace_routes.dart`; `app/core/lib/src/ui_client.dart` (remove dead `programmingRequest`, artifact CRUD, `transcribeVoice`).
- **Consolidate duplicate `ProcessRunner`:** `src/Modules/Coding/Coding/ProcessRunner.cs` + `IProcessRunner.cs` duplicate `src/Modules/Microsoft/DotNet/DotNet/Process/ProcessRunner.cs` + `IProcessRunner.cs` (111 identical lines). Keep one shared implementation, delete the other, and update `src/Modules/Coding/Coding/CodingModule.cs:39` / `src/Modules/Microsoft/DotNet/DotNet/DotNetModule.cs:13` registrations. If the correct shared owner is ambiguous, record the decision as an ADR in `docs/product/decisions/` before deleting.
- **Move:** `src/Behaviors/ElonBitcoin.cs` and `src/Behaviors/Fakes/TwitterFakes.cs` to a test host: `src/Testing/DigitalBrain.Testing.E2E/Demo/` and remove `TestTwitterModule`/`AddBehavior<ElonBitcoin>` from `AppHost.cs`/`BehaviorEndpoints.cs`.
- **Test:** `dotnet test --solution DigitalBrain.slnx --max-parallel-test-modules 1 -p:CodeGraphRefresh=false --ignore-exit-code 8` (compile proves no live caller broke); `dotnet test --project src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj -p:CodeGraphRefresh=false --ignore-exit-code 8`; `ContainedProcessFacts` + `DotnetRunnerFacts` green after consolidation.
- **Acceptance:** solution builds; `aspire run` healthy; zero references to deleted symbols; `DigitalBrain.Mcp` gone from `DigitalBrain.slnx` and the tree; net line delta reported in the ledger.
- **Dependencies:** none. **Gate:** none.

**Task P0.2 — Tell the truth: glossary, ADRs, CI and project paths.**
- **Files:** create `docs/product/decisions/` (one MADR per ratified 09-19..22 decision and per D1–D6 working assumption), `docs/product/epics/README.md` + `_templates/`; rewrite `CONTEXT.md` from code; add "superseded" banner or rewrite `README.md`, `continuation.md`; fix `.github/workflows/ci.yml`, `src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml`; correct stale test project references: both Flutter test csprojs currently climb one parent too far for `src/Testing/...`, and `src/Modules/Coding/Tests/Unit/DigitalBrain.Modules.Coding.Tests.Unit.csproj` points to the removed `src/Modules/Flutter/Contracts/` instead of the moved `src/Modules/Google/Flutter/Contracts/`; fix these as CI/test path truth. Fix `src/Applications/IntoChat/IntoChat/Dockerfile` (remove the `DigitalBrain.Mcp` publish stage at line 46 and the stale `src/Modules/DigitalBrain/Mcp/...` path; correct the `DigitalBrain__Modules__*` list to the live AppHost chain) and `src/Applications/IntoChat/IntoChat/docker-entrypoint.sh` (drop the MCP child process); correct stale spec status lines.
- **Test:** docs-lint (add a CI step) + `src/Modules/DigitalBrain/Kernel/Tests/Unit/ManifestCompletenessFacts.cs` (each product module declares a `[ModuleConfiguration]` contract).
- **Acceptance:** `CONTEXT.md` describes only existing concepts; no CI, publish or container path points at moved/deleted folders (`src/Modules/Flutter`, `src/Modules/DigitalBrain/Mcp`); ADR register lists every decision.
- **Dependencies:** none (packaging edits do not require P0.1 at file level; both are in Phase 0).

**Epic E0.2 — Lean profile, telemetry hygiene, usage capture (WS-A, WS-C, WS-N).**

**Task P0.3 — Product/developer AppHost profiles; unwire Orleans Journaling; demos out; host Downloads off by default.**
- **Files:** `src/Applications/IntoChat/AppHost/AppHost.cs`, `ProductSurfaceResources.cs`; delete `src/Modules/DigitalBrain/Kernel/Aspire/AzureOrleansJournalHosting.cs` and its call in `DigitalBrainRuntimeHostingExtensions.cs`; remove the `storage.AddBlobs(DigitalBrainNames.Journal)` line from `src/Modules/DigitalBrain/Kernel/Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs:57`; remove `Microsoft.Orleans.Journaling*` from `Directory.Packages.props`. Also remove the default host-Downloads root at `src/Applications/IntoChat/AppHost/AppHost.cs:95` (the `"Downloads"` fallback under `IntoChat:LocalFiles:Roots:downloads`) so the Files app is **off by default** and only exposes a root when `IntoChat:LocalFiles:Roots:downloads` is explicitly configured.
- **Test:** `dotnet test --project src/Modules/DigitalBrain/Kernel/Tests/Unit/DigitalBrain.Runtime.Tests.Unit.csproj -p:CodeGraphRefresh=false --ignore-exit-code 8` (new `PersistenceFacts.JournalingPackageIsNotReferenced`); a new `HygieneFacts` case asserts no Downloads root is registered unless configured; `aspire run --detach` + `aspire wait IntoChat --status healthy`.
- **Acceptance:** product profile starts with only user-path modules; Journaling is not a startup dependency; no host `Downloads` path is exposed by default; developer profile keeps Coding/Behavior/Roslyn/DotNet.
- **Dependencies:** P0.1. **Gate:** none.

**Task P0.4 — Trace propagation audit, quiet logs, span budget, GenAI spans, all token classes.**
- **Files:** `src/Modules/DigitalBrain/Kernel/Aspire/DigitalBrainRuntimeHostingExtensions.cs` — **do not assume a duplicate `AddActivityPropagation()` call**: a source grep finds exactly one (`src/Modules/DigitalBrain/Kernel/Aspire/DigitalBrainRuntimeHostingExtensions.cs:40`). First **measure**: export one idle and one `/agent` trace and count spans per grain call; then locate the real duplication source across Aspire's `AddActivityPropagation` (which already subscribes Orleans diagnostics), the explicit `AddSource`/`WithTracing` list in ServiceDefaults, and any additional instrumentation. Remove only a **verified** duplicate source; if measurement finds none, close the task by recording the measurement and keep exactly one registration. `src/Applications/IntoChat/ServiceDefaults/ServiceDefaultsExtensions.cs` (framework logs `Warning`, subscribe `Npgsql` + MCP sources); `src/Modules/Coding/Coding/Validation/WindowsContainedProcess.cs` + `src/Modules/DigitalBrain/Behaviors/Behavior/Execution/LocalBehaviorExecutor.cs` + `src/Modules/DigitalBrain/Behaviors/Runtime/BehaviorApp.cs` (propagate W3C trace context into the child env); `src/Modules/AI/AI/Clients/AIClients.cs` (GenAI `UseOpenTelemetry` on every call site); `ModelProfiles.CreateInferenceClient` routes through `BuildChatPipeline`.
- **Test:** new `src/Applications/IntoChat/Tests/E2E/Diagnostics/TraceBudgetFacts.cs`: idle < 20 spans/min and J1 ≤ 25 spans; a concrete regression check asserts one grain call emits exactly 2 spans and that exactly one Orleans propagation source is registered (fails if a second is added); existing `AgentWorkflowFacts` still green.
- **Acceptance:** one trace per intent; a framework/grain call emits 2 spans, not 4; behavior worker spans share the parent trace; the measurement and the removed source (or the proof none exists) are recorded in the verification ledger.
- **Dependencies:** P0.1. **Gate:** none.

**Task P0.5 — Metering decorator captures all token classes per intent.**
- **Files:** create `src/Modules/AI/AI/Metering/MeteringChatClient.cs` (a `DelegatingChatClient` in `BuildChatPipeline`) and `src/Modules/AI/AI/Metering/MeteringEmbeddingGenerator.cs`; add `IntentContext` (`src/Modules/DigitalBrain/Kernel/Contracts/`) stamped by `AgentEndpoints`; persist per-intent usage durably. Add nothing to `ConversationCoordinator` (deleted in P1).
- **Test:** `src/Modules/AI/Tests/Unit/Metering/MeteringFacts.cs` (input/cached/reasoning/output recorded); `AgentWorkflowFacts` asserts usage is non-null for a scripted turn.
- **Acceptance:** every `/agent` turn persists token usage keyed by intent id; GenAI spans carry token counts.
- **Dependencies:** P0.4. **Gate:** none.

**Task P0.6 — Sensitive capture policy (D14).**
- **Files:** `src/Applications/IntoChat/ServiceDefaults/ServiceDefaultsExtensions.cs` and `src/Modules/AI/AI/Clients/AIClients.cs`: content capture stays on for the local owner's dev runs, off in hosted deployments and for any other principal; from Phase 1, Personal/Credential values redacted even in dev.
- **Test:** `src/Applications/IntoChat/Tests/E2E/Security/ContentCaptureFacts.cs` (a seeded Personal value never appears in GenAI logs in dev).
- **Acceptance:** the `739c6c385` goal is kept without the leak.
- **Dependencies:** P0.4, WS-E (for classes) — in P0 use a minimal class tag.

**Task P0.7 — Rename `WorkspaceNeuron.Receipts` → `OperationLog`; component words out of product UI.**
- **Files:** `src/Modules/Google/Flutter/Flutter/Workspace/WorkspaceNeuron.cs` (the persisted `[Id(2)] Receipts` / `[Id(3)] SurfaceReceipts` members live in the nested `WorkspaceStorage` record here, lines 108–115); Dart workspace labels. **`src/Modules/Google/Flutter/Contracts/Workspace/WorkspaceState.cs` does not contain a `Receipts` member** and is therefore not part of this rename.
- **Test:** `src/Modules/Google/Flutter/Tests/Unit/Workspace/WorkspaceFacts.cs`.
- **Acceptance:** no persisted member named `Receipts` that means idempotency; no engine word in customer UI.
- **Dependencies:** none. **Gate:** none.

**Task P0.8 — Behavior tools leave the default turn; "not supported yet" fallback.**
- **Files:** `src/Applications/IntoChat/IntoChat/Agent/ConversationCoordinator.cs` (tool allowlist down to the Supabase pair + any P1 tools); `BehaviorAgentTools` offered only in developer mode (config flag); `RiskNotification`/one-line message from C01.
- **Test:** `src/Modules/AI/Tests/Unit/Agents/AgentToolFacts.cs` (default allowlist excludes `behavior_*`).
- **Acceptance:** default turn carries ≤ 8 tools; a UI request **with developer mode off** returns a one-line unsupported message naming Phase 1. Developer mode itself stays enabled by default for the owner until J2a and C11 ship (highlevel §11 Phase 0).
- **Dependencies:** P0.1. **Gate:** none.

**Phase 0 exit criteria (from spec §11):** trash register Phase-0 rows deleted/fixed with net lines, explicitly including: host `Downloads` exposure off by default (P0.3), the stale Reqnroll `.gitignore` rule (P0.1), and the duplicate `ProcessRunner` (P0.1); lean product + developer profiles; Journaling unwired; one propagation registration and Warning logs; idle shell < 20 spans/min CI check; agent client through `BuildChatPipeline` with GenAI spans and all token classes captured by the decorator; sensitive capture per D14; behavior tools developer-only; `WorkspaceNeuron.Receipts` renamed; docs/ADR/CI truthful; **narration test passed for J1 using `current-state.md` §4 plus one trace** (not a receipt). Developer mode stays **enabled by default until J2a (P1.5/P1.9) and C11 (P2.7) ship** — the developer-only gate is enforced only for *behavior* tools, and the owner's turn keeps working in the meantime. The "unmapped brain-events retry" was reproducible at baseline as an actively called Dart poll (`ui_client.dart`) against `GraphEndpoints.cs`, whose C# handler was already excluded by the `IntoChat.csproj` `Compile Remove` block; P0.1 removed both the orphan poll and handler. Do not describe it as historical-only or as a live server route. *Owner track:* ratify D1–D6 (assumed here), 10 problem interviews, 3–5 LOIs (G-5), commercial baseline.

---

### Phase 1 — Ask, see, keep, safely (8 weeks · J1, J2a, J2b · C01 C02 C03 C04 C05 C06 C07 C09)

**Epic E1.1 — Ask & see (J1) (WS-A, WS-E, WS-I, WS-C, WS-G-shadow).**

**Task P1.1 — Semantic type catalog v0 (WS-E).**
- **Files:** create `src/Modules/DigitalBrain/Kernel/Contracts/Types/SemanticType.cs`, `TypeCatalog.cs`, `FieldKind.cs`, `SchemaExtensions.cs` (JSON Schema with `x-intochat-*`), `Types/TypeCatalog.v0.json`; C# value types `PlainText`, `LongText`, `Number`, `Date`, `DateTime`, `Boolean`, `Choice`, `Email`, `Url`, `SecretRef`, `Reference`. **P1.1 is the single owner of the `SecretRef` type** (`src/Modules/DigitalBrain/Kernel/Contracts/Types/SecretRef.cs`); P1.6 consumes it and must not create a second copy. v0 catalog is closed for primitives, open for composition (records built from catalog types).
- **Interfaces:** `SemanticType { Id, Primitive, Sensitivity, LlmExposure, InputWidget, DisplayWidget, Redactor, Indexable }`; `TypeCatalog.Get(FieldKind)`; `TypeCatalog.Validate(value, kind)` throws `TypeValidationException` naming the rejected value and allowed values.
- **Test-first:** `src/Modules/DigitalBrain/Kernel/Tests/Unit/Types/TypeCatalogFacts.cs`: flat subset maps 1:1 to MCP form elicitation; `SecretRef` is never collected by a form; a `Date` rejects `"date"` with an error naming `Date`.
- **Acceptance:** one catalog; tool schemas generated from it; errors name allowed values.
- **Dependencies:** none. **Gate:** none.

**Task P1.2 — One assistant runtime: `AgentNeuron` only (WS-A).**
- **Files:** `src/Applications/IntoChat/IntoChat/Agent/AgentEndpoints.cs` drives `AgentNeuron`; delete `ConversationCoordinator.cs`, `src/Modules/AI/Contracts/Conversations/IConversation.cs`, `src/Modules/AI/AI/Conversations/ConversationNeuron.cs`, and `/author` in `BehaviorEndpoints.cs` + `BehaviorAuthoringService.cs`; bound `AgentNeuron` history (last N turns + summary) in `AgentNeuron.cs`/`AgentStorage`.
- **Interfaces:** `IAgent` already exposes `Ask`/`Execute`/`GetLastUsage`; keep the SSE event shape used by the Flutter client.
- **Test-first:** `src/Modules/AI/Tests/Unit/Agents/AgentHistoryFacts.cs` (bounded after 4,096 messages); `src/Applications/IntoChat/Tests/E2E/Agent/AgentWorkflowFacts.cs` (replay, cancel, concurrent rejection, failure keeps the turn).
- **Acceptance:** `AgentNeuron` is the only conversation runtime; a failed run keeps the turn in history.
- **Dependencies:** P1.1 (tool schemas), P0.5 (usage). **Gate:** none.

**Task P1.3 — Live tables the assistant can read and refine (WS-I, C09, D6).**
- **Files:** rename `SupabaseWorkspaceTools.cs` → `WorkspaceTableTools.cs`; add tools `table_read` (schema, counts, aggregates; row values only for `Public` columns or under a per-connection grant) and `table_refine` (same window, not a new one). Harden `SupabaseTableNeuron.CreateFromQueryOnce`/`UpdateView`; keep `SupabaseQueryGuard` + `READ ONLY` + 15 s/3 s timeouts + `LIMIT 0` describe + 1–32 columns.
- **Consolidate the four table mechanisms into one** (C09): keep `ISupabaseTable` generalized into one live-table contract with pluggable query sources; remove the other three — `ITable` (Flutter UI-kit rows stored in grain state), `IClickHouseTable` (its `TableRendered` has zero consumers and it is unreferenced by the app), and `QueryWindowOperation`/`QueryWindowOperationNeuron` (a plain grain in the app, replaced by the read/refine tools). Because `GET /ui/tables/{id}` is still served by the UI kit (`src/Modules/Google/Flutter/Flutter/Shared/UiKitEndpoints.cs`), removing `ITable` is a **required ADR** in `docs/product/decisions/` before deletion; if the ADR rejects removal, keep `ITable` as a declared adapter and say so. The ADR names all three removed mechanisms and the one kept contract.
- **Test-first:** `src/Modules/Supabase/Tests/Unit/Table/SupabaseTableFacts.cs` (refine mutates the same window; columns typed via catalog; error names the 1–32 limit); `src/Applications/IntoChat/Tests/E2E/Agent/AgentTableJourneyFacts.cs` (J1 refine + "how many" from a count, no rows); a test asserts only one query compiler/policy/guard/type-map cluster remains (or that any survivor is ADR-fenced).
- **Acceptance:** J1 sequence in spec §10 passes on the scripted model; every row read counted on the receipt; one live-table contract, with the three removed mechanisms gone or explicitly ADR-fenced.
- **Dependencies:** P1.1, P1.2. **Gate:** none.

**Task P1.4 — Durable receipts with shadow Compute (WS-C, WS-G).**
- **Files:** create `src/Modules/Receipts/` (contract + neuron, per-intent row outside the workspace blob); create the **single** price book in the Compute module: `src/Modules/Compute/Compute/PriceBook/PriceBook.cs` + `src/Modules/Compute/Compute/PriceBook/pricebook.v0.json` (per concrete model, input/cached/reasoning/output; local models priced 0 and labelled "local"). **Do not create a price book under `src/Modules/AI/`** — P2.2 extends this same Compute module with the ledger and durable metering. `IntentContext` propagation through tools/traces; receipt UI card in `app/shell/lib/workspace/`.
- **Interfaces:** `IReceipts.WriteAsync(intentId, ReceiptDraft)`; `Receipt` carries north-star fields (first-try, retries, repairs, kept, approved vs actual Compute, discovered vs hard-wired).
- **Test-first:** `src/Modules/Receipts/Tests/Unit/ReceiptFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Receipts/ReceiptJourneyFacts.cs` (every intent has a durable receipt with a shadow price; receipt survives trace eviction); a guard test asserts exactly one `pricebook*.json` exists in the solution and it lives under `src/Modules/Compute/`.
- **Acceptance:** 100 % of intents have a receipt; shadow Compute shown, not charged; product events `intent.*`/`app.*` written content-free; exactly one price book and path.
- **Dependencies:** P0.5, P1.2. **Gate:** none (shadow only).

**Epic E1.2 — Draw & keep safely (J2a, J2b) (WS-B, WS-D, WS-E, WS-F, WS-C).**

**Task P1.5 — Declarative Form neuron + `show_form`/`show_view` (WS-B).**
- **Files:** create `src/Modules/Google/Flutter/Contracts/Form/IForm.cs` and `src/Modules/Google/Flutter/Flutter/Form/FormNeuron.cs` (one neuron per window: typed field list, draft values, atomic submit in a single state); `src/Applications/IntoChat/IntoChat/Agent/WorkspaceFormTools.cs` (`show_form`, `show_view`); extend `AppSurfaceComposer` only to open/lay out the window; renderer `app/ui/lib/src/composition/neuron_view.dart` + model `ui_part.dart` render the Form from one read. No per-field UI-kit neurons for forms (T7).
- **Test-first:** `src/Modules/Google/Flutter/Tests/Unit/Form/FormFacts.cs` (atomic multi-field submit; ≤ 1 activation/blob per form); `src/Modules/Google/Flutter/Tests/E2E/Form/FormHttpFacts.cs`; `app/ui/test/composition/form_renderer_test.dart` (date picker + masked input render).
- **Acceptance:** J2a form appears in ≤ 10 s with 0 compile steps; unsupported kind uses a declared fallback and one-line explanation.
- **Dependencies:** P1.1. **Gate:** none.

**Task P1.6 — My Data vault v0 with envelope encryption (WS-D).**
- **Files:** create `src/Modules/MyData/` (Contracts + `MyDataNeuron` per owner, typed fields `me.birthDate`, `me.email.work`, access audit, export/erasure); **consume the `SecretRef` type created by P1.1** (`src/Modules/DigitalBrain/Kernel/Contracts/Types/SecretRef.cs`) — do not create a second `SecretRef`; T4 envelope encryption (per-owner data key, per-credential key, wrapped by Key Vault or DPAPI; crypto-shredding); the My Data app.
- **Test-first:** `src/Modules/MyData/Tests/Unit/VaultFacts.cs` (crypto-shred removes access; value never returned in read results); `src/Applications/IntoChat/Tests/E2E/Security/CanarySecretFacts.cs` (the Phase-1 feature gate: a seeded secret appears 0 times in plaintext in grain state, signals, HTTP reads, traces, logs, vectors or tool results).
- **Acceptance:** J2b stores `me.birthDate`; no app can read it yet (grants arrive P2).
- **Dependencies:** P1.1. **Gate:** none.

**Task P1.7 — Workspace scoping; remove unscoped `/ui` value routes (WS-F).**
- **Files:** `src/Applications/IntoChat/IntoChat/Workspace/WorkspaceScope.cs`; `src/Modules/Google/Flutter/Flutter/Shared/UiKitEndpoints.cs` + `UiHttp.cs` (scope routes under `/workspaces/{scope}/…`); delete `GET /ui/textfields/{name}`; `FlutterModule.cs`; every neuron holding owner/workspace data keyed under the workspace scope. Integration connections stay global until the token vault (accepted exception).
- **Test-first:** `src/Applications/IntoChat/Tests/E2E/Security/CrossWorkspaceFacts.cs` (a second workspace cannot read the first's value); `src/Modules/Google/Flutter/Tests/Unit/Composition/TypeParityFacts.cs` is green (C#↔Flutter kind parity).
- **Acceptance:** unscoped value routes gone; cross-workspace read denied.
- **Dependencies:** P1.1. **Gate:** none.

**Task P1.8 — Golden-prompt suite for J1/J2a on a live model (WS-A).**
- **Files:** `src/Applications/IntoChat/Tests/E2E/Agent/GoldenJourneyFacts.cs` (env-gated `DIGITALBRAIN_E2E_LIVE_MODEL`), fixed prompts from spec §10.
- **Test:** the command in §1.2; assert ≥ 18/20 pass each, nightly.
- **Acceptance:** O2 target met on live models.
- **Dependencies:** P1.3, P1.5. **Gate:** G-7 (provider keys).

**Task P1.9 — Renderer registry, window states, one window reference (WS-B, C02).**
- **Files:** create `src/Modules/Google/Flutter/app/ui/lib/src/composition/renderer_registry.dart` (one registry entry per catalog kind, with an explicitly declared fallback for kinds that have no dedicated renderer); extend `app/ui/lib/src/models/ui_part.dart` and `neuron_view.dart` to render loading, empty, permission-denied, expired-connection and failed states; add the first-run workspace state (assistant + starter prompts matching connected sources) in `src/Modules/Google/Flutter/Flutter/Workspace/WorkspaceNeuron.cs`; implement one window reference ("this window shows neuron X") as a typed field, not a string route.
- **Scope per highlevel C02 only:** renderer registry + declared fallbacks (9 of 29 kinds today), window states, and one window reference. Do not claim a general SSE rework beyond C02's "SSE push instead of polling"; the `/agent` token streaming already owned by C01 is unchanged.
- **Test-first:** `app/ui/test/composition/renderer_registry_test.dart` (every catalog kind resolves to a renderer or a declared fallback; unknown kind renders the fallback, never a blank); an E2E case opens a new workspace (first-run), an empty window, a denied read and a failed read and asserts the declared state renders; a test asserts the window reference resolves to a neuron id.
- **Acceptance:** every catalog kind is registered or has a declared fallback; no window renders blank on first-run/empty/error; one window reference exists.
- **Dependencies:** P1.1, P1.5. **Gate:** none.

**Phase 1 exit criteria (spec §11):** `AgentNeuron` only runtime; J2a p50 ≤ 10 s with 0 compile steps; parity + canary-secret tests green; every intent has a durable receipt with shadow price; `AgentNeuron` history bounded; live-model J1/J2a ≥ 18/20; ≥ 2 design partners ran J1 on their own data under signed pilot terms and a DPA. *Owner track:* data-protection readiness (G-4). **Gate:** G-4, G-7.

---

### Phase 2 — Make it yours (12 weeks · J3, J5 · C04 C06 C07 C08 C09 C10 C11 C12 C14 C15 C16 C17)

**Epic E2.1 — Durable foundations (enabler) (WS-F, WS-G, WS-I, WS-N, WS-Q).**

**Task P2.1 — Accounts, principals, caller context, one call filter (WS-F, T2).**
- **Files:** create `src/Modules/Identity/` (accounts, owner/member roles, login, invitations, sharing); `src/Modules/DigitalBrain/Kernel/Kernel/Enforcement/` + `Contracts/Enforcement/ICallFilter.cs`. Caller context stamped only at trusted edges (authenticated HTTP, app proxy, scheduler) and **re-stamped** at app boundaries; the proxy narrows grants to the app's own. Any gateway holder is trusted platform code.
- **Test-first:** `src/Modules/DigitalBrain/Kernel/Tests/Unit/EnforcementFacts.cs` (a principal set by a non-edge caller is rejected); `src/Applications/IntoChat/Tests/E2E/Security/WorkerGatewayDeniedFacts.cs` (from P2 a behavior worker cannot open an Orleans gateway connection in the product profile).
- **Acceptance:** single-owner login on by default; accounts with invited members and owner/member roles; one enforcement component only.
- **Dependencies:** P1.7. **Gate:** none.

**Task P2.2 — Durable metering, ledger, storage sampler (WS-G, T5/T3).**
- **Files:** extend the **same** `src/Modules/Compute/` module and its single price book created in P1.4 (`src/Modules/Compute/Compute/PriceBook/PriceBook.cs` + `pricebook.v0.json`) — add idempotent meter events keyed `(intentId, meterId, step)` from the P0.5 chat and embedding decorators, the call filter (first-party functions), and a daily storage sampler (`storage.gb_month`, daily-peak average); a cost ledger separate from the price book; single-writer `WalletNeuron` over an append-only PostgreSQL ledger table with a unique constraint on the idempotency key (T3, no Orleans transactions). Metering reconciles within 1 % of provider usage. **No second price book is created here or anywhere else.**
- **Test-first:** `src/Modules/Compute/Tests/Unit/MeterFacts.cs` (same key twice = one event); `LedgerFacts.cs` (concurrent writers never double-charge); shadow storage line on a receipt/statement.
- **Acceptance:** metering reconciles; a hosted first-party file store samples storage in shadow.
- **Dependencies:** P2.1, P0.5. **Gate:** none (shadow). Migrate to real prices only after G-1/G-2/G-3.

**Task P2.3 — Token vault, MCP bridge, Connect flow (WS-I).**
- **Files:** create `src/Modules/Connections/`; move Salesforce (`SalesforceState` → vault) and Gmail OAuth tokens behind `SecretRef`; register `McpAgentTools.AddMcpAgentTools` in production with allowlist entries; self-serve Connect (pick source; paste connection string or OAuth as `SecretRef`; read-only probe; status connected/expired/failing; disconnect); web research (`websearch`, `browse_web`, `lookup_company`) as metered connections (`search.request`).
- **Test-first:** `src/Modules/Connections/Tests/Unit/ConnectFlowFacts.cs`; `src/Modules/Supabase/Tests/Unit/Query/SupabaseQueryFacts.cs` extended (probe is read-only); `src/Applications/IntoChat/Tests/E2E/Security/CanarySecretFacts.cs` extended to connection tokens.
- **Acceptance:** a connector is in the product profile only if chat can reach it; Salesforce hosted MCP becomes discoverable operations.
- **Dependencies:** P1.6, P2.1. **Gate:** counsel for OAuth/DPA already covered by G-4.

**Task P2.4 — Persistence off local disk; migration policy (WS-N).**
- **Files:** replace `DurableDocumentStore<T>` local JSON (defined in `src/Modules/Coding/Coding/Drafts/CodeDraftStore.cs:99`) with cluster storage at every call site: `src/Modules/Coding/Coding/Drafts/CodeDraftStore.cs`, `src/Modules/DigitalBrain/Behaviors/Behavior/Programs/BehaviorProgramStore.cs`, `src/Applications/IntoChat/IntoChat/Behavior/BehaviorCatalogStore.cs`, and `src/Modules/DigitalBrain/Behaviors/Behavior/Execution/BehaviorLogStore.cs`; give Qdrant a data volume via `src/Modules/Memory/Aspire.Hosting/Configuration/QdrantHostingOptions.cs`; write `docs/product/decisions/0000-stored-state-migration.md` (T1); `Neuron<TState>` failed write rolls back or deactivates.
- **Test-first:** `src/Modules/DigitalBrain/Kernel/Tests/Unit/PersistenceFacts.cs` (failed write rollback); restore-from-backup test; `src/Modules/Coding/Tests/Unit/Drafts/CodeDraftStoreFacts.cs` updated to the cluster store.
- **Acceptance:** one persistence model in cluster storage; no local-disk JSON in the product profile.
- **Dependencies:** P2.1. **Gate:** none.

**Task P2.5 — Hosted deployment + operations (WS-Q, C17).**
- **Files:** `src/Applications/IntoChat/AppHost/` product profile; `src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml` and `src/Applications/IntoChat/IntoChat/Dockerfile` corrected; `ops/otel/collector.yaml` (tail sampling) and persistent telemetry backend; Key Vault managed identity; nightly backups of grain storage + ledger + Qdrant; `docs/operations/runbook.md`; in-app "Report a problem" attaching the intent id; disable the Windows developer executor in hosted deployments.
- **Test:** `src/Applications/IntoChat/Tests/E2E/Hosting/RestoreDrillFacts.cs` (harness-gated) + `aspire run --detach` and `aspire wait IntoChat --status healthy` against the product profile.
- **Acceptance:** one hosted deployment passes a restore drill and runs J1/J2 golden prompts.
- **Dependencies:** P2.1, P2.4 (serial — P2.4 first). **Gate:** G-3 (hosting/data), G-5.

**Epic E2.2 — Make it yours (J5) (WS-J, WS-K, WS-P).**

**Task P2.6 — `app.json` v1 (declarative) and Save as app (WS-J, C10, D5/T7).**
- **Files:** create `src/Modules/Apps/` (`app.json` model: identity, SemVer, publisher, kind `declarative`/`remote`/`process`, description for people + model, typed operations generated from the neuron interface, UI entry, permissions/data classes, meters, example prompts + scenarios); a platform proxy neuron so apps never add grain types; per-workspace install/uninstall without restart; immutable versions with rollback; generated manifests for every first-party module; `Save as app` from the workspace; the Applications launcher driven by manifests.
- **Test-first:** `src/Modules/Apps/Tests/Unit/ManifestFacts.cs` (generated manifest cannot drift from the interface); `src/Applications/IntoChat/Tests/E2E/Apps/SaveAsAppFacts.cs` (app survives restart; uninstall states what data is kept).
- **Acceptance:** J5 first half passes; every product module carries a manifest.
- **Dependencies:** P1.1, P1.6, P2.1. **Gate:** none.

**Task P2.7 — Customer automations on durable jobs (WS-K, C11, T8).**
- **Files:** create `src/Modules/Automations/` (declarative trigger → typed action, validated before activation, budget per automation); each run is a Job record with idempotency key `(automationId, scheduledTime)` on `ReminderNeuron`; delete Timer's in-memory schedule; paid/side-effect steps never auto-retried; run history in receipts; the developer tier reduced to 3–4 coarse tools with server-managed revisions, contract-level checks against a test brain, tautological tests rejected, first exception in status, quiet logs, delete/uninstall, console behind developer mode.
- **Test-first:** `src/Modules/Automations/Tests/Unit/JobFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Automations/AutomationRestartFacts.cs` (survives host restart; paid step never twice).
- **Acceptance:** J3's daily automation passes; no automatic retry of side effects.
- **Dependencies:** P2.6, P2.2. **Gate:** none.

**Task P2.8 — Inbox v1 (WS-P, C16).**
- **Files:** create `src/Modules/Inbox/` (structured items: id, kind, source intent/app/window, actions, read/resolved; repeats grouped; deep link to window/receipt; SSE push; email digest for approvals waiting > 1 h); producers: automations, Compute alerts, approvals, connections, kill switch. Delete the volatile `IInbox` string list and `InboxBanner` polling.
- **Test-first:** `src/Modules/Inbox/Tests/Unit/InboxFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Inbox/StructuredInboxFacts.cs` (a failed automation creates an Inbox item with the first error).
- **Acceptance:** work that happened while away reaches the user.
- **Dependencies:** P2.6, P2.7. **Gate:** none.

**Epic E2.3 — Find & install (J3) (WS-H, WS-I, WS-R).**

**Task P2.9 — Discovery and system memory (WS-H, C08, D12).**
- **Files:** create `src/Modules/Discovery/`; a catalog built from manifests is the truth (vector + keyword search returns ids only); separate durable Qdrant collections on the Memory module for system capabilities (apps/operations/agents/types), workspace instances ("memory of neurons"), user memories (Public only, provenance; may reference a My Data field id but never its value); namespaced aliases (`intochat.files`, `com.acme.leadgen`); embeddings computed off the grain call; background idempotent rebuilds with keyword fallback and a visible degraded state; delete the orphaned `digitalbrain.capabilities` guard in `MemoryNeuron.cs`; a `find_capability` tool; vector search enters the turn only when the candidate set would exceed 8; workspace-instance search ships regardless; an unmet-intent board.
- **Test-first:** `src/Applications/IntoChat/Tests/E2E/Discovery/DiscoveryGateFacts.cs` — golden set ≥ 60 prompts (direct/indirect/negative) against ≥ 50 operations including ≥ 20 distractor manifests; top-5 recall ≥ 0.9, negative precision ≥ 0.9, p95 < 300 ms; discovery still works with embeddings down.
- **Acceptance:** discovery gate passes; never blocks a turn.
- **Dependencies:** P2.6, P2.2. **Gate:** A4 (assumption test; demand-independent).

**Task P2.10 — Grants, consent sheet, and LeadGenerator (WS-I, WS-R, J3).**
- **Files:** Identity grants keyed `(app, semanticType, mode)` once/this-chat/always with one-click revoke; a denied value looks empty; consent sheet showing examples, data types, permissions and a shadow Compute estimate; `src/Apps/leadgenerator/app.json` composing web search + company lookup (meter `search.request`), a Leads live table in the app's own data (company-level fields only), and the P2.7 daily automation; never LinkedIn-derived, no outreach in v1.
- **Test-first:** `src/Modules/Identity/Tests/Unit/GrantFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Apps/LeadGeneratorFacts.cs` (consent sheet shown; Leads window opens company fields; daily automation lands in Inbox).
- **Acceptance:** J3 passes on a scripted model with shadow, not charged.
- **Dependencies:** P2.7, P2.8, P2.9. **Gate:** **G-6 (LeadGenerator source review)**, D15.

**Phase 2 exit criteria (spec §11):** apps install/uninstall per workspace without restart; every first-party module carries a generated manifest; accounts/login exist, single-owner vault migrated, cross-workspace access denied; a product-profile behavior worker can't open a gateway and a non-edge principal is rejected; discovery gate passes and works with embeddings down; metering reconciles within 1 % and a storage meter reports in shadow; a scheduled automation survives restart and never runs a paid step twice; one hosted deployment passes a restore drill; a design partner goes from invitation to first accepted J1 in ≤ 15 min; ≥ 3 partners keep ≥ 1 saved app/automation that re-runs weekly. *Owner track:* counsel by week 2 (G-3), D16 (G-1), Stripe account (G-2), LeadGenerator source review (G-6).

---

### Phase 3 — Pay as you go (8 weeks · J4 · C03 C06 C07 C10 C11 C12)

**Epic E3.1 — Spend limits & billing (WS-G, WS-F, WS-Q).**

**Task P3.1 — Allowances at three levels, limits, alerts, ledger, invoices.**
- **Files:** extend the P2.1 call filter with allowances (standing budget, install consent with monthly limit, per-operation allowance) scoped once/this-chat/always; pending approvals survive restart; limits per account/workspace/app/run with alerts at 75/90/100 % and a distinct hard-stop; monthly statements and invoices via Stripe Checkout/Tax (T10); the failure policy classes (platform fault, provider outage, model retry inside success, cancellation/revoked grant/limit, third-party fault) applied to the ledger; recurring meters accrue without an intent and appear on the monthly statement, with a 30-day readable/exportable grace period at a limit.
- **Test-first:** `src/Modules/Compute/Tests/Unit/AllowanceFacts.cs`; `src/Applications/IntoChat/Tests/E2E/Compute/ChaosChargeFacts.cs` (chaos test finds 0 double charges; actual ≤ limit in 100 % of intents); a prompt-injection suite asserts 0 paid calls without an allowance.
- **Acceptance:** J4 passes; each design partner pays ≥ 2 monthly invoices at published prices; ≥ 2 of 3 renew; contribution margin meets the D8 target.
- **Dependencies:** P2.2, P2.5. **Gate:** **G-1, G-2, G-3, G-9, G-10 (A5).**

**Task P3.2 — First paid capability (WS-R).**
- **Files:** `src/Apps/image-editor/app.json` (BackgroundRemover as a paid operation) and/or paid company enrichment in `leadgenerator/app.json`; the paid operation joins the P3.1 allowance path.
- **Test-first:** `src/Applications/IntoChat/Tests/E2E/Apps/PaidCapabilityFacts.cs` ("Remove the background from these 3 photos": plan card estimates 12 Compute, max 20; allow once; receipt shows 9 charged, the failed image not charged).
- **Acceptance:** the first capability the design partners use weekly is chargeable within an approved cap.
- **Dependencies:** P3.1, P2.10. **Gate:** G-3.

**Phase 3 exit criteria (spec §11):** chaos test finds 0 double charges; actual ≤ limit 100 %; 0 paid calls without an allowance under prompt injection; each partner paid ≥ 2 invoices and ≥ 2 of 3 renew; contribution margin meets D8; ≥ 8 willingness-to-pay interviews; cloud-stored Files appear as a storage line. *Owner track:* pricing page, terms, counsel sign-off, VAT adviser (G-3, G-9).

---

### Phase 4 — Invited developers (10 weeks · J6 · C07 C08 C10 C12 C13 C15)

**Epic E4.1 — Broker gateway & remote apps (WS-L).**

**Task P4.1 — Out-of-process broker gateway with egress rules.**
- **Files:** create `src/Modules/Broker/`; one enforcement component continues (in-silo call filter P2 → allowances P3 → HTTP gateway for `remote` apps P4); manifest data classes checked on every call; no other approval path is built; the Windows developer executor is absent in hosted deployments; host loads only contracts assemblies after signature and banned-API checks.
- **Test-first:** `src/Modules/Broker/Tests/Unit/GatewayFacts.cs` (100 % of third-party calls pass the broker; declared-vs-observed permissions/meters diff = 0).
- **Acceptance:** broker handles 100 % of third-party calls.
- **Dependencies:** P2.1, P2.6. **Gate:** G-3 (P2B/DSA scope).

**Epic E4.2 — Publisher program, certification, earnings (WS-M).**

**Task P4.2 — Catalog, certification, publish pipeline (WS-M, C13, D11).**
- **Files:** create `src/Modules/Marketplace/`; a catalog service separate from the package feed; listings generated from manifests; publish rings first-party → invited `remote`; trust pipeline: namespace proof, signatures in require mode, scans, sandbox run of scenarios on fakes, declared-vs-used diff, golden-prompt precision, human review only for risky classes, Beta label; governance: kill switch < 15 min with statement of reasons, listing policy/takedowns, reviews only from workspaces that ran the app, "Report this app", developer + acceptable-use policies, neutral ranking by accepted intents with published parameters (counsel confirms EU P2B/DSA).
- **Test-first:** `src/Modules/Marketplace/Tests/Unit/CertificationFacts.cs` (certification evidence attached to every listing); `src/Applications/IntoChat/Tests/E2E/Marketplace/PublishRemoteFacts.cs` (J6: certify on fakes, list as Beta, propose, install-consent, run through broker, receipt).
- **Acceptance:** J6 passes; ≥ 5 invited apps used weekly by non-author workspaces; kill-switch drill < 15 min.
- **Dependencies:** P4.1, P3.1. **Gate:** G-3, G-2 (payout regions), A7.

**Task P4.3 — Earnings ledger and payouts (merchant of record) (WS-M).**
- **Files:** earnings recorded in fiat, never Compute; holding period ≥ card dispute window (120 days); verified identity, supported country, tax forms; threshold + claw-back; refunds as new entries; IntoChat as merchant of record (tax on full price, disputes/refunds/chargebacks, supplier agreement with self-billing, sanctions screening); take rate per D9.
- **Test-first:** `src/Modules/Marketplace/Tests/Unit/EarningsFacts.cs` (payout end to end; refunds as new entries).
- **Acceptance:** payouts tested end to end; creators who can't onboard publish free apps only.
- **Dependencies:** P4.2. **Gate:** G-1, G-2, G-3.

**Phase 4 entry/exit (spec §11):** entry ≥ 15 active paying workspaces, take rate decided, counsel on third-party charges + P2B/DSA. Exit: broker handles 100 %; declared-vs-observed diff 0; certification evidence on every listing; payouts tested; kill switch < 15 min; ≥ 5 invited apps weekly by non-authors.

---

### Phase 5a — Creators publish (6 weeks · C13 C10 C08)

**Task P5a.1 — Non-programmer publishing of declarative apps.**
- **Files:** extend `src/Modules/Marketplace/` with creator publishing; publish a saved `declarative` app; gated by identity, the call filter, certification of scenarios on deterministic fakes, the kill switch, and re-consent when an app's permissions or prices grow. No sandbox is needed because declarative apps carry no code. Revisit self-serve sign-up, prepaid Compute (D7) and consumer terms (D16) here.
- **Test-first:** `src/Applications/IntoChat/Tests/E2E/Marketplace/PublishDeclarativeFacts.cs` (a saved app publishes; a permission growth triggers re-consent).
- **Acceptance:** a regular user publishes an app; no code runs in the silo.
- **Dependencies:** P4.2, P3.1. **Gate:** G-1 (consumer/B2B), G-3 (consumer terms).

### Phase 5b — Sandboxed code apps (later · C12 C13)

**Task P5b.1 — Signed `process` apps in a per-app sandbox.**
- **Files:** extend `src/Modules/Apps/` + `src/Modules/Broker/` with `process` kind: signed `.nupkg` (`IntoChatApp` type, embedded `intochat/app.json`), per-app container with CPU/memory/process limits, no Orleans gateway, network denied by default; banned-API analyzers; Wasm later.
- **Test-first:** `src/Modules/Broker/Tests/Unit/SandboxFacts.cs` (no gateway, no network by default); a certification run of a `process` app on fakes.
- **Acceptance:** third-party code runs safely; every call through the broker.
- **Dependencies:** P4.1, P4.2. **Gate:** **G-8 penetration test.**

---

## 5. Final validation — Computer Use real-user journey validation

After Phase 3 (and again incrementally), an agent drives the real product as a **real user** ("Computer Use"): the Flutter web/desktop client on a live deployment, no scripted model, using the existing E2E browser harness (`AsBrainBrowser`, Playwright `1.62.0`, `readySelector: "flt-semantics"`). Each journey is a scripted end-to-end run whose evidence is a receipt, a trace export and screenshots.

| Journey | Real-user steps | Pass evidence |
|---|---|---|
| **J1** | Connect a Supabase DB; ask "Show me all customers"; refine "only London"; ask "how many?" | Same window refined, not duplicated; answer "312 in London" from a count without reading rows (D6); receipt with shadow Compute; ≤ 25 spans; assistant saw only schema/count/Public values |
| **J2a** | Ask for a card with name, surname, date of birth; fill and Save; add a password field | Form ≤ 10 s, 0 compile steps; stored as `PlainText/PlainText/Date` (Personal); password masked "•••• set"; assistant sees types/handle only |
| **J2b** | Say "remember my date of birth", enter it in the secure field | Stored as `me.birthDate` in My Data; listed; no app reads it until granted |
| **J3** | Ask "Find new dental clinics in Berlin"; approve concern sheet; say "do this every morning" | Consent sheet (examples/types/permissions/shadow cost); Leads window with company fields only; daily automation lands in Inbox; no LinkedIn data; no outreach |
| **J4** | Ask "Remove the background from these 3 product photos"; allow once | Plan card estimate/max; new versions, originals kept; receipt 9 Compute charged, failed image not charged |
| **J5** | Say "Save this as an app called Customer intake"; let it need my date of birth | App appears versioned and survives restart; grant prompt once/always; grant in My Data with Revoke |
| **J6** | An invited developer submits a `remote` app | Certified on fakes, listed Beta, proposed to a matching request, runs through the broker, earnings accrue net of take rate |

**Validation harness.** New `src/Applications/IntoChat/Tests/E2E/Journeys/` classes (one per journey) plus a Computer-Use driver script under `docs/superpowers/plans/` execution ledger. The live-model suite is the nightly gate (`GoldenJourneyFacts`); the Computer-Use run is the human-observable acceptance. Both must pass. Where a step depends on a gate (§0.3), mark the step `blocked` and record the gate rather than faking the result.

---

## 6. Self-review against capabilities, journeys and phases

### 6.1 Capability coverage C01–C17

| Cap | Phase(s) | Epics/tasks | Status after plan |
|---|---|---|---|
| C01 Assistant | P0–1 | P0.8, P1.2, P1.8 | `AgentNeuron` only; ≤ 8 tools; streaming; receipts |
| C02 Workspace/instant UI | P0–1 | P0.1, P0.7, P1.5, P1.7, P1.9, P2.6 | declarative Form neuron; renderer registry (every kind or declared fallback); first-run/empty/permission-denied/expired-connection/failed window states; one window reference; C02-scoped SSE push; single source of truth |
| C03 Receipts/traces | P0–1,3 | P0.4, P0.5, P0.6, P1.4, P3.1 | durable receipts; product events; one trace; GenAI spans; receipt UI |
| C04 My Data | P1–2 | P1.6, P2.3 | vault; `SecretRef`; token vault; envelope encryption; export/erasure; grants UI |
| C05 Semantic types | P1 | P1.1, P1.7 | catalog v0; JSON Schema `x-intochat-*`; C# value types; one table map + parity test |
| C06 Identity/consent | P1–3 | P1.7, P2.1, P2.10, P3.1 | roles; caller context; grants; 3 approval levels; audit log |
| C07 Compute | P0–4 | P0.5, P1.4, P2.2, P3.1, P4.3 | usage→shadow→durable metering→billing→earnings |
| C08 Discovery | P2 | P2.9 | manifest catalog on Qdrant; gate; `find_capability`; degraded state |
| C09 Connections | P1–2 | P1.3, P2.3, P2.10 | one live-table contract (four mechanisms consolidated to one; the three removed named in P1.3/ADR); connect flow; MCP bridge; web research |
| C10 Apps | P2,5a | P2.6, P5a.1 | `app.json`; three kinds; install/version/rollback; launcher |
| C11 Automations | P2–3 | P2.7, P3.2 | declarative trigger→action; durable jobs; developer tier |
| C12 Isolation | P2–5 | P2.1, P4.1, P5b.1 | call filter→allowances→broker→sandbox |
| C13 Marketplace | P4–5 | P4.2, P4.3, P5a.1, P5b.1 | listings; trust; payouts; rings |
| C14 Runtime/durability | P0–2 | P0.3, P1.2, P2.4 | tiers; Journaling unwired; bounded state; one persistence model; migration policy |
| C15 Dev kit/decisions | P0–2 | P0.2, P2.6, P4.2 | CONTEXT rewrite; ADRs; epics; manifest generator; templates; eval tool |
| C16 Inbox | P2 | P2.8 | structured items; producers; SSE; digest |
| C17 Operations | P2 | P2.5, P3.1, P4.3 | provisioning; upgrades; backups; SLO; OTel; runbook; deletion |

**Coverage:** C01–C17 all have owners. No capability is left unassigned.

### 6.2 Journey coverage J1–J6

| Journey | Phase | Primary tasks | Validation |
|---|---|---|---|
| J1 | 1 | P1.3, P1.4, P0.4 | Golden + Computer Use |
| J2a/J2b | 1 | P1.5, P1.6, P1.7 | Golden + Computer Use |
| J3 | 2 | P2.7, P2.8, P2.9, P2.10 | Computer Use; **G-6** |
| J4 | 3 | P3.1, P3.2 | Computer Use; **G-1/G-2/G-3/G-9** |
| J5 | 2 | P2.6, P2.10, P1.6 | Computer Use; My Data vault/grant from P1.6 |
| J6 | 4 | P4.1, P4.2, P4.3 | Computer Use; **G-3/G-2** |

### 6.3 Phase coverage P0–P5b

| Phase | Epics | Exit criteria mirrored from spec §11 | Code-only? |
|---|---|---|---|
| P0 | Trash & truth; Lean profile | §4 P0 exit | Mostly; narration test needs the owner; A1/A9 need G-5 |
| P1 | Ask & see; Draw & keep safely | §4 P1 exit | Canary/parity/receipt/golden code; **G-4/G-7** not code |
| P2 | Durable foundations; Make it yours; Find & install | §4 P2 exit | Code + **G-1/G-2/G-3/G-6** and demand tests |
| P3 | Spend limits & billing; First paid capability | §4 P3 exit | Code + **G-1/G-2/G-3/G-9**; willingness-to-pay is human |
| P4 | Broker; Publisher program; Earnings | §4 P4 entry/exit | Code + **G-2/G-3** + 15 paying workspaces (human) |
| P5a | Creators publish | §4 P5a | Code + **G-1/G-3** consumer terms |
| P5b | Sandboxed code apps | §4 P5b | Code + **G-8** penetration test |

---

## 7. Gates that cannot be satisfied by code alone (recap)

1. **G-1 D16** — entity, markets, customer type (blocks Phase 3).
2. **G-2 Stripe** — written restricted-business confirmation; D9 take rate; payout regions (blocks P3 billing/invoices and P4 payouts; prepaid top-ups are P5a, not blocked here).
3. **G-3 Counsel** — payments/VAT/MoR/P2B/DSA scope (blocks P3/P4).
4. **G-4 Data protection** — pilot terms, privacy notice, DPA, sub-processor list, retention (blocks first partner data).
5. **G-5 Design partners** — 3–5 LOIs; A1/A9 (blocks partner sessions and P2/P3 demand exits).
6. **G-6 LeadGenerator source review (D15)** — API terms, Art. 14 notice, outreach law (blocks P2.10/J3).
7. **G-7 Live-model provider keys** — nightly golden gates and reconciliation.
8. **G-8 Penetration test** — blocks P5b only.
9. **G-9 Tax adviser VAT view** — blocks P3 exit.
10. **G-10 A1–A9 assumption tests** — demand/value tests block their listed phase exits.

Also inherently human: the narrator test (P0), willingness-to-pay interviews (P3), ≥ 15 active paying workspaces (P4 entry), ≥ 5 invited apps used weekly (P4 exit), ≥ 8 WTP interviews (P3).

---

## 8. Review focus (what to check on every integration)

1. Does the change preserve the **one enforcement component** (no second approval path) and stamp/re-stamp caller context only at trusted edges?
2. Can a secret or Personal/Credential value appear in plaintext in state, signals, reads, traces, logs, vectors or tool results? (Canary test must stay green once it exists.)
3. Does every new tool schema come from `TypeCatalog`, and does every error name the allowed values?
4. Is persisted state changed? Is the migration named (T1) or is the clean break cited?
5. Did the task list its deletions, and did the deletion actually happen (net lines reported)?
6. Is the assistant still the first user of the capability (machine-readable before human listing), and is discovery precision a gate?
7. Does the receipt remain a durable record, never derived from sampled traces?
8. Does the trace stay within budget (idle < 20/min, J1 ≤ 25, authoring ≤ 45) with GenAI spans and W3C context into child processes?
9. Are paid or side-effect steps never auto-retried, and is the failure policy applied on the ledger?
10. Are parallel workers touching disjoint files, and was the integration gate (build/run/test/review) passed before merge?
