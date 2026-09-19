# Next steps after Google neuron/e2e and slim IntoChat

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the gaps that block a *product* loop (webhook → signal → UI, real Auth SDK, honest docs) without growing `DigitalBrainSimulation` again.

**Architecture:** Keep the three test layers. Next work is **on Google/IntoChat/SDK**, not on NeuronTesting. Simulation stays silo-only. Module e2e stays `ModuleWebHost`. App e2e stays Aspire.

**Tech Stack:** Same as now (Orleans 10, Aspire 13.5, Flutter web, Playwright).

---

## Done (do not redo)

- Kernel + Time neuron tests; `DigitalBrainSimulation` without Kestrel.
- `DigitalBrain.Sdk` (`Auth/`, `Webhooks/`, `HttpSurfaces/`).
- Google: `IGmail.AcceptWatchPush` → `MailReceived`; `POST /google/gmail/watch`; OAuth **stub** GET; neuron + module e2e green (47 tests in the last matrix).
- Slim AppHost: Time + Flutter + Twitter fakes; `IInbox`; Flutter `Kind`; health e2e after silo `BrainClient` (no `UseOrleansClient`).
- ElonBitcoin as `IBehavior` on `IDigitalBrain`.

## Do not do next

- Put HTTP/`UseHttp` back on `DigitalBrainSimulation`.
- Time Aspire.Hosting or Time e2e.
- Port Excel/ClickHouse/Salesforce/AI until Google Auth + AppHost Google module are real.
- Runtime codegen of behaviors.

---

### Task 1: Fix stale testing docs

`src/Testing/README.md` still describes `PersistenceDirectory`, `StorageFaults`, `HoldNextRead`. Those APIs are gone.

- [ ] Delete the “State recovery and failures” section (or rewrite to: neuron tests use Orleans memory storage; no file grain store).
- [ ] Commit `Drop deleted persistence APIs from the testing README.`

---

### Task 2: Re-run Elon **app** e2e (Playwright)

HealthFacts passed after `b3950df6`. `ElonBitcoinUiFacts` was written then failed on `UseOrleansClient`. That clash is fixed.

- [ ] `dotnet test src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false --filter ElonBitcoin`
- [ ] If Flutter web/Playwright missing: install Playwright browsers; keep Kind=Web.
- [ ] Fix only product bugs (inbox poll, CORS, webhook not on IntoChat pipeline). Do **not** change Simulation.
- [ ] Success: Chromium sees “Bitcoin to the moon”.

IntoChat must `MapDigitalBrainModules` / `UseModuleHttpSurfaces` so Twitter webhook and `/ui/inbox` exist on the **AppHost** process (not only `ModuleWebHost`). Verify `Program.cs` maps module endpoints.

---

### Task 3: Google on the slim AppHost

AppHost today: Time + Flutter + `TestTwitterModule`. Google is not loaded, so Gmail watch never hits the real silo.

- [ ] `AppHost.cs`: `.AddModule<GoogleModule>()`.
- [ ] IntoChat references `DigitalBrain.Modules.Google` if not already (slim csproj).
- [ ] App e2e **or** a Google-in-AppHost fact: `POST` IntoChat `google/gmail/watch` then `GET /ui/inbox` **or** Observe — pick HTTP so Playwright can stay later.
- [ ] Optional: a tiny `IBehavior` (Gmail → Inbox) like ElonBitcoin: `On<MailReceived>(gmail)` → `IInbox.Appear`. That is the “email showed in UI” story.

Success: one in-process Google e2e stays; one AppHost POST watch → inbox JSON contains the address.

---

### Task 4: Real Auth on SDK (still no live Google account)

OAuth callback is `Results.Ok()`. `AddGmailAuthentication` is Compile-Remove (Abstractions).

- [ ] Port **one** login: browser redirect + callback stores a **test** token (in-memory), using `DigitalBrain.Sdk.Auth` (`BrowserLoginSurface`, `TokenHandoff`) on the **new** kernel. No `DigitalBrain.Abstractions`.
- [ ] Neuron: `IGmail` stays signal-first; do not revive Command `Connect`.
- [ ] Module e2e: GET callback with a fake `code` → 200 and grain/connection readable **or** a `GmailConnected` signal. Mock token endpoint in `ConfigureSilo`.
- [ ] Do not call accounts.google.com in CI.

---

### Task 5: Pub/Sub envelope (optional, after 3–4)

Production Gmail watch is Pub/Sub `{ message: { data: base64 } }`, not raw `GmailWatchPush`.

- [x] Unwrap in `GoogleModule` webhook; keep `AcceptWatchPush` as the neuron API.
- [x] E2E posts a Pub/Sub-shaped body. Neuron tests still post the inner DTO via grain.

---

### Task 6: Next module clone (only after Google Auth e2e is honest)

Copy the Google pattern to **Microsoft** or **Salesforce** (one, not both): Compile-Remove Abstractions, one signal, one webhook, `Tests/` + `E2E/`. Stop if the module has no HTTP surface.

---

## Order

```
1 README
2 Elon Playwright on AppHost (closes the UI loop we already built)
3 GoogleModule on AppHost + optional MailReceived → Inbox behavior
4 Sdk Auth + fake token (module e2e)
5 Pub/Sub envelope
6 Clone pattern to one more HTTP module
```

Task 2 and Task 3 can overlap if different people; Task 4 depends on Sdk Auth types compiling without Abstractions.

## Simulation

Leave `DigitalBrainSimulation` alone unless a bug appears. Remaining knobs (`UseReminders`, `ConfigureSilo`/`ConfigureClient`) are load-bearing for Time. Do not “simplify” them in this plan.
