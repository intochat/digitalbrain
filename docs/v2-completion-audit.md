# DigitalBrain V2 completion audit

This matrix is intentionally conservative: a green unit test does not claim an end-to-end outcome unless the corresponding runtime path is wired and exercised.

| Area | Current evidence | Status |
| --- | --- | --- |
| Canonical tenant/workspace/principal context and grain IDs | `V2Contracts`, `V2GrainIds`, isolation tests | Implemented and unit-verified |
| Signed sessions, refresh rotation, revocation | `V2SessionTokenService`, `V2SessionManager`/`FileV2SessionManager`, HTTP refresh/logout routes, principal-kind/assurance claims, lifecycle tests | Implemented and unit-verified; AppHost wires file-backed sessions |
| One-use operator bootstrap and `brain.admin` gate | `V2OperatorBootstrap`, profile-aware MCP capability advertisement, policy tests | Implemented and unit-verified; AppHost default keeps admin disabled |
| Aggregate commits, checksum sealing, inbox/outbox, effect history | in-memory/file stores, Orleans grain, V2 workflow aggregate registration, and crash-window tests | Implemented; broader multi-silo crash verification pending |
| V2 schema/type registry | Fail-closed versioned `V2SchemaRegistry` with stable envelope/workflow registrations in the V2 kernel composition | Implemented and unit-verified |
| Proposal/approval state machine | `V2Workflow` plus `V2WorkflowAggregate` enforce authenticated operator approval, no stable Approved state, guarded transitions across apply/retry/unknown/compensation/manual-intervention states, and persist the approval audit, `ApplyQueued` state, and ordinal-zero first effect through one V2 aggregate commit/inbox boundary | Implemented and unit-verified; multi-silo crash verification remains pending |
| Projection scan/checkpoint/quarantine | `V2ProjectionWorker`, file sink, replay tests, and file-backed query projection store wired to the fresh AppHost V2 namespace; Production MCP now fails closed unless durable operation/session/projection paths are configured | Implemented locally with production-shaped durable-path guard; managed production backend wiring remains pending |
| Scoped query boundary and opaque cursors | projection query port and MCP routes | Implemented and unit-verified |
| Scoped conversation owner and structured tool outcomes | `V2ConversationOwner` and contract tests | Implemented and unit-verified |
| Model routing policy | `V2ModelRouter` policy tests | Implemented and unit-verified; live provider health paths pending |
| OAuth credential security and state machine | V2 connector project and fake adapter tests | Implemented with fake providers; live sandbox verification unavailable |
| Private feed, retention, restart recovery | in-memory/file feed stores and tests | Implemented and unit-verified |
| Operation status restart recovery | JSONL operation store wired by AppHost MCP path; command envelope is appended with operation as the durable handoff record; single-use durable claim, polling execution worker, application dispatcher, Orleans-backed effect command handlers, and secret-safe poison-record quarantine are covered by restart/build tests | Implemented and unit-verified |
| Durable UI actions | token hashing, max uses, operation/idempotency records | Implemented and unit-verified; downstream UI submission integration pending |
| MCP authentication/admission | HTTP middleware, V2-only HTTP composition, authenticated `brain_read`/`brain_act`/`brain_approve`/`brain_admin` tools, application/query ports, durable command dispatcher registration, guard, gRPC metadata helper, and a narrow `IV2EffectWorkerPort` boundary (MCP no longer injects `IGrainFactory`) | Implemented and unit-verified; live authenticated client contract pending |
| Telemetry redaction/cardinality/drop accounting | `V2TelemetryBuffer` bounds both metric and trace queues, redacts labels/details, accounts drops, and is registered in kernel and HTTP MCP compositions | Implemented and unit-verified; collector/dashboard wiring pending |
| Deployment preview/drift policy | pure preview validator and tests | Implemented and unit-verified; no cloud apply performed |
| Flutter V2 protocol/runtime | decoder, session/feed/action controllers, full `flutter test` (62 passed); analyzer completes with existing non-V2 warnings/infos | Implemented and locally verified |
| Optional P3 spikes | `docs/adr-v2-p3-decisions.md` records measured/architectural reasons to reject new search, Agent Framework/MCP Tasks, and generated RFW runtime dependencies | Proven irrelevant; no runtime integration required |
| Aspire V2 composition | AppHost V2 env/profile wiring, doctor/start smoke | Locally verified |
| Full V2-only cutover and removal of all V1 runtime dependencies | V2 excludes legacy MCP/read tools (including stdio, which now fails closed, and legacy tool DI), kernel MCP, Gateway/UI gRPC, HomeFeed/Synapse streams and buses, signal subscriber, upload/OAuth handlers, and legacy chat/voice/PackConfig/seeder/connector/Ino registrations. Source projects and other legacy composition code remain for the V1 branch | Partially complete; not complete |
| Production-shaped multi-silo, failure, restore, and live-provider verification | no live cloud credentials or deployment permitted; full .NET suite passes (all projects, 386 tests), V2 suite passes (41 tests), and Flutter suite passes (62 tests) | Live verification not available; fakes/emulators used |

V1 persisted data was not migrated, rewritten, or deleted. No production infrastructure was applied.
