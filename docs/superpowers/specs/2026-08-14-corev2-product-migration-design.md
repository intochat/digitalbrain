# CoreV2 Product Migration Design

**Date:** 2026-08-14

**Status:** Superseded by `2026-08-14-corev2-journal-chat-design.md`

## Outcome

DigitalBrain starts through Aspire as a clean CoreV2 product. Aspire launches durable Orleans infrastructure, a dedicated DigitalBrain runtime silo, the ProductHost as an Orleans client, and the Flutter shell. The Flutter shell discovers enabled modules and invokes and observes their public operations through the ProductHost. No running CoreV2 project references a V1 project or uses ProductHost-local persistence for Core activity, graph, delivery, wiring, module, or scheduling state.

## Current Reality

The CoreV2 branch contains a well-tested framework proof and a product authority/catalog boundary. It does not yet contain a production runtime composition. The acceptance host wraps an in-memory proof runtime in one test grain, while most classes named `*Grain` are internal runtime objects rather than deployed durable Orleans grains.

The rejected ProductHost composition duplicated Core state in EF-backed host records and was correctly reverted. The generated AppHost scaffold is uncommitted, empty, version-inconsistent with central package management, and currently breaks the solution build. The previous migration status and twenty-task cutover plan live untracked in the master worktree and do not describe the product worktree accurately.

## Target Process Topology

```text
Aspire AppHost
  ├─ Azurite / production Azure Storage
  │    ├─ Orleans clustering
  │    ├─ reminders
  │    └─ named grain stores
  ├─ DigitalBrain.RuntimeHost          Orleans silo
  │    └─ explicit CoreV2 module set
  ├─ DigitalBrain.ProductHost          Orleans client + HTTP/SSE/MCP
  ├─ Flutter shell                     ProductHost HTTP/SSE client
  └─ optional module resources         Ollama/OpenAI, Qdrant, upstream MCP
```

The AppHost declares resources, references, health dependencies, and development parameters. It does not contain domain behavior or persistence code. `DigitalBrain.RuntimeHost` owns the Orleans silo and activates durable CoreV2 grains. `DigitalBrain.ProductHost` authenticates callers and adapts transports to the Orleans client boundary. Flutter owns presentation and resumable client state only.

## Project Boundaries

The migration introduces these CoreV2 projects:

- `Brain.Aspire.Hosting`: AppHost-side `AddDigitalBrain`, the DigitalBrain builder/reference model, stable resource names, and module resource projections.
- `Brain.Aspire`: application-side ServiceDefaults, Orleans silo configuration, Orleans client configuration, and configuration-name constants shared with hosting.
- `DigitalBrain.RuntimeHost`: a minimal executable that calls the runtime hosting extension and registers the explicit product module set.
- `DigitalBrain.AppHost`: a thin C# AppHost using only Aspire hosting and module-hosting packages.
- `DigitalBrain.ProductHost`: an ASP.NET Core Orleans client exposing the product protocol; it owns no Core durable state.
- `Brain.Modules.*.Contracts`, `Brain.Modules.*`, and optional `Brain.Modules.*.Aspire.Hosting`: one contract, implementation, and infrastructure projection boundary per module.
- `Brain.Modules.UI.Aspire.Hosting`: the module-owned Flutter launcher with window, web, and headless modes, ProductHost endpoint injection, readiness ordering, and development hot reload.
- `src/CoreV2/UI/Flutter/core` and `shell`: the authority-neutral protocol client and the open-source product shell.

Core packages keep their existing purity constraints. Cross-module references target contracts projects only. AppHost packages never reference runtime implementations. One top-level declared type lives in each matching source file.

## Aspire Resource Model

`AddDigitalBrain("brain")` creates one logical DigitalBrain resource backed by an Aspire Orleans service. Its default development fabric uses persistent Azurite resources for clustering, reminders, and named grain storage. A silo project calls `WithReference(brain)`; a client project calls `WithReference(brain.AsClient())`. Both wait on required infrastructure, while clients additionally wait on the runtime project in the AppHost.

Module hosting extensions contribute projections to the DigitalBrain builder. A projection may add an external resource or inject configuration into the runtime/ProductHost, but it cannot register runtime module behavior. Runtime behavior comes from the explicit product module set inside `DigitalBrain.RuntimeHost`.

All Aspire packages use the repository's centrally managed compatible version set; packages without a matching preview remain on their verified stable version. The AppHost SDK and CLI compatibility are verified before runtime work begins. ServiceDefaults package versions come only from `Directory.Packages.props`.

## Durable Runtime Boundary

Production behavior is not hosted by copying the in-memory `ProofRuntime`. The runtime exposes narrow Orleans grain interfaces for product operations and activity observation. Durable state is stored through Orleans persistent state and reminders. The initial distributed proof moves one existing Proof operation through this boundary before additional modules are introduced.

The ProductHost client implementation translates a typed product invocation into an Orleans grain call and translates the resulting durable activity into a redacted product projection. ProductHost restarts do not affect activity completion. Repeating the same idempotency key returns the same activity. Workspace and principal always come from the validated access grant.

## Product Protocol

The shared northbound protocol remains:

```text
GET  /health
GET  /alive
GET  /v2/modules
GET  /v2/operations
POST /v2/operations/{operationId}:invoke
GET  /v2/activities/{activityId}
GET  /v2/activities/{activityId}/events
POST /mcp
```

`/v2/modules` exposes product-safe module status: enabled, ready, needs setup, or unavailable. Operation discovery returns only operations belonging to enabled modules and allowed by policy. Invocation requires an idempotency key. Activity reads and SSE are workspace-scoped redacted projections. MCP is an adapter over the same catalog and invocation services, not a second runtime path.

## Module System

Modules are migrated one at a time through the same complete path:

1. versioned contracts and one explicit `ModuleManifest`;
2. durable runtime behavior and state ownership;
3. explicit runtime registration;
4. product operation adapter and JSON binding;
5. optional Aspire resource projection;
6. focused unit tests and one distributed acceptance scenario;
7. Flutter presentation only after the product protocol is stable.

The launch module set is explicit and ordered. The initial clean local set contains Proof, Conversation, Scheduling, and Behavior. AI and Memory are enabled when their local resources are configured. Google and Salesforce are enabled when their upstream MCP endpoints and connection policy are configured. Missing optional infrastructure reports `NeedsSetup`; it does not prevent the base product from starting.

Conversation owns transcripts and turn lifecycle. AI owns typed generation/transcription capabilities, not conversations. Memory owns workspace-scoped recall artifacts. Scheduling owns reminder-backed schedules. Behavior owns declarative revisions, validation, activation, and scheduled/invoked execution. Google and Salesforce own policy-controlled upstream MCP bridges and never expose credentials or provider object models to Core.

## Flutter Migration

The Aspire Flutter integration is migrated before the full screen set. It preserves the master composition shape: `brain.AddModule<UiModule>(ui => ui.WithWindowHost())`, with `WithWebHost` and `WithHeadlessHost` alternatives. The module-owned extension launches the Flutter shell as an executable resource, injects the ProductHost endpoint, waits for ProductHost health, and preserves hot reload support.

Flutter migration proceeds by protocol capability: module discovery, operation discovery, invocation receipt, activity read, SSE reconnection, then module-specific screens. The first runnable UI is a generic module/operation browser capable of invoking the Proof operation. Conversation becomes the first dedicated product screen. Existing reusable widgets may be ported, but V1 transport, identity, graph, journal, and DTO models are not copied into CoreV2.

## Error, Security, and Recovery Rules

- Runtime startup fails for invalid durable-store or duplicate module configuration.
- Optional module dependencies yield `NeedsSetup` instead of crashing the base product.
- ProductHost authenticates before catalog lookup and ignores caller/workspace fields supplied by clients.
- Credentials remain behind opaque connection references and never enter activities, logs, SSE, MCP results, or Flutter state.
- Aspire health means process and dependency readiness; distributed acceptance tests separately prove silo/client and product connectivity.
- Runtime restart tests prove durable activity, conversation, schedule, and behavior state recovery.
- ProductHost and Flutter restarts prove resumable observation without duplicate execution.

## Migration Slices and Gates

Every slice is independently reviewable, testable, and committed:

1. Correct status and establish one green version-aligned AppHost scaffold.
2. Add and model-test the AppHost-side DigitalBrain resource.
3. Add and unit-test runtime hosting configuration.
4. Start a minimal runtime silo through Aspire.
5. Add and integration-test the Orleans client extension.
6. Start ProductHost as a client through Aspire.
7. Run Proof through a durable distributed operation/activity boundary.
8. Add module discovery, operation discovery, invocation, activity read, and SSE as separate slices.
9. Migrate Flutter Aspire hosting and a generic operation browser.
10. Migrate Conversation, then AI, Memory, Scheduling, Behavior, Google, and Salesforce independently.
11. Add remote MCP over the shared product surface.
12. Delete V1 source only after architecture tests prove no remaining references and Aspire acceptance passes from a clean checkout.

Each slice uses a failing focused test first, passes its focused test, passes affected architecture tests, and leaves the solution buildable with warnings treated as errors.

## Completion Evidence

The migration is complete only when all of the following are true:

- `dotnet build DigitalBrain.slnx -c Release -warnaserror` passes.
- every CoreV2 test project passes in Release.
- Flutter core and shell tests pass.
- `aspire start --isolated --non-interactive` starts the declared resource graph.
- Aspire waits report storage, runtime, ProductHost, and Flutter ready/running.
- a clean local user can open Flutter, see module status, invoke a working operation, and observe its terminal activity.
- runtime and ProductHost restart acceptance passes without duplicated work or lost durable state.
- no active project references `src/Kernel`, `src/Modules`, or a `DigitalBrain.*` V1 assembly.
- the old AppHost and V1 source trees are removed only in the final verified cutover commit.
