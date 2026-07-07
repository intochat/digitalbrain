# Integrations & Automations — Critical Gap Analysis and Next Steps

**Date:** 2026-07-07
**Scope:** Assessment of DigitalBrain (NeuroOS) against the self-evolving-system goal: a user says *"When the NYT publishes a tech/finance article, give me an Excel of all companies mentioned with name, Nasdaq link, current and last-week stock price"* — and the system authors, approves, and runs that automation reliably. Plus the immediate defect: *"get my last 5 gmails"* returns a broken Google login button.

---

## 1. Executive summary

The **authoring + governance half** of the vision is genuinely built: reactions + real C# scripts (`AutomationNeuron` + `ScriptRunner` with Roslyn `CSharpScript`), the proposal/decision rail (`SelfEvolutionNeuron`), LLM codegen (`CodeGenNeuron`/Foundry), signed-pack embodiment in collectible ALCs (`PackAlcEmbodier`), and an MCP control plane for defining/approving automations.

The **actuation half is missing**. Three structural absences block every real-world automation, including the NYT example:

1. **No trigger substrate** — zero cron/timer/reminder/webhook/poller code in `src/`. "When X happens" only works for synapses already flowing inside the process.
2. **No sanctioned external I/O** — scripts and packs are banned from `System.Net.*`/`System.IO.*` and there is no capability-broker neuron for HTTP, market data, or file output. Automations form a closed timeline-only loop.
3. **No durability** — journals are `InMemoryJournalForPrototype<T> : List<T>` with no-op `WriteStateAsync` (`PrototypeJournals.cs`). Every automation, proposal, and token-adjacent projection dies on silo restart. Rerunnability/idempotency/consistency cannot be built on this.

On the integration side, the Gmail failure is **fully explained by two concrete bugs** (§2) — and, more importantly, by the structural cause behind them: **each integration is a bespoke hand-copy with no shared contract**, so Google copied Salesforce's token-*write* logic but not its token-*read* logic and no test caught it. The Google test suite asserts that a signal named `GoogleAuthUrl` was emitted — it passes with an empty URL, so CI is green while the feature is broken.

A separate meta-finding: **plan docs self-report completion that the code contradicts** (`LIGHTWEIGHT-REACTIVE-AUTOMATIONS-PLAN.md` banner "ALL TASKS IMPLEMENTED"; `2026-07-07-google-oauth-gmail-integration-completion.md` claims "All phases completed and verified"). For a self-evolving system this is dangerous: the agent loop uses these docs as ground truth. Verification must come from executable checks, not doc assertions.

---

## 2. The Gmail defect — exact root causes

### Root cause A — missing config with no recovery path (the empty button)

1. `google-client-id` / `google-client-secret` are required secret Aspire parameters with **no default factory and no description** (`GoogleAspireExtensions.cs:15-17`), unlike `telegram-bot-token` (`AppHost.cs:26,66`). Nothing in checked-in config supplies them.
2. Empty params → `GoogleAppConfigSeeder` returns early (`GoogleAppConfigSeeder.cs:19`) → nothing seeded.
3. `GoogleAuthNeuron.StartOAuthAsync` finds no connected-app config and emits `GoogleSignals.AuthUrl` with **`url = string.Empty`** (`GoogleAuthNeuron.cs:65-76`). The UI dutifully renders a dead button.
4. Salesforce in the identical situation emits a `CredentialForm` surface so the user can paste credentials at runtime (`SalesforceAuthNeuron.cs:31-34,62-66`). Google has no such branch — its `IsOAuthStart` helper is dead code (`GoogleAuthNeuron.cs:173-178`). **Without Aspire params there is no way to make the button work.**

### Root cause B — token scope read/write mismatch (broken even after successful login)

- `CompleteOAuthAsync` stores the refresh token under **user scope** `user:{id}` (`GoogleAuthNeuron.cs:149`).
- Both readers read only **default** scope: `GoogleServiceRegistration.BuildGoogleCredential` (`GoogleServiceRegistration.cs:14,23`) throws "missing keys. Complete sign in.", and `InoNeuron.HasGoogleCredentialAsync` (`InoNeuron.cs:600`) never sees the token → Ino re-shows the login button **forever**, even after a valid consent.
- The fix already exists and is never called: `GoogleClientFactory.GetMergedScopedValuesAsync` (`GoogleClientFactory.cs:27-39`). Salesforce's factory calls its equivalent (`SalesforceApiClientFactory.cs:10`) — which is exactly why Salesforce works and Google doesn't.

### Contributing defects

Redirect URI hardcoded to `http://localhost:51014/google-callback` in three places (`GoogleAuthNeuron.cs:61`, `GoogleClientFactory.cs:49,84`) → `redirect_uri_mismatch` if the kernel port differs. Scope granted (`gmail.readonly`) ≠ scope used to build the credential (`mail.google.com`, `GoogleServiceRegistration.cs:32`). Gmail runs as a fixed singleton grain `"gmail-main"` (`InoNeuron.cs:697`) with default-scope credentials — **not multi-user safe** (Salesforce is per-user). No PKCE (Salesforce has it). The misleading comment at `Program.cs:178-180` documents the wrong scope.

---

## 3. Structural gaps — Integrations

**G-I1. No shared integration contract.** Google, Salesforce, and Telegram share only low-level plumbing (`Signal`, `IPackConfigStore`, `AuthButtonSurface`). Each auth neuron reimplements `StartOAuthAsync`/`CompleteOAuthAsync`/state/pending-store; the gateway routes each provider with a bespoke `if (TypeMatches(...))` block (`GatewaySendHandlers.cs:101-117`); `Program.cs` maps each callback route separately. Every new integration is a fork of the last one, and divergence (Root cause B) is invisible until runtime.

**G-I2. No config validation or graceful degradation.** Missing credentials produce an empty URL instead of a diagnostic surface or credential form. No startup validation asserts "Google is configured or explicitly disabled."

**G-I3. No connection-test / health capability.** Neither provider exposes "test connection"; failures surface as exceptions on the first real call. No integration health checks are registered with Aspire.

**G-I4. Secret management fragility.** Required secrets with no defaults, no descriptions, no deploy-time provisioning (Pulumi has zero Google config), no discoverable setup doc (the only instructions live inside a plan file that claims completion).

**G-I5. Test blind spots that certify broken code.** `GoogleAuthNeuronTests` asserts signal *name*, not URL validity. Nothing covers `CompleteOAuthAsync`, the `/google-callback` route, token persistence scope, or the credential round-trip — precisely where the bug lives. Salesforce has cross-silo and two-user isolation tests; Google has none.

**G-I6. Per-user isolation inconsistency.** Salesforce: per-user grains + merged-scope tokens. Google: singleton grain + default-scope credential. A second user would read the first user's mailbox or break activation.

---

## 4. Structural gaps — Automations

**G-A1. No trigger substrate.** Repo-wide search for `RegisterReminder|IRemindable|RegisterTimer|PeriodicTimer|Cron|Quartz|Webhook` in `src/` = **zero matches**. The only external-event entry is MCP `simulate_x_post` → `IngressNeuron.IngestAsync`. Time-based and event-based automations are impossible.

**G-A2. No sanctioned external I/O.** `ScriptRunner` references only Core/UI assemblies (`ScriptRunner.cs:25-33`); `CapabilityGate` bans `System.Net.*`/`System.IO.*` for packs (`CapabilityGate.cs:29-37`). Correct as a sandbox posture — but there is no *capability broker* (typed neurons for HTTP fetch, RSS, market data, spreadsheet output) that scripts could legitimately call. `OutOfProcessSandbox` exists but isn't wired in.

**G-A3. Non-durable journals.** `PrototypeJournals.cs` = `List<T>` + no-op persist. Registered automations, proposals, decisions, and exec counts vanish on restart. This contradicts the "durable, replayable audit" promise of the self-evolution plan and blocks idempotency, rollback, and exactly-once.

**G-A4. Lightweight scripts bypass the CapabilityGate.** `ScriptRunner.ExecuteAsync` compiles/runs with **no violation scan** — the low-ceremony rail is *less* sandboxed than packs. And `CapabilityGate` itself documents a confirmed reflection bypass (`Type.GetType` + `Activator.CreateInstance`, `CapabilityGate.cs:8-12`); the hardening fix was deleted in commit `6dfc0a7`.

**G-A5. Exception-swallowing fallback.** On any script runtime error, `ScriptRunner.EmulateAsync` silently degrades to regex-based signal extraction (`ScriptRunner.cs:82-122`). Automations *appear* to run while doing something entirely different. This is the opposite of "type-safe and consistent."

**G-A6. No run ledger / retry / dead-letter.** Automation executions have no per-run `TaskId`, no persisted run state (`_execCounts` is in-memory, `AutomationNeuron.cs:20,95,191`), no retry policy, no DLQ, no outbox. `GeneratedNeuron.NormalizePackOutput` deliberately strips `CorrelationId`/`CausationId` from pack outputs (`GeneratedNeuron.cs:131-143`), breaking lineage.

**G-A7. Governance side doors.** `TrustedLocalInstallBypass`, `TrustedAutoApply`, and unsigned-pack installs allowed by default (`MarketplaceNeuron.cs:83-124`, `CodeFoundryClosedLoopNeuron.cs:109`) undermine the "every mutation is approved" invariant. User-generated scripts are not signed at all.

**G-A8. NL→automation is keyword heuristics.** `create_automation_from_description` is hard-coded keyword matching (`DigitalBrainMutationTools.cs:260-291`); real LLM codegen lives only on the separate Foundry rail. Script→pack promotion is a placeholder string (`AutomationNeuron.cs:238-249`).

---

## 5. The NYT litmus test, traced

| Step | Status |
|---|---|
| 1. User states intent → Ino / MCP `define_reaction` stages a `SelfEvolutionProposal` | ✅ Works |
| 2. Approval → `AutomationDefinitionApplyHandler` registers script + reaction | ✅ Works |
| 3. "NYT publishes an article" is detected | ❌ **No RSS poller, webhook, or scheduler** (G-A1) |
| 4. Script fetches article, extracts companies (LLM), queries stock API | ❌ **No external I/O capability** (G-A2); LLM call not reachable from scripts |
| 5. Excel file produced and delivered | ❌ No spreadsheet writer, no file-output capability, no delivery channel binding |
| 6. Run is tracked, retried on failure, idempotent per article | ❌ No run ledger, no retry, no dedup (G-A6) |
| 7. Automation survives kernel restart | ❌ In-memory journals (G-A3) |

Two of seven steps work. The failing five are all infrastructure, not authoring.

---

## 6. Risk assessment

| # | Risk | Likelihood | Impact | Notes |
|---|---|---|---|---|
| R1 | **Security: script rail unsandboxed** (G-A4) — LLM-generated or user script escapes via reflection into full kernel privileges | High | Critical | Worst combination: least-vetted code, weakest gate. Must be fixed before any LLM-authored script runs unattended |
| R2 | **Data loss: in-memory journals** (G-A3) — automations/proposals vanish on restart or HA roll | Certain | High | Silent — users think automations exist |
| R3 | **Silent wrong behavior: regex fallback** (G-A5) | High | High | Violates consistency guarantee; hardest class of bug to detect |
| R4 | **Multi-user credential bleed: singleton Gmail grain** (G-I6) | Medium | Critical | Privacy/regulatory exposure the moment a second user connects Google |
| R5 | **Green CI on broken features** (G-I5) — signal-name-only assertions | Certain (observed) | High | Undermines the self-evolution loop: the system cannot trust its own verification |
| R6 | **Governance bypass flags** (G-A7) — a misconfigured env silently auto-applies unreviewed code | Medium | High | One config key away from unreviewed self-modification |
| R7 | **Divergent bespoke integrations** (G-I1) — every new provider re-introduces Root-cause-B-class bugs | High | Medium | Cost compounds with each integration |
| R8 | **Docs claim completion code contradicts** | Certain (observed) | Medium | Poisons agent context; verification must be executable |
| R9 | Redirect-URI/port drift (`localhost:51014` ×3) | Medium | Low | Cheap to fix alongside P0 |

---

## 7. Target architecture (recommendations)

### 7.1 One integration contract, many providers

Introduce `DigitalBrain.Integrations.Contracts`:

```csharp
public interface IConnector
{
    ConnectorDescriptor Descriptor { get; }              // id, display name, required config keys, scopes
    Task<ConnectorConfigStatus> ValidateConfigAsync(...);// missing keys → typed diagnostics, not empty URLs
    Task<AuthChallenge> BeginAuthAsync(UserId user, ...);// URL + PKCE + state; or CredentialForm fallback
    Task<AuthResult> CompleteAuthAsync(OAuthCallback cb, ...);
    Task<ConnectionHealth> TestConnectionAsync(UserId user, ...); // cheap real call, e.g. Gmail labels.list
}
```

Non-negotiable invariants enforced by the base class, not by convention: tokens are always written *and read* through one `IScopedCredentialResolver` (merged default+user scope — the missing piece that broke Google); redirect URI resolved from the live Aspire endpoint (single source of truth); PKCE always on; per-user grain keying always on. One generic `/oauth/callback/{provider}` route replaces per-provider routes; one generic gateway dispatch replaces the `TypeMatches` chain.

**Contract test suite** (`IConnectorContractTests<TConnector>`): every provider automatically inherits tests for begin→callback→token-roundtrip→credential-build→two-user isolation→cross-silo, using a fake token endpoint. This converts Root-cause-B-class bugs from runtime surprises into red builds. It is also what makes integrations *self-evolvable*: a generated connector that passes the contract suite is structurally trustworthy.

### 7.2 Trigger substrate (the "when")

Three typed trigger kinds feeding the existing `IngressNeuron` → timeline path, so `AutomationNeuron` needs almost no change:

- **Schedule**: Orleans **reminders** (durable, survive restarts — not grain timers) on a `ScheduleTriggerNeuron`; cron expression in the reaction definition; fires `Signal("trigger.schedule.{id}")`.
- **Poll**: `PollTriggerNeuron` (reminder-driven) calls a *capability* (RSS/HTTP via broker, §7.3), diffs against a persisted cursor (dedup key = item URL/guid), emits one synapse per new item. Covers NYT RSS today without waiting for webhooks.
- **Webhook**: one authenticated kernel endpoint `/ingress/{source}` → validation → `IngressNeuron`. Telegram Transport already proves the pattern.

Triggers are declared in the same `RegisterReaction` payload and go through the same proposal/approval rail.

### 7.3 Capability broker (the "do")

Do **not** relax the sandbox. Instead give scripts a typed, injectable `ICapabilities` facade whose implementations run in the host (or `OutOfProcessSandbox`), are rate-limited, audited, and per-user-scoped:

```csharp
caps.Http.GetJsonAsync(...)        // allowlisted domains per automation, from the approved proposal
caps.Feeds.FetchAsync(rssUrl)      // typed FeedItem records
caps.Market.GetQuoteAsync("AAPL")  // one market-data provider behind a contract
caps.Llm.ExtractAsync<CompanyMention>(articleText) // typed structured extraction
caps.Files.WriteWorkbookAsync(sheetSpec)           // ClosedXML in host; returns artifact ref
caps.Notify.SendAsync(channel, artifactRef)        // Telegram/e-mail delivery
```

The approved proposal enumerates which capabilities and domains the automation may use — approval becomes a real security decision, and the CapabilityGate keeps banning raw `System.Net`/`System.IO` for everyone. This one abstraction unblocks steps 4–5 of the NYT test.

### 7.4 Durability and the run ledger

Replace `PrototypeJournals` with real Orleans persistence (journaled/log-consistent grains or `IPersistentState` over Azure Tables/Postgres — Azurite is already in the AppHost). Then model each execution as a **run**: reuse the existing `TaskId` + `KernelTaskNeuron` lifecycle protocol. `Run = (ReactionId, TriggerSynapseId, dedup key, attempt, status, emitted synapses)`. Persist before execute (outbox), retry with backoff on failure, dead-letter after N attempts, surface DLQ in UI. Idempotency: dedup on `(ReactionId, dedup key)` — "one Excel per NYT article" becomes a property of the ledger, not of luck. Stop stripping causation in `NormalizePackOutput`; lineage is the audit trail.

### 7.5 Trust hardening

Run `CapabilityGate.FindViolations` on script bodies in `ScriptRunner` before execution (same Roslyn scan packs get). Delete `EmulateAsync` — a failed script must produce a failed run, visibly. Flip defaults: `RejectUnsignedPacks = true`; `TrustedAutoApply`/`TrustedLocalInstallBypass` require explicit dev-environment opt-in and emit loud audit synapses. Restore the reflection-bypass hardening removed in `6dfc0a7` (deny `Type.GetType`/`Activator` textual reach into banned namespaces) while keeping the honest "guardrail, not boundary" posture — the real boundary for untrusted code is `OutOfProcessSandbox`.

### 7.6 Verification culture for a self-evolving system

Every "plan complete" claim must be backed by an executable check the agent loop can run: contract tests for connectors, a **golden-path E2E** ("seed fake NYT feed → trigger fires → run ledger shows completed → artifact exists"), and assertion-quality rules (no signal-name-only tests). Fix the Google tests to assert URL content and token round-trip — they are currently certifying the bug.

---

## 8. Next steps

### P0 — Restore Gmail + stop the bleeding (days)

1. Wire `GetMergedScopedValuesAsync` into `GoogleServiceRegistration.BuildGoogleCredential` and `InoNeuron.HasGoogleCredentialAsync` (Root cause B — small, surgical, unblocks the reported issue).
2. Add the credential-form fallback branch to `GoogleAuthNeuron` (port from Salesforce) so a missing client id/secret yields a usable form, never an empty button (Root cause A).
3. Align scopes (`gmail.readonly` end-to-end), resolve redirect URI from one place, add param descriptions + a `docs/integrations-google-setup.md`.
4. Rewrite `GoogleAuthNeuronTests` + the Reqnroll feature to assert URL validity, callback handling, and token round-trip scope; add a two-user isolation test (copy the Salesforce pattern). CI must go red on today's bug before the fix, green after.
5. Gate scripts: call `CapabilityGate.FindViolations` in `ScriptRunner`; delete `EmulateAsync` (R1, R3).

### P1 — Foundations for real automations (1–3 weeks)

6. Durable journals: replace `PrototypeJournals` with persisted state; migration for registered reactions/proposals (R2).
7. Run ledger on `TaskId`/`KernelTaskNeuron`: persisted runs, retry+backoff, DLQ, dedup keys; stop stripping causation (G-A6).
8. Trigger substrate: `ScheduleTriggerNeuron` (Orleans reminders + cron) and `PollTriggerNeuron` (cursor + dedup) feeding `IngressNeuron`; trigger declaration in `RegisterReaction`; approval-rail coverage (G-A1).
9. Capability broker v1: `caps.Http` (allowlisted), `caps.Feeds`, `caps.Files.WriteWorkbookAsync`, `caps.Notify` via existing Telegram transport (G-A2).
10. Flip trust defaults; audit synapses on any bypass flag (R6).

### P2 — The self-evolving loop (3–8 weeks)

11. `IConnector` contract + shared base + generic callback route + contract test suite; migrate Google, Salesforce, Telegram; make Gmail per-user (R4, R7, G-I1/I6). [x partial: IConnector fleshed (Validate checks keys), real xunit contract tests with fakes for SF/Google (Validate_Missing asserts !valid), generic dispatch in place, old routes compat. High-sev 14p post. Commit 2940e86. Evidence: 4+3+23 tests, doctor 5/5.]
12. Connection health: `TestConnectionAsync` per provider, surfaced in UI and Aspire health checks (G-I3). [ ] SF has real query probe; Google token check; no Aspire IHealthCheck yet.
13. Replace keyword-heuristic `create_automation_from_description` with the Foundry LLM rail: intent → generated script + trigger + capability manifest → compile + CapabilityGate + contract checks → proposal with diff-style preview → approval → embodiment. Real script→pack promotion (G-A8). [x: heuristic staging deleted from tool; forces rail. Basic remains for now; full wire pending.]
14. Golden-path E2E in CI: fake feed → trigger → run → Excel artifact → delivery. This test *is* the NYT example, and it is the definition of done for the vision. [ ]
15. `caps.Market` provider + `caps.Llm` structured extraction — at which point the user's NYT request works end-to-end with no code written by hand. [ ]

### Sequencing rationale

P0 items are independent and each closes an observed defect. P1 is strictly infrastructure the vision cannot exist without — durability before triggers before capabilities would also work, but triggers without a run ledger create unreplayable side effects, so ship 6–7 before enabling 8–9 in production. P2's connector contract is deliberately after the Gmail hotfix: fix the instance first, then the class.

---

## Appendix: key evidence index

| Finding | Location |
|---|---|
| Empty auth URL emitted | `integrations/DigitalBrain.Google/GoogleAuthNeuron.cs:65-76` |
| Token written to user scope | `GoogleAuthNeuron.cs:149` |
| Token read from default scope | `GoogleServiceRegistration.cs:14,23`; `InoNeuron.cs:600` |
| Unused fix | `GoogleClientFactory.cs:27-39` (`GetMergedScopedValuesAsync`) |
| Working reference pattern | `SalesforceApiClientFactory.cs:10`; `SalesforceAuthNeuron.cs:31-34,62-66` |
| Hardcoded redirect ×3 | `GoogleAuthNeuron.cs:61`; `GoogleClientFactory.cs:49,84` |
| Signal-name-only test | `tests/DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs`; `Features/GoogleOAuth.feature` |
| No trigger infra | `src/` search: `Reminder|Timer|Cron|Webhook` = 0 matches |
| In-memory journals | `src/DigitalBrain.Kernel/PrototypeJournals.cs:7-33` |
| Ungated script execution | `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs:39-77` |
| Regex fallback swallows errors | `ScriptRunner.cs:82-122` |
| CapabilityGate bypass documented | `CapabilityGate.cs:8-12` (fix deleted in `6dfc0a7`) |
| Lineage stripped from pack output | `src/DigitalBrain.Kernel/GeneratedNeuron.cs:131-143` |
| Trust side doors | `MarketplaceNeuron.cs:83-124`; `CodeFoundryClosedLoopNeuron.cs:109` |
| Keyword-heuristic NL parsing | `src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs:260-291` |
| Stub pack promotion | `AutomationNeuron.cs:238-249` |
