# Specialist refactor verification

Implemented the approved [foundation and specialist-module plan](../plans/2026-09-05-neuron-foundation-and-specialist-modules.md) on `codex/day-zero-scripting`. Changes are left uncommitted for review.

## Result

- Neuron and external-facade requests now share exact sender/causation/type matching, delivery outcomes and a bounded deadline. Retained replies survive a stale observation cursor; mixed awaited delivery/binding cycles fail promptly.
- Ino delegates to real Aspire, Gmail and Salesforce agents. Each specialist prepares its own tools through the common async contract; STDIO and authenticated HTTP use shared MCP discovery/session mechanics. Native provider schemas remain authoritative within explicit admission and provider policies.
- Login continuation records the exact specialist, request and read scope, then resolves the accepted connection revision. The existing chat worker resumes it once. Old Gmail/Salesforce wrapper continuations require a fresh request.
- Gmail drafts and Salesforce create/update operations require exact, published, expiring previews and a fresh trusted confirmation. Confirmation refreshes the native catalog and consumes the proposal before attempting a write. Uncertain writes are not retried.
- Module registrations supply graph labels and icon keys; the UI kit resolves bundled assets. Scripts import both provider contracts and use the generic agent request/reply path.

Bound, Learned, Broadcast and unsubscribe semantics are preserved. `AgentActivity` is diagnostic journal evidence, not a newly promised subscription event. No provider operation DTOs, extra router, persistent credentials or multi-account interface were added.

## Automated checks

| Suite | Passed |
|---|---:|
| Simulation, including native MCP, provider policies, continuation, confirmation and GenAI telemetry | 197 |
| Substrate/facade/request/synapse | 95 |
| Scripting | 31 |
| Aspire hosting/conformance | 59 |
| Graph projection, real HTTP subscription and chat stream contract | 18 |
| **Backend total** | **400** |

The simulation run includes 13 authenticated HTTP MCP cases, 13 STDIO MCP cases and 11 real-silo continuation cases. Sensitive telemetry tests cover enabled and disabled capture.

Flutter: 34 chat/login/graph-shell tests passed; the earlier focused UI run passed 16 model/kit/graph cases. These selections overlap and are not reported as 50 unique tests. Seven changed Dart files passed analysis. The Windows Flutter app subsequently rebuilt successfully through the normal AppHost launch.

A solution restore refreshed runtime dependency manifests after the new scripting contract references. The final whitespace check passed.

## Live verification — 2026-09-05, approximately 10:19–10:23 UTC

Restarted the local AppHost and rebuilt the Windows app. Kernel health returned HTTP 200. Flutter, kernel, MCP, scripting and configured local dependencies were running and healthy.

Sent `How Many aspire resources are healthy` through `/owner/commands`, the same HTTP chat entry point used by Flutter. It returned HTTP 200, a chat acceptance and a completed assistant response: **17 healthy resources, 4 NotStarted/unknown, 0 explicitly unhealthy**. A second fresh request reused the specialist connection and returned the same **17 of 21** result. The Aspire CLI inventory with hidden resources included independently matched those counts.

Trace `36dad8601b2ec1378da20e93720d1122` shows completed Ino and Aspire agent spans, `ask_aspire`, native `list_resources`, and `gpt-5.6-luna` model spans. Model input/output and tool arguments/results were present with content capture enabled. The graph reported Microsoft/Aspire metadata, the `aspire` icon key and the actual Learned `AgentRequest` edge from Ino.

Read-only Gmail and Salesforce requests returned their login guidance. The conversation journal contained `WaitingForUser` actions with the exact principal-scoped target and native read scope:

- Gmail: `get_current_account`, `search_threads`, `get_thread`, `list_labels`.
- Salesforce: `getUserInfo`, `soqlQuery`.

Graph responses reported Google/Gmail/`gmail` and Salesforce/Salesforce/`salesforce` metadata. Provider writes were absent from the login continuation scopes.

## Remaining live validation

Credentials are intentionally volatile, so the restarted kernel needs a fresh user login for Gmail and Salesforce. Login cards, target/scope capture, routing and graph presentation were verified live; authenticated provider reads and actual OAuth completion were covered by fixtures but could not be completed live without that login. No email draft or Salesforce record was created or changed.

The app is left running. Ask Ino for an Aspire health check, or request a Gmail/Salesforce read and use the application's login card to reconnect.
