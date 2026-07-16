# Everything Is a Neuron Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Do not dispatch subagents unless the user explicitly authorizes delegation.

**Goal:** Replace duplicated DigitalBrain runtime and transport concepts with a universal Neuron kernel, beginning with a proven DigitalBrain MCP invocation that changes the same Neuron-backed UI state observed by Flutter.

**Architecture:** Introduce one stable `INeuron` Orleans interface and `NeuronAddress`, then move behavior behind typed facets and Synapses. Preserve existing Session, Conversation, SurfaceFeed, Effect, Feature, and connector behavior through adapters until each specialized path has an equivalent universal contract and passing migration tests.

**Tech Stack:** .NET 11 preview, Microsoft Orleans 10.2.1, Aspire CLI/AppHost 13.4.6, ModelContextProtocol 1.4.0, Flutter >=3.44, grpc 5.1.0, protobuf 6.0.0, xUnit.

## Global Constraints

- Everything addressable is a Neuron; differences are implementation traits only.
- Use one product identity across Flutter and MCP; transport audience is never part of product Neuron identity.
- Keep one human-approved Effect rail.
- Keep external provider mutations behind approved Effect proofs.
- Do not add a second Agent, Feature, graph, shell, or MCP marketplace runtime.
- Do not expose provider MCP URLs in Flutter.
- Do not edit `.aspire/modules/`.
- Do not add source-code comments.
- Use `apply_patch` for repository file edits.
- Preserve unrelated and pre-existing workspace changes.
- Use Context7 before package-specific implementation; when quota-blocked, use primary official documentation.
- Use CodeGraph for symbol relationships and blast radius.
- Use Aspire for live resource control, logs, traces, waits, and targeted rebuilds.
- Run focused owning test projects during slices.
- Run `dotnet test --logger "console;verbosity=minimal"` from the repository root before completion.
- Record line deletion metrics after every migration phase.

## Planned File Responsibilities

### New contract files

- `src/DigitalBrain.Kernel.Contracts/Core/NeuronAddress.cs`: stable Owner/space/Neuron identity and key derivation.
- `src/DigitalBrain.Kernel.Contracts/Runtime/NeuronContracts.cs`: universal grain interface, invocation, read, receipt, event, description, lifecycle, and facet descriptors.
- `src/DigitalBrain.Kernel.Contracts/Runtime/SynapseContracts.cs`: closed Synapse relation set and authority records.
- `src/DigitalBrain.Kernel.Contracts/Runtime/UiNeuronContracts.cs`: UI Neuron projection, action, and causal reference contracts.

### New kernel files

- `src/DigitalBrain.Kernel/Runtime/NeuronGrain.cs`: universal grain activation, read, invoke, event, and persistence lifecycle.
- `src/DigitalBrain.Kernel/Runtime/NeuronDocumentValidator.cs`: state-envelope and identity validation.
- `src/DigitalBrain.Kernel/Runtime/NeuronFacetRegistry.cs`: contract-to-handler resolution.
- `src/DigitalBrain.Kernel/Runtime/NeuronInvocationPipeline.cs`: authentication context, replay, revision, Synapse policy, handler execution, persistence, and projection.
- `src/DigitalBrain.Kernel/Runtime/NeuronProjectionCoordinator.cs`: idempotent UI Neuron projection.
- `src/DigitalBrain.Kernel/Runtime/NeuronPolicyEvaluator.cs`: grant and Synapse authorization.
- `src/DigitalBrain.Kernel/Runtime/NeuronWorkFacet.cs`: shared inbox, lease, fence, retry, completion, and pause mechanics.

### New MCP files

- `src/DigitalBrain.Mcp/NeuronMcpTools.cs`: `neuron_describe`, `neuron_read`, and `neuron_invoke`.
- `src/DigitalBrain.Mcp/NeuronRequestContextFactory.cs`: authenticated transport context to Neuron caller context.
- `src/DigitalBrain.Mcp/LegacyNeuronToolAliases.cs`: temporary `ino_interact` and `feature_*` adapters.

### New Flutter files

- `app/lib/runtime/neuron/neuron_address.dart`: Dart identity model.
- `app/lib/runtime/neuron/neuron_projection.dart`: generic projection and action model.
- `app/lib/runtime/neuron/neuron_transport.dart`: generic read/invoke/watch port.
- `app/lib/runtime/neuron/neuron_controller.dart`: revisioned UI Neuron store.
- `app/lib/runtime/neuron/neuron_view_registry.dart`: closed native `viewKind` registry.

### New tests

- `tests/DigitalBrain.UnitTests/NeuronAddressTests.cs`
- `tests/DigitalBrain.UnitTests/NeuronDocumentValidatorTests.cs`
- `tests/DigitalBrain.UnitTests/NeuronFacetRegistryTests.cs`
- `tests/DigitalBrain.UnitTests/NeuronPolicyEvaluatorTests.cs`
- `tests/DigitalBrain.OrleansTests/Runtime/NeuronGrainTests.cs`
- `tests/DigitalBrain.IntegrationContractTests/NeuronContractBoundaryTests.cs`
- `tests/DigitalBrain.E2ETests/NeuronMcpUiE2ETests.cs`
- `app/test/runtime/neuron/neuron_controller_test.dart`
- `app/test/runtime/neuron/neuron_transport_test.dart`
- `app/integration_test/neuron_mcp_ui_test.dart`

---

## Task 1: Establish the live baseline and preserve current work

**Interfaces:**

- Consumes: current AppHost, MCP, kernel, FeatureHost, Flutter resources and uncommitted MCP/feed changes.
- Produces: a reproducible baseline report and a safe starting point for the first vertical slice.

- [ ] **1. Capture the current workspace state.** Run `git status --short`, `git diff --stat`, and `git branch --show-current` from `E:\brain`; save the exact output in the execution log section at the bottom of this plan without staging or modifying unrelated files.
- [ ] **2. Confirm that the current checkout is intentionally used in place.** Record that a separate worktree would omit the existing uncommitted MCP session, feed renewal, probe, and bridge work required by the live proof; do not create a worktree unless those changes are first committed or migrated.
- [ ] **3. Refresh CodeGraph.** Run `codegraph sync E:\brain`, then `codegraph status E:\brain`; require an up-to-date index before changing contracts.
- [ ] **4. Verify Aspire prerequisites.** Run `aspire doctor --non-interactive`; require zero failed checks.
- [ ] **5. Capture the live resource table.** Run `aspire describe --format Table --include-hidden --non-interactive`; record health for `kernel-*`, `mcp`, `feature-host`, and `flutter-ui`.
- [ ] **6. Capture current MCP logs.** Run `aspire logs mcp --non-interactive` or the current CLI equivalent and record startup errors, endpoint URLs, and authentication failures without copying secrets into the plan.
- [ ] **7. Capture current kernel traces.** Run `aspire otel traces --non-interactive` scoped to the latest `ino` or surface activity; if the CLI requires a query, use `aspire otel traces --help` and record the exact supported command.
- [ ] **8. Measure the source baseline.** Count production and test lines for the groups listed in `EVERYTHING-IS-A-NEURON.md`; write the values to `artifacts/neuron-migration-baseline.json` only when implementation begins and keep the file untracked unless the repository already tracks metrics artifacts.
- [ ] **9. Run the focused existing tests.** Execute `dotnet test tests/DigitalBrain.UnitTests/DigitalBrain.UnitTests.csproj --logger "console;verbosity=minimal"` and record pass/fail counts.
- [ ] **10. Run the current Flutter runtime tests.** Execute `flutter test test/runtime` from `app`; record pass/fail counts and stop to diagnose any baseline failure before attributing it to new work.

## Task 2: Lock the universal contract vocabulary

**Interfaces:**

- Consumes: `NeuronId`, `NeuronScope`, `BrainOwnerId`, `ActorId`, and current runtime contracts.
- Produces: final names and invariants used by every later task.

```csharp
public readonly record struct NeuronAddress(
    BrainOwnerId OwnerId,
    string SpaceId,
    NeuronId NeuronId);
```

- [ ] **11. Query CodeGraph for identity consumers.** Run `codegraph impact NeuronId` and `codegraph impact NeuronScope`; list every production caller that constructs or parses current identities.
- [ ] **12. Decide the first-slice address mapping.** Define Chat as `OwnerId + actor/{ActorId}/chat + NeuronId("main")`, Ask UI as `OwnerId + actor/{ActorId}/ui + NeuronId("ask")`, and Activity UI as the same UI space with `NeuronId("activity")`.
- [ ] **13. Define public bounds.** Set `SpaceId` and `NeuronId.Value` maximum UTF-8 lengths using existing contract boundary conventions; use exact constants in `NeuronAddress`.
- [ ] **14. Define canonical key encoding.** Use length-prefixed UTF-8 components plus SHA-256, matching `RuntimeStateKeys` safety rather than delimiter-only concatenation.
- [ ] **15. Define transport independence.** Add a contract test asserting that UI and MCP `RequestContext` instances with the same Owner/Actor resolve the same Chat and Ask UI addresses despite different Session IDs and audiences.
- [ ] **16. Define actor-private versus owner-shared spaces.** Add exact helper names `NeuronSpaces.Actor(ActorId)` and `NeuronSpaces.Owner`; forbid ad hoc scope strings outside these helpers.
- [ ] **17. Define system Neurons.** Add `NeuronSpaces.System` for Login and infrastructure Neurons without pretending physical processes are product state.
- [ ] **18. Define address parsing policy.** Do not support arbitrary public string parsing in the first slice; construct addresses through typed factories and expose a canonical display string only for diagnostics and MCP input.
- [ ] **19. Define compatibility aliases.** Map existing conversation and surface keys to addresses through adapter factories without changing persisted keys in the first slice.
- [ ] **20. Commit the vocabulary slice.** Stage only the new/modified identity contract and its tests, then commit with `feat(runtime): define universal neuron identity`.

## Task 3: Implement `NeuronAddress`

**Files:**

- Create: `src/DigitalBrain.Kernel.Contracts/Core/NeuronAddress.cs`
- Test: `tests/DigitalBrain.UnitTests/NeuronAddressTests.cs`

- [ ] **21. Write failing equality tests.** Assert that identical Owner/space/Neuron values compare equal and that changing any component changes equality.
- [ ] **22. Write failing key-stability tests.** Assert exact lowercase 64-character SHA-256 output for a fixed address fixture.
- [ ] **23. Write failing ambiguity tests.** Assert that `("ab","c")` and `("a","bc")` components cannot generate the same key.
- [ ] **24. Write failing bound tests.** Assert whitespace, empty, oversized, or noncanonical component values throw `ArgumentException`.
- [ ] **25. Run the owning unit project.** Expect the new tests to fail because `NeuronAddress` does not exist.
- [ ] **26. Implement `NeuronAddress`.** Add Orleans serialization attributes, constructor validation, `ToGrainKey`, and `ToString` using the approved canonical format.
- [ ] **27. Implement `NeuronAddressKeys`.** Add a private length-prefixed binary writer and SHA-256 hashing with no source comments.
- [ ] **28. Implement `NeuronSpaces`.** Add typed factories for actor, owner, session, system, feature, connection, effect, and UI spaces.
- [ ] **29. Re-run the owning unit project.** Require all `NeuronAddressTests` and existing unit tests to pass.
- [ ] **30. Inspect compatibility impact.** Run `codegraph sync` and `codegraph impact NeuronAddress`; verify no dependency from Kernel Contracts to host or integration implementations.

## Task 4: Define the universal Neuron contracts

**Files:**

- Create: `src/DigitalBrain.Kernel.Contracts/Runtime/NeuronContracts.cs`
- Test: `tests/DigitalBrain.IntegrationContractTests/NeuronContractBoundaryTests.cs`

```csharp
[Alias("digitalbrain.neuron.v1")]
public interface INeuron : IGrainWithStringKey
{
    Task<NeuronDescription> DescribeAsync();
    Task<NeuronSnapshot> ReadAsync(NeuronRead request);
    Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation);
    Task<NeuronEventPage> ReadEventsAsync(NeuronEventCursor cursor);
}
```

- [ ] **31. Write a failing contract-boundary test.** Assert `INeuron` lives in Kernel Contracts, references Orleans abstractions only, and contains exactly four methods.
- [ ] **32. Write failing serializer tests.** Round-trip `NeuronDescription`, `NeuronRead`, `NeuronSnapshot`, `NeuronInvocation`, `NeuronReceipt`, `NeuronEventCursor`, and `NeuronEventPage` through Orleans serialization.
- [ ] **33. Define `NeuronLifecycle`.** Include only `Active`, `Paused`, `AwaitingDecision`, `Completed`, `Failed`, and `OutcomeUnknown`; do not copy every specialized status.
- [ ] **34. Define `NeuronDescription`.** Include address, kind, schema version, lifecycle, revision, execution profile, facet descriptors, and supported projections.
- [ ] **35. Define `NeuronInvocation`.** Include command ID, contract ID, JSON input, optional expected revision, correlation ID, causation reference, and occurrence time.
- [ ] **36. Define `NeuronReceipt`.** Include command ID, operation Neuron reference, target revision, disposition, safe result, and replay flag.
- [ ] **37. Define read contracts.** `NeuronRead` requests one named bounded projection; `NeuronSnapshot` returns address, revision, lifecycle, projection schema, and JSON payload.
- [ ] **38. Define event contracts.** Bound cursor page size, event payload size, and event-tail count using explicit constants.
- [ ] **39. Run contract tests.** Require serialization and architectural boundary tests to pass.
- [ ] **40. Commit the universal contract slice.** Commit only contract and test files with `feat(runtime): add universal neuron contract`.

## Task 5: Define Synapse contracts and policy inputs

**Files:**

- Create: `src/DigitalBrain.Kernel.Contracts/Runtime/SynapseContracts.cs`
- Test: `tests/DigitalBrain.UnitTests/NeuronPolicyEvaluatorTests.cs`

- [ ] **41. Write failing enum tests.** Assert the relation set is exactly `Contains`, `Requires`, `Grants`, `BackedBy`, `Projects`, `CausedBy`, `Awaits`, `Approves`, `EmitsTo`, and `UsesModule`.
- [ ] **42. Write failing identity tests.** Assert a Synapse source and target cannot be default addresses and its revision must be positive.
- [ ] **43. Write failing constraint-bound tests.** Reject constraints JSON beyond the approved UTF-8 size and reject invalid JSON.
- [ ] **44. Define `NeuronAuthority`.** Include issuing Owner, Actor, Session Neuron reference, decision ID, issued time, and evidence digest.
- [ ] **45. Define `SynapseRecord`.** Use Orleans aliases and stable field IDs from the design.
- [ ] **46. Define grant lookup input.** Add `NeuronPolicyRequest` with caller, target, contract ID, and the target document Synapses.
- [ ] **47. Define denial reasons.** Use a closed enum for missing grant, wrong owner, wrong actor, unhealthy backing connection, paused target, wrong revision, and invalid authority.
- [ ] **48. Implement pure policy evaluation.** Authorize owner-local deterministic reads explicitly and require matching `Grants` or system policy for privileged contracts.
- [ ] **49. Run unit tests.** Require all policy tests and existing unit tests to pass.
- [ ] **50. Commit the Synapse slice.** Commit with `feat(runtime): define neuron synapses and policy`.

## Task 6: Introduce the universal document and validator

**Files:**

- Create: `src/DigitalBrain.Kernel/Runtime/NeuronDocument.cs`
- Create: `src/DigitalBrain.Kernel/Runtime/NeuronDocumentValidator.cs`
- Test: `tests/DigitalBrain.UnitTests/NeuronDocumentValidatorTests.cs`

- [ ] **51. Write a failing valid-document test.** Build the smallest Active Chat document and expect validation success.
- [ ] **52. Write failing identity mismatch tests.** Reject a document whose address key differs from the activated grain key.
- [ ] **53. Write failing revision tests.** Reject negative revisions and a transition that advances by more than one.
- [ ] **54. Write failing kind/schema tests.** Reject blank kind, unknown schema version, and unregistered state schema.
- [ ] **55. Write failing event-tail tests.** Reject duplicate event IDs, descending sequences, oversized payloads, and events whose target address differs from the document.
- [ ] **56. Write failing Synapse tests.** Reject duplicate Synapse IDs and invalid source addresses.
- [ ] **57. Implement `NeuronDocument`.** Use the approved state envelope and immutable arrays.
- [ ] **58. Implement `NeuronDocumentValidator`.** Keep validation pure and deterministic so it can run before persistence and in migration tools.
- [ ] **59. Run owning tests.** Require all validator tests to pass.
- [ ] **60. Commit the document slice.** Commit with `feat(runtime): add validated neuron document`.

## Task 7: Generalize encrypted persistence

**Files:**

- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/EncryptedRuntimeStateContracts.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/EncryptedPersistentState.cs`
- Test: `tests/DigitalBrain.OrleansTests/Runtime/NeuronGrainTests.cs`

- [ ] **61. Write a failing aggregate-kind test.** Assert the encrypted protector accepts the universal `neuron` aggregate kind.
- [ ] **62. Write a failing legacy-kind test.** Assert existing Conversation, SurfaceFeed, Session, and Effect envelopes still open during migration.
- [ ] **63. Add `RuntimeStateKinds.Neuron`.** Do not remove legacy kinds in this phase.
- [ ] **64. Add `RuntimeStateSchemas.Neuron`.** Start at schema version 1.
- [ ] **65. Add a `neuronstate` storage provider name.** Wire the constant without changing AppHost yet.
- [ ] **66. Refactor protector kind validation.** Accept `Neuron` and preserve all legacy values.
- [ ] **67. Add universal document encryption tests.** Protect, unprotect, rewrap, and detect tampering.
- [ ] **68. Add persistence rollback tests.** Reuse `PersistedStateReconciliation` and assert outcome-unknown writes poison the activation.
- [ ] **69. Run the Orleans owning project.** Require existing encrypted-state tests and new Neuron tests to pass.
- [ ] **70. Commit persistence compatibility.** Commit with `feat(runtime): persist universal neuron documents`.

## Task 8: Build the facet registry

**Files:**

- Create: `src/DigitalBrain.Kernel/Runtime/NeuronFacetRegistry.cs`
- Create: `src/DigitalBrain.Kernel.Contracts/Runtime/NeuronFacetContracts.cs`
- Test: `tests/DigitalBrain.UnitTests/NeuronFacetRegistryTests.cs`

- [ ] **71. Write failing duplicate-contract tests.** Register two handlers with the same stable contract ID and require startup failure.
- [ ] **72. Write failing unknown-contract tests.** Resolve an absent contract and require a bounded `UnknownNeuronContract` result.
- [ ] **73. Define `INeuronFacet`.** Expose contract ID, input schema, output schema, operation kind, required grants, and execution profile.
- [ ] **74. Define generic handler context.** Include target address, caller context, current document, time provider, and a child-Neuron resolver.
- [ ] **75. Implement registry construction.** Materialize an ordinal dictionary and validate every descriptor at startup.
- [ ] **76. Implement input validation.** Validate the JSON payload against handler-specific bounded parsing before invoking the handler.
- [ ] **77. Implement output validation.** Require handlers to return a transition containing the next document, safe result, child Neuron requests, and projection requests.
- [ ] **78. Add registration extension methods.** Wire facets through `UseDigitalBrainOrleans` without referencing MCP or Flutter projects.
- [ ] **79. Run owning tests.** Require duplicate, unknown, input, output, and startup tests to pass.
- [ ] **80. Commit the facet registry.** Commit with `feat(runtime): add typed neuron facet registry`.

## Task 9: Implement the first universal grain

**Files:**

- Create: `src/DigitalBrain.Kernel/Runtime/NeuronGrain.cs`
- Test: `tests/DigitalBrain.OrleansTests/Runtime/NeuronGrainTests.cs`

- [ ] **81. Write a failing activation test.** Resolve `INeuron` by a valid address key and assert `DescribeAsync` returns the same address.
- [ ] **82. Write a failing initialization test.** Read an uninitialized known-kind Neuron and require its registered factory to produce revision zero state.
- [ ] **83. Write a failing unknown-kind test.** Resolve a key with no provisioning record and require a bounded unavailable result without state creation.
- [ ] **84. Write a failing command replay test.** Invoke the same command ID twice and require one state transition and a replay receipt.
- [ ] **85. Write a failing expected-revision test.** Invoke with a stale expected revision and require no state change.
- [ ] **86. Write a failing event cursor test.** Commit two events and read them in order after cursor zero.
- [ ] **87. Implement `NeuronGrain`.** Inject encrypted persistent state, registry, validator, policy evaluator, time provider, and projection coordinator.
- [ ] **88. Implement exact-one revision enforcement.** The grain rejects handler transitions that do not preserve or increment revision exactly once.
- [ ] **89. Run Orleans tests.** Require activation, initialization, replay, conflict, events, and existing runtime tests to pass.
- [ ] **90. Commit the grain slice.** Commit with `feat(runtime): add universal neuron grain`.

## Task 10: Represent Chat and UI destinations as explicit Neurons

**Files:**

- Modify: `src/DigitalBrain.Kernel.Contracts/Core/RuntimeContracts.cs`
- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/SurfaceFeedNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoConversationOutboxDispatcherGrain.cs`
- Test: `tests/DigitalBrain.UnitTests/SurfaceFeedActionAuthorityTests.cs`

- [ ] **91. Write a failing Chat address test.** Assert `RequestContext` resolves the same Chat address for UI and MCP sessions sharing Owner/Actor.
- [ ] **92. Write a failing Ask UI address test.** Assert the home/Ask surface carries the canonical Ask UI Neuron address.
- [ ] **93. Write a failing cause test.** Assert a projected operation surface identifies both the Chat/Operation causal Neuron and target UI Neuron.
- [ ] **94. Extend surface presentation contracts.** Add target UI Neuron address and causal Neuron reference using backward-compatible optional fields.
- [ ] **95. Update home-surface bootstrap.** Construct Ask UI and Chat addresses from Owner/Actor without using Session ID.
- [ ] **96. Update outbox projection.** Map Conversation operation events to the Ask UI Neuron and Activity UI Neuron.
- [ ] **97. Preserve action renewal fixes.** Keep consumed and expired bindings replaceable and clamp action-token expiry to the protocol maximum.
- [ ] **98. Run unit and Orleans tests.** Require SurfaceFeed action, conversation outbox, and compatibility tests to pass.
- [ ] **99. Inspect the surface payload manually.** Run the existing Dart probe and confirm address fields decode without breaking old clients.
- [ ] **100. Commit the first UI Neuron representation.** Commit with `feat(ui): represent ask and activity as neurons`.

## Task 11: Add generic Neuron MCP tools

**Files:**

- Create: `src/DigitalBrain.Mcp/NeuronMcpTools.cs`
- Create: `src/DigitalBrain.Mcp/NeuronRequestContextFactory.cs`
- Modify: `src/DigitalBrain.Mcp/Program.cs`
- Test: `tests/DigitalBrain.UnitTests/NeuronMcpToolTests.cs`

- [ ] **101. Write failing tool-schema tests.** Assert exactly `neuron_describe`, `neuron_read`, and `neuron_invoke` are registered by the new tool type.
- [ ] **102. Write failing authentication tests.** Require every tool to reject absent, UI-audience, expired, or wrong-owner credentials.
- [ ] **103. Define MCP address input.** Accept canonical Owner/space/Neuron components and forbid caller-supplied Owner values that differ from the authenticated Owner.
- [ ] **104. Implement `NeuronRequestContextFactory`.** Convert validated MCP session context into the universal caller context.
- [ ] **105. Implement `neuron_describe`.** Resolve `INeuron` using the authenticated address and return bounded JSON.
- [ ] **106. Implement `neuron_read`.** Pass projection name and bounded parameters through `NeuronRead`.
- [ ] **107. Implement `neuron_invoke`.** Pass contract, input, command ID, correlation ID, and optional expected revision through `NeuronInvocation`.
- [ ] **108. Register the new tool type.** Keep existing `McpTools` registered for compatibility.
- [ ] **109. Run MCP unit tests and targeted build.** Execute `dotnet build src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj --no-restore` followed by the owning unit project.
- [ ] **110. Commit generic MCP tools.** Commit with `feat(mcp): expose universal neuron tools`.

## Task 12: Route `ino_interact` through the Neuron contract

**Files:**

- Create: `src/DigitalBrain.Mcp/LegacyNeuronToolAliases.cs`
- Modify: `src/DigitalBrain.Mcp/McpTools.cs`
- Test: `tests/DigitalBrain.UnitTests/NeuronMcpToolTests.cs`

- [ ] **111. Write a failing alias-equivalence test.** Invoke `ino_interact` and `neuron_invoke` with equivalent Chat input and require identical target address and contract ID.
- [ ] **112. Define `digitalbrain.chat.interact.v1`.** Add the stable typed input and result contracts to Kernel Contracts.
- [ ] **113. Implement a Chat facet adapter.** Translate the universal invocation to the current durable INO operation path without duplicating workflow execution.
- [ ] **114. Replace `ino_interact` internals.** Make it construct a Chat Neuron invocation and delegate to the same service used by `neuron_invoke`.
- [ ] **115. Preserve command ID semantics.** Use the caller command ID as the universal idempotency key.
- [ ] **116. Preserve grants.** Require `brain.interact` through Neuron policy and current request authorization.
- [ ] **117. Preserve response shape temporarily.** Return the existing accepted-operation fields from the alias while adding the target Neuron address.
- [ ] **118. Run alias tests.** Require replay, wrong owner, missing grant, and valid invocation tests to pass.
- [ ] **119. Build MCP and Kernel.** Run targeted builds with `--no-restore`.
- [ ] **120. Commit the alias migration.** Commit with `refactor(mcp): route ino interact through neuron invocation`.

## Task 13: Create one generic Flutter Neuron model

**Files:**

- Create: `app/lib/runtime/neuron/neuron_address.dart`
- Create: `app/lib/runtime/neuron/neuron_projection.dart`
- Test: `app/test/runtime/neuron/neuron_controller_test.dart`

- [ ] **121. Write failing address parsing tests.** Decode valid Owner/space/Neuron JSON and reject blank, oversized, or mismatched fields.
- [ ] **122. Write failing projection tests.** Decode revision, view kind, data, actions, children, and cause.
- [ ] **123. Write failing action tests.** Require target address, contract ID, expected revision, input schema, and capability token.
- [ ] **124. Implement immutable Dart address types.** Add equality, hash code, and canonical diagnostic string.
- [ ] **125. Implement immutable projection types.** Keep JSON values bounded and defensive-copy collections.
- [ ] **126. Add compatibility decoding.** Convert current `SurfaceEnvelope` fields into a `NeuronProjection` when new fields are present.
- [ ] **127. Reject scope mismatch.** Ensure decoded UI Neuron Owner/Actor space matches the signed session identity.
- [ ] **128. Run Dart unit tests.** Execute `flutter test test/runtime/neuron`.
- [ ] **129. Run existing surface protocol tests.** Ensure compatibility decoding did not break current payloads.
- [ ] **130. Commit Flutter Neuron models.** Commit with `feat(ui): add generic neuron projection model`.

## Task 14: Build the Flutter Neuron controller and transport adapter

**Files:**

- Create: `app/lib/runtime/neuron/neuron_transport.dart`
- Create: `app/lib/runtime/neuron/neuron_controller.dart`
- Modify: `app/lib/runtime/feed_state.dart`
- Test: `app/test/runtime/neuron/neuron_transport_test.dart`

- [ ] **131. Write a failing revision-order test.** Accept a newer projection and reject duplicate or stale revisions for the same address.
- [ ] **132. Write a failing feed-gap test.** Require controller reset when global feed sequence skips.
- [ ] **133. Write a failing identity-change test.** Clear all UI Neurons when Owner or Actor changes; do not clear merely because a token refreshes the same session.
- [ ] **134. Define `NeuronTransport`.** Add read, invoke, watch, acknowledge, and close operations without product-specific methods.
- [ ] **135. Implement `SurfaceFeedNeuronTransportAdapter`.** Convert current gRPC feed events into generic projections.
- [ ] **136. Implement `NeuronController`.** Store projections by `NeuronAddress` and expose address-based selectors.
- [ ] **137. Adapt `FeedController`.** Delegate projection storage to `NeuronController` while retaining compatibility getters during migration.
- [ ] **138. Run new and existing runtime tests.** Require both Neuron and legacy controller suites to pass.
- [ ] **139. Run `dart analyze`.** Require zero new analyzer warnings.
- [ ] **140. Commit controller migration.** Commit with `refactor(ui): store surfaces as ui neurons`.

## Task 15: Prove the first MCP-to-UI vertical slice

**Files:**

- Preserve: `src/DigitalBrain.Mcp/McpDevSessionEndpoint.cs`
- Preserve: `tools/DigitalBrain.AgentMcp/`
- Preserve or replace: `app/tool/probe_send.dart`
- Create: `tests/DigitalBrain.E2ETests/NeuronMcpUiE2ETests.cs`

- [ ] **141. Add endpoint security tests.** Assert `/dev/mcp-session` exists only in Development/Test and rejects invalid credentials.
- [ ] **142. Build the MCP bridge.** Run `dotnet build tools/DigitalBrain.AgentMcp/DigitalBrain.AgentMcp.csproj`.
- [ ] **143. Build the MCP host.** Run `dotnet build src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj --no-restore`.
- [ ] **144. Rebuild only MCP.** Run `aspire resource mcp rebuild --non-interactive`, then `aspire wait mcp --non-interactive`.
- [ ] **145. Verify the UI feed before invocation.** Run the Dart probe, record Ask UI Neuron revision, SurfaceFeed sequence, action binding, Owner, and Actor.
- [ ] **146. Create an MCP session.** Use the development endpoint and verify its Owner/Actor match the Flutter session while audience and Session Neuron differ.
- [ ] **147. List DigitalBrain MCP tools.** Use the ModelContextProtocol 1.4.0 client and assert `ino_interact` plus the new Neuron tools are present.
- [ ] **148. Invoke Chat through MCP.** Call `neuron_invoke` when available, otherwise the migrated `ino_interact`, with a unique command ID and a prompt containing a unique probe marker.
- [ ] **149. Observe the UI Neuron change.** Keep the Flutter feed watcher active and require a later Ask or Activity UI Neuron revision whose cause references the accepted MCP operation.
- [ ] **150. Commit the vertical-slice proof.** Add the deterministic E2E test and commit with `test(e2e): prove mcp updates ui neurons`.

## Task 16: Prove the result visually with Computer Use

**Interfaces:**

- Consumes: running Flutter desktop window, MCP invocation marker, UI Neuron projection.
- Produces: visual confirmation that the rendered Flutter UI reflects the MCP-caused Neuron revision.

- [ ] **151. Connect through the supported Computer Use runtime.** Use the plugin’s `node_repl` `js` execution tool, bootstrap `computer-use-client.mjs`, and call `sky.list_apps()`.
- [ ] **152. Select the Flutter app window.** Choose the exact `flutter-ui` window returned by `list_apps`; never guess process IDs or automate a terminal.
- [ ] **153. Capture the pre-invocation window state.** Request a screenshot and, when useful, filtered accessibility text containing Ask, Activity, the current transcript, and the probe marker.
- [ ] **154. Invoke DigitalBrain MCP outside Computer Use.** Use the MCP client tool or test harness, not UI automation, to submit the unique marker.
- [ ] **155. Wait on the application state.** Use Aspire wait/log/trace mechanisms and the feed watcher; do not use arbitrary sleeps longer than the retry cadence.
- [ ] **156. Capture the post-invocation Flutter state.** Rehydrate the same window and verify the marker or corresponding operation state appears.
- [ ] **157. Verify address causality.** Correlate the visible UI change with the Ask or Activity UI Neuron address and the MCP operation ID from logs/feed evidence.
- [ ] **158. Verify no manual UI action caused the update.** The only Flutter interaction permitted is navigation or observation; do not submit the chat through Flutter.
- [ ] **159. Save bounded proof metadata.** Record timestamp, command ID, operation ID, target Chat Neuron, target UI Neuron, pre/post revisions, and feed sequences without secrets.
- [ ] **160. Mark the first milestone complete.** Check items 141–159 only after automated and visual evidence agree.

## Task 17: Move Session and Login onto universal contracts

**Files:**

- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/SessionNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/SessionNeuron.cs`
- Modify: `src/DigitalBrain.Mcp/RuntimeSessionAuthority.cs`
- Test: `tests/DigitalBrain.OrleansTests/Runtime/SessionNeuronTests.cs`

- [ ] **161. Define Login and Session addresses.** System Login Neuron is stable; each issued session is its own Session Neuron.
- [ ] **162. Add universal descriptions.** Session describes audience, assurance, grants, lifecycle, and expiry projection without returning token hashes.
- [ ] **163. Add universal session contracts.** Map rotate and revoke to stable contract IDs.
- [ ] **164. Route session authority through an adapter.** Preserve refresh replay detection and revocation semantics.
- [ ] **165. Add two-audience identity tests.** UI and MCP sessions differ but resolve identical product Neuron addresses.
- [ ] **166. Add Login UI projection.** Model the login destination as a UI Neuron with an Authenticate action targeting the Login Neuron.
- [ ] **167. Keep authentication secrets out of projection state.** Assert passwords, refresh tokens, and token hashes never appear in Neuron events or UI data.
- [ ] **168. Run Session and UI tests.** Require rotation, replay, revoke, projection, and audience tests to pass.
- [ ] **169. Run a targeted MCP/UI smoke test.** Ensure the previous Chat proof still succeeds after session adaptation.
- [ ] **170. Commit Session/Login migration.** Commit with `refactor(auth): model login and sessions as neurons`.

## Task 18: Move Capability and Connection behavior onto Neurons

**Files:**

- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/CapabilityDiscovery.cs`
- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/IConnector.cs`
- Modify: `src/DigitalBrain.Kernel/Capabilities/OwnerCapabilityCatalog.cs`
- Modify: `src/DigitalBrain.Kernel/Capabilities/OwnerConnectionCatalog.cs`

- [ ] **171. Define Capability Neuron addresses.** Use stable provider/domain IDs such as `gmail` and `salesforce`.
- [ ] **172. Define Connection Neuron addresses.** Use Owner-scoped provider connection IDs.
- [ ] **173. Convert descriptors to Neuron descriptions.** Preserve name, examples, grants, required connection, operation kind, and availability.
- [ ] **174. Represent healthy backing with `BackedBy` Synapses.** Remove duplicated healthy-connection sets from the universal projection.
- [ ] **175. Represent Feature access with `Requires` and `Grants` Synapses.** Preserve version and constraints in Synapse metadata.
- [ ] **176. Add connection-health projection tests.** Revoking or expiring OAuth must make the Capability Neuron unavailable on the next read.
- [ ] **177. Add Gmail typed facet adapters.** Route current Gmail handlers through stable Neuron contracts.
- [ ] **178. Add Salesforce typed facet adapters.** Route current Salesforce handlers through stable Neuron contracts.
- [ ] **179. Run capability and connector test projects.** Require all security, health, discovery, and invocation tests to pass.
- [ ] **180. Commit capability/connection migration.** Commit with `refactor(capabilities): model capabilities and connections as neurons`.

## Task 19: Move Effect, Approval, Feature, and work execution

**Files:**

- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/InoEffectPlan.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoEffectPlanNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Features/`
- Modify: `hosts/DigitalBrain.FeatureHost/`

- [ ] **181. Define Effect and Approval addresses.** Use immutable Effect IDs and decision IDs under Owner scope.
- [ ] **182. Add `ApprovedEffectProof`.** Require exact Effect address, revision, payload digest, decision ID, execution fence, and approval time.
- [ ] **183. Remove raw connector mutation entry points.** Replace them with proof-requiring overloads after tests cover every caller.
- [ ] **184. Extract `WorkFacet`.** Move shared inbox, lease, fence, retry, completion, pause, and outcome-unknown behavior from Feature and INO implementations.
- [ ] **185. Adapt Conversation operations to `WorkFacet`.** Preserve current reminders, timers, and durable completion.
- [ ] **186. Adapt Feature installations to `WorkFacet`.** Preserve release switching, grant revisions, publication fencing, and rollback.
- [ ] **187. Model Feature release and installation relations.** Use `UsesModule`, `Requires`, and `Grants` Synapses.
- [ ] **188. Convert FeatureHost to a generic module worker.** Claim work by Neuron reference and return transitions/effects through typed contracts.
- [ ] **189. Run Feature, Effect, and E2E tests.** Require exact replay, approval, decline, rollback, and provider verification behavior.
- [ ] **190. Commit the work/effect migration.** Commit with `refactor(runtime): unify feature operation and effect neurons`.

## Task 20: Delete residue, verify the whole system, and report

**Files:**

- Delete after migration: `app/lib/rfw_host/`
- Delete after migration: RFW payload branches in `app/lib/runtime/protocol/surface_protocol.dart`
- Delete after migration: obsolete MCP aliases and Feature-specific UI RPCs
- Update: `README.md`
- Update: `EVERYTHING-IS-A-NEURON.md`

- [ ] **191. Delete RFW production code and tests.** Remove imports, dependencies, payload variants, rendering branches, and package references only after native UI Neuron tests cover retained views.
- [ ] **192. Delete obsolete MCP tools.** Remove `feature_*` aliases after callers use universal Neuron tools and compatibility telemetry shows no remaining dependency.
- [ ] **193. Delete Feature-specific UI RPCs and protobuf messages.** Regenerate Dart output and verify the generic Neuron transport replaces every removed call.
- [ ] **194. Delete specialized catalogs and projection DTOs.** Keep only adapters still required for persisted-state migration.
- [ ] **195. Delete specialized grain interfaces.** Remove Session, Conversation, SurfaceFeed, Effect, Feature, and catalog interfaces only when all callers use `INeuron` or documented temporary migration adapters.
- [ ] **196. Measure deletion.** Re-run the baseline line-count script and calculate percentage reduction for production, transport/orchestration, generated code, and tests.
- [ ] **197. Run full static verification.** Execute `dotnet build --no-restore`, `dart analyze` from `app`, and any repository formatting checks.
- [ ] **198. Run the exact root test gate.** Execute `dotnet test --logger "console;verbosity=minimal"` and require zero failures and zero skips; run the full Flutter test suite and integration test.
- [ ] **199. Run final live proof.** Use DigitalBrain MCP to invoke the Chat Neuron, confirm kernel and feed traces, and use Computer Use to verify the Flutter UI Neuron visibly updates.
- [ ] **200. Complete the migration report.** Update `EVERYTHING-IS-A-NEURON.md` with implemented decisions, deleted concepts, measured reductions, remaining adapters, exact test results, and the final MCP operation/UI Neuron evidence.

## Execution Log

Add dated entries here while executing. Each entry must include:

- plan item numbers completed
- files changed
- focused tests run and exact results
- Aspire resource actions
- CodeGraph impact checks
- additions/deletions
- blockers or changed assumptions
- next item number
