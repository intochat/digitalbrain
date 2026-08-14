# CoreV2 product migration status

Updated: 2026-08-14

## Outcome

CoreV2 is the only compiled product graph. One `aspire start` command brings up persistent storage, the Orleans runtime silo, the ProductHost client/gateway, and the Flutter desktop client through module-owned Aspire projections.

## Current truth

- The repository Aspire entry point targets `src/CoreV2/DigitalBrain.AppHost`.
- `Brain.ServiceDefaults` owns health checks, service discovery, HTTP resilience, and OpenTelemetry conventions.
- `Brain.Aspire.Hosting` models the brain aggregate, persistent Azurite storage, Orleans clustering, reminders, grain state, and role-specific module projections.
- `Brain.Aspire` provides distinct application-side Orleans silo and client registrations over AppHost-injected storage configuration.
- `DigitalBrain.RuntimeHost` owns all durable product state. `DigitalBrain.ProductHost` is an independently hosted Orleans client and exposes module discovery, operation invocation, durable activity lookup, SSE activity observation, and MCP adapters.
- Product activity and module state survive ProductHost and runtime restarts. No durable product state is stored in ProductHost.
- `Brain.Modules.UI.Aspire.Hosting` owns window, web, and headless Flutter launch modes, ProductHost endpoint injection, readiness ordering, source-watch reload, and an Aspire dashboard hot-reload command.
- The CoreV2 Flutter Windows shell compiles and launches from `aspire start`, receives `DIGITALBRAIN_PRODUCT_BASE`, and uses the Flutter tool for hot reload.
- The Flutter shell has a dedicated durable Conversation surface plus a generic discovery/invocation surface for all module operations.
- Caller and workspace identity are derived by ProductHost. Client-supplied caller/workspace headers are ignored.
- The V1 `src/Kernel` and `src/Modules` trees are uncompiled references only. Architecture tests prevent active CoreV2 projects from depending on them.

## Module launch state

| Order | Module | Status | Durable capability |
| ---: | --- | --- | --- |
| 1 | Proof | Ready | Durable activity-backed proof operation |
| 2 | Conversation | Ready | Workspace-isolated ordered transcripts and idempotent sends |
| 3 | Scheduling | Ready | Persistent schedules, cancellation, and Orleans reminders |
| 4 | Behavior | Ready | Versioned behaviors, activation, deterministic runs, and run history |
| 5 | AI | NeedsSetup | Optional provider adapter is not configured |
| 6 | Memory | Ready | Workspace/namespace-scoped store, search, and remove |
| 7 | Google | NeedsSetup | Optional provider adapter is not configured |
| 8 | Salesforce | NeedsSetup | Optional provider adapter is not configured |

## Active slice

The base product migration and live cutover are complete. Remaining work is optional provider setup for AI, Google, and Salesforce. Physical deletion of the inert V1 reference trees is intentionally deferred until those adapters reach selected V1 feature parity; V1 is already absent from the compiled and running product path.

## Live acceptance evidence

On 2026-08-14 an isolated Aspire run established this graph:

`Azurite storage -> Orleans runtime -> ProductHost -> Flutter Windows`

- All four resources reached healthy/running state.
- ProductHost returned the eight modules above in the declared launch order and exposed 13 operations.
- Conversation send/read, Scheduling schedule/read, Behavior publish/activate/run/read, and Memory store/search were invoked through ProductHost successfully.
- Restarting the runtime preserved transcript messages, the registered schedule/reminder, active behavior revision and run, and stored memory.
- Restarting ProductHost preserved activity lookup and left Flutter healthy.
- The Aspire Flutter hot-reload command completed successfully against the running Windows client.
- Aspire shut down cleanly with no remaining application instances.

## Definition of done

- `dotnet build DigitalBrain.slnx -c Release` succeeds.
- Every CoreV2 test project passes through its native .NET 11 test executable.
- `aspire start --isolated --non-interactive` reaches healthy storage, runtime, ProductHost, and Flutter resources.
- Flutter can discover and invoke module operations and observe durable activity through ProductHost.
- No compiled CoreV2 project references V1 source roots.
