# Customer Researcher Implementation Plan

**Goal:** Launch Customer Researcher from Flutter's Applications menu, visibly research a company in an embedded Windows browser, and save sourced company data to PostgreSQL.

**Architecture:** Flutter owns a WebView2 control. A session-bound Microsoft/Playwright neuron attaches to that exact page over loopback CDP. The application composes the input/browser surface, runs an agent with browser tools, validates the results and saves through a typed Postgres repository.

**Tech stack:** Existing .NET/Orleans/AI framework, Microsoft.Playwright 1.62.0, Flutter Windows/WebView2, Npgsql and Aspire PostgreSQL.

**Spec:** [Research and accepted proposal](../../research/2026-09-29-customer-researcher.md). The user requested execution on 2026-09-29; proceed without another approval round.

## Constraints

- Exactly two main UI rows: input/actions/status, then expanding embedded browser.
- No separate Chrome window or separate hidden research page; Playwright uses the visible WebView2 page.
- Root-level module files under Microsoft/Playwright; application-specific research logic under Apps/CustomerResearcher.
- Windows desktop first, matching the current AppHost. Unsupported platforms show an explicit message.
- Browser handles and CDP connections are transient. Only loopback endpoints and an unpredictable per-view page marker may attach.
- Cancel, disconnect and replacement stop stale actions/saves; two windows use distinct browser/page identities.
- Parameterized, workspace-scoped, retry-idempotent persistence. Keep IPostgres.Query read-only.
- Existing AI browse_web/lookup_company remain compatible.

## Interfaces between implementation areas

- `DigitalBrain.Microsoft.Playwright.IPlaywright`: `Attach(BrowserAttachment, CancellationToken)`, `Detach(string sessionId, CancellationToken)`, `Navigate(string url, CancellationToken)`, `Snapshot(CancellationToken)`, `Click(string selector, CancellationToken)`, `Fill(string selector, string value, CancellationToken)`, `Read()`.
- `BrowserAttachment(int Port, string SessionId)`. Initial target URL: `about:blank#digitalbrain-{SessionId}` with 32 hex characters.
- Navigation/snapshot/actions return `BrowserObservation(string Url, string Title, string Text, IReadOnlyList<BrowserLink> Links)`; `BrowserLink(string Text, string Url)`.
- `BrowserSession(bool Connected, string? SessionId, string? Url, string? Title)`.
- Flutter `IWebBrowser.Connect(int port, string sessionId)` emits `BrowserConnected(Name, Port, SessionId)` through its UI binding; `Disconnect(string sessionId)` emits `BrowserDisconnected(Name, SessionId)`. Neither persists endpoints.
- Browser UI events use `kind=webbrowser`, `action=connect|disconnect`, JSON `value={port,sessionId}` (disconnect only needs sessionId).
- App browser part is `browser`, app route/launch key is `customer-researcher`, app title is `Customer Researcher`.

## Tasks

### 1. Microsoft/Playwright module
- [x] Create contracts/runtime/unit-test projects and root-level neuron, session provider and configuration.
- [x] Attach only to the marked existing page; reuse it for all tools. Serialize operations, propagate cancellation, bound observations and validate public HTTP(S) navigation.
- [x] Test target/session isolation, navigation restrictions, stale detach and lifecycle behavior; verify actual CDP connection against embedded WebView2 during integration.

### 2. Flutter embedded browser and composition
- [x] Add WebView2 Flutter plugin, initialize a process-scoped local debug environment before controllers, use a unique ephemeral profile and per-view marker.
- [x] Replace browser placeholder for Windows; implement explicit unsupported state, loading/error/reopen recovery and lifecycle cleanup.
- [x] Add browser composer node, binding signals, node/event endpoints and dedicated renderer.
- [x] Test composition admission/rendering/events; build and launch Windows Flutter for same-page verification.

### 3. Customer Researcher application and storage
- [x] Compose a two-row app surface; implement scoped window/run state, submit/stop/browser-ready events.
- [x] Use existing AI client/tool infrastructure for sourced company name, website, location/address, email, phone, industry and summary. Unknown fields remain null; ambiguity requests clarification.
- [x] Add public Postgres data-source seam and typed repository with schema creation/migration and parameterized retry-safe writes.
- [x] Register application/open endpoint and AppHost modules/project references.
- [x] Test save success, database failure, cancellation, ambiguity, repeated submission and workspace scoping.

### 4. Menu and integration
- [x] Add Customer Researcher launcher using existing generic application-open route.
- [x] Add solution projects, documentation and execution instructions.
- [x] Run focused .NET suites, Flutter analysis/tests and Windows build; verify browser/agent same-page behavior and real database writes where local services permit.
- [x] Review the integrated changes and report any runtime verification gaps accurately.

## Review focus

1. A stale page/session must not be used after a component reconnects or closes.
2. A second company submission must not save results from the previous run.
3. The visible page and Playwright observations must always refer to the same target.
4. Database failures must never produce a Saved status; retry must not duplicate a research row.
5. Browser session state must remain scoped to the owning workspace/window; model arguments cannot select another window.

## Execution notes

The independent module and application areas can be implemented in parallel against these explicit interfaces. The primary agent owns Flutter integration and final verification. Do not commit product changes unless requested.

## Verification results

- Flutter backend: 62 tests passed, including connection signal validation and transient session behavior.
- Playwright: 17 tests passed with a real Flutter-owned WebView2 page, including navigation, snapshot, detach without closing the page, cancellation and exact-target reconnection.
- Customer Researcher: 7 tests passed with a disposable PostgreSQL 17 database, including parameterized writes, retry upsert, workspace isolation and stale-result fencing. The live test skips explicitly without its connection variable.
- Flutter composition suite: 32 tests passed; new browser renderer test and existing UI widgets passed; launcher tests passed.
- Analysis of changed Flutter files: clean. Whole UI analysis also reports six pre-existing informational lints in image-export files.
- IntoChat and AppHost builds passed. Windows native plugin required a target-specific opt-in for its legacy coroutine header with Visual Studio 2026.
- Review findings on idle deactivation, target identity expiration and cumulative request budgets were fixed and re-reviewed. Native cancellation testing additionally found and fixed an incomplete SDK-await race.
- Native smoke process and disposable database were stopped after verification.
- Full configured-model company research through the live application was not exercised; real browser and database integrations were validated separately.