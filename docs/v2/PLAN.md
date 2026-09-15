# DigitalBrain v2: durable composed behaviors

## Agreed product contract

DigitalBrain is the main project. A composing assistant produces a saved definition from registered capabilities, not executable code. Agents inside the graph produce structured decisions without invoking tools. Filters and mappings are neurons. The runtime validates payload contracts before activation and validates messages during execution. Capability registration controls shared versus owned lifetime. Definitions and active processing survive restart using DigitalBrain's existing journals, snapshots, reminders, command deduplication and announcement outbox. Retryable work reuses stable identities; uncertain non-idempotent effects pause for inspection.

## Compatibility

Work on branch `v2`, based on the existing `feat/standing-composition` checkout, preserving its unfinished RecipeNeuron, NeuronInvoker and composition tests. Keep existing signal aliases, storage keys, descriptor methods, conversational agent tools, module integrations and Flutter HTTP routes. Do not import NewPM's in-memory Orleans host or replace DigitalBrain's durable substrate. No production database migration/deletion or external provider configuration is part of this change.

## Design

- Add a durable `IBehavior` neuron with save, validate, start, stop and read commands, exposed through existing describe/call tools.
- A serializable definition declares named nodes, capability ids, configuration, signal contracts, and directed connections. Resource identity is distinct from the role in one behavior.
- Own processors under deterministic behavior/revision/role ids. Shared sources retain their original ids and configuration. Per-owner connection leases prevent one behavior from disconnecting another or removing manual connections.
- Start/stop are durable, retryable lifecycle work. Validate before touching resources; configure processors before opening upstream delivery. Save the desired lifecycle before performing remote effects. Recovery reconciles the same identities. Stopped processors reject new work; stop removes only owned edge leases.
- Reuse `Neuron<TState>` and `Announce` for processor results. Add registered filter, map, typed-method action and structured-decision implementations. Action calls use the existing INeuronInvoker and deterministic CommandId values. Unknown outcomes are visible and require explicit resolution.
- Add durable Twitter receipt ingestion with stable post ids and a simulated provider path for tests/examples. Real provider delivery uses authenticated existing ingress conventions and requires actual configured provider access.
- Preserve recipe composition and direct module calls. New examples and assistant guidance use saved behaviors; migration must not silently convert or reinterpret historical state.

## Execution checklist

- [x] Inspect both repositories, preserve dirty work, create v2.
- [x] Launch three parallel Grok CLI planning sessions: durability, integrations, composition.
- [x] Capture planner findings and reconcile them with the actual source.
- [x] Record baseline regression results: 423 passed, 6 existing opt-in tests skipped.
- [x] Add serializable behavior contracts and conservative schema validation.
- [x] Add durable node/connection ownership with compatibility tests.
- [x] Implement behavior lifecycle on existing durable work and storage.
- [x] Implement owned filter/map/action processors and structured AI decisions.
- [x] Expose capability discovery and behavior commands to the assistant through existing tool paths.
- [x] Add provider receipt/source integration and runnable Elon-post/internal-event examples.
- [x] Verify invalid contracts, ownership collisions, shared/manual edges, repeated start/stop, restart, duplicate events, action uncertainty, and tool-free decision output.
- [x] Run existing regression suites and build; verify affected user-visible flows.
- [x] Update architecture/context and migration/operator documentation with evidence and remaining integration prerequisites.

## Verification

Run the existing DigitalBrain test executable with its Microsoft Testing Platform runner, plus focused v2 tests. Use file-backed BrainSimulation restarts for persistence. Preserve opt-in external-service tests and report their availability honestly. Run solution build with CodeGraphRefresh=false to avoid an unrelated indexing mutation. Test simulated providers without consuming real external accounts. Finish only after new behavior use cases and preserved existing functionality have concrete verification evidence.

## Reconciled Grok planning findings

Three independent CLI sessions are preserved under research/. All recommend one existing durable runtime, persisted definitions, deterministic identities, owned processing nodes, schema validation and compatibility with the current recipe path. Some proposed details conflict with current source or the approved model; the implementation resolves them as follows:

- Connection leases are additive, matching the approved shared-owner semantics. A second behavior may use the same physical edge; it does not replace or reject the first owner. Existing Synapse wire fields stay unchanged.
- Lifecycle orchestration uses durable values plus the current pending work queue. It does not call SaveAsync multiple times in one snapshot reaction: DigitalBrain explicitly forbids that. Processing neurons still use one snapshot/outbox commit per input.
- Workspace ConversationalAgent lacks generic graph tools. It receives small behavior tools using the same IBehavior commands; MCP and AgentNeuron continue through existing describe/call. No second execution engine is introduced.
- Source types and action methods must already be registered in DigitalBrain. Source events declare payload contracts and all owned processing validates actual input. Arbitrary executable configuration is unavailable.
- Function-invocation middleware cannot be trusted to enforce a tool-free model response merely because Tools is empty. Structured decisions reject that middleware and use a direct configured chat client.
- Real X credentials, subscriptions and external delivery are deployment prerequisites. The migration includes durable typed receipt ingestion and deterministic test provider input, without inventing a live connection.
