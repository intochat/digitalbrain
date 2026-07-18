# BDD test assessment — ino behaviors as testable memory

## Current state

### .feature files (3 total)

| Feature | Location | Scenarios | Step class | Runner |
|---|---|---|---|---|
| Runtime neuron lifecycle | `features/ino-new/InoNew.Tests/Features/Neuron.feature` | 3 | `NeuronSteps.cs` | `NeuronScenarioTests.cs` |
| Behavior memory + vector search | `features/ino-new/InoNew.Tests/Features/BehaviorMemory.feature` | 3 | (inline in BehaviorMemoryGrainTests) | same |
| Shell neuron | `features/timetravel/Timetravel.Tests/Features/ShellNeuron.feature` | 2 | `ShellNeuronSteps.cs` | `ShellNeuronScenarioTests.cs` |

### Test infrastructure

- **NeuronBddContext** (`src/Testing/NeuronBddHooks.cs`) — reusable per-scenario context. Boots a TestCluster, holds a `PromptMatchingMockChatClient` for mocked LLM responses, a `Scenario` dictionary for step-to-step state passing.
- **PromptMatchingMockChatClient** — mocks the LLM with prompt-matcher rules (`When(predicate, response)`). Tracks call counts and received prompts for assertions.
- **Pattern:** `NeuronScenarioTests` runs Background steps then Scenario steps in one `[Fact]` per scenario. This is the xunit.v3 compromise for the Reqnroll.xUnit incompatibility (CLAUDE.md known-problem #8).

### What's covered

**Neuron.feature (3 scenarios):**
1. Create neuron → registry lists it → timeline records NeuronActivated
2. Connect two neurons → registry lists synapse → timeline records SynapseFired(connect)
3. Fire synapse → receipt has valid sequence → timeline records SynapseFired(verb)

**BehaviorMemory.feature (3 scenarios):**
1. Ingest example → count = 1 → search returns top hit
2. Ingest .feature file → count = #scenarios → search returns relevant scenario
3. Ingest same example twice → count = 1 (upsert)

**ShellNeuron.feature (2 scenarios):**
1. execute valid command → exit code 0 → timeline records SynapseFired(execute)
2. execute invalid command → exit code non-zero → timeline records Error or SynapseFired

## Gaps — behaviors NOT yet covered by BDD scenarios

### Missing .feature files

| Behavior | Why it matters | Priority |
|---|---|---|
| **Dispatcher command shell** | The InoCommandDispatcher is the central command surface for terminal + Telegram + mini app. Each verb is a behavior contract. | HIGH |
| **Decay lifecycle** | Consolidation (hot→cold→soft-deleted), access-boost on retrieval, importance ranker. These are the memory model's core invariants. | HIGH |
| **Timeline scrubbing** | GetStateAtAsync at past sequence reflects historical state — this is Time Travel's core promise. | MEDIUM |
| **Multi-fire sequence** | Firing the same synapse N times produces N distinct timeline events with distinct sequence numbers. | MEDIUM |
| **Behavior memory → neuron integration** | A neuron queries behavior memory during execution to retrieve similar examples. NOT implemented yet, but the .feature file should exist as the contract BEFORE the code. | LOW (blocked on #3) |

### Missing scenarios in EXISTING .feature files

| Feature | Missing scenario |
|---|---|
| Neuron.feature | Create neuron with duplicate id → error |
| Neuron.feature | Create neuron with blank name → error |
| Neuron.feature | Fire synapse from non-activated neuron → error |
| Neuron.feature | ListNeurons returns creation order |
| Neuron.feature | ListSynapses filtered by neuron id |
| BehaviorMemory.feature | Search with no results → empty hits |
| BehaviorMemory.feature | MCP behavior_memory_search returns ingested examples |
| ShellNeuron.feature | execute with timeout → timeout error |

## The vision: behaviors as searchable memory

The user's core insight is powerful: **BDD scenarios ARE ino's behavior memory.** Every `.feature` file is simultaneously:

1. **A test contract** — the scenario must pass for the system to be correct
2. **A behavior example** — the scenario is ingested into `IBehaviorMemory` as a vector-searchable artifact
3. **An LLM constraint** — when a neuron encounters a situation similar to a scenario, the retrieved example constrains its response toward the codified behavior

This creates a virtuous loop:
- Write a `.feature` file describing how ino should behave
- Tests enforce the behavior at build time
- The same scenarios are ingested at runtime and shape LLM decisions via vector search
- Consistency between test-time and runtime behavior is structural, not accidental

### What's already wired

- `FeatureFileIngestor.ParseScenarios(path, text)` parses `.feature` files into `BehaviorExample` records
- `IBehaviorMemory.IngestFeatureFileAsync(path)` ingests all scenarios from a file
- `IBehaviorMemory.SearchAsync(query, top)` retrieves top-k similar examples
- Tests verify the ingest→search round-trip with the `DeterministicEmbeddingGenerator`
- `BehaviorMemoryTools` MCP surface exposes ingest + search to external LLMs

### What's NOT wired yet

1. **Auto-ingest at silo startup.** No hosted service scans `features/**//*.feature` and ingests on boot. Each scenario is only available in memory if explicitly ingested.
2. **Neuron-execution-time retrieval.** `NeuronGrain.FireAsync` does NOT call `IBehaviorMemory.SearchAsync`. The behavior memory is standalone — it doesn't shape execution yet.
3. **Scenario metadata on the BehaviorExample.** The current parser extracts title + body but not tags, data tables, or the feature name. Richer metadata would improve retrieval quality.

## Recommended next .feature files

### Dispatcher.feature (NEW — highest priority)

```gherkin
Feature: ino command shell
  The InoCommandDispatcher is the universal command surface for ino.
  Every verb documented in its Usage string is a behavior contract.

  Background:
    Given a running test cluster with timeline capture enabled
    And the dispatcher is connected to the cluster

  Scenario: create verb creates a neuron and reports its id
    When I execute "create alpha"
    Then the output contains "created neuron alpha"
    And the registry lists 1 neuron

  Scenario: fire verb auto-connects and fires
    Given I have executed "create src; create dst"
    When I execute "fire src dst ping hello"
    Then the output contains "fired synapse"
    And the output contains "timeline seq="

  Scenario: timeline verb renders events as plain text
    Given I have executed "create alpha; create beta; fire alpha beta greet"
    When I execute "timeline"
    Then the output contains "Timetravel - ino"
    And the output contains "alpha"
    And the output contains "greet"

  Scenario: unknown verb prints a hint
    When I execute "nonsense alpha"
    Then the output contains "unknown command"

  Scenario: empty script produces no error
    When I execute "   ;  ;  "
    Then the output does not contain "[error]"
```

### Decay.feature (NEW)

```gherkin
Feature: Synapse decay lifecycle
  The decay model is biologically-inspired forgetting. Events age over time,
  access boosts them back, and consolidation passes move untouched events
  toward soft-deletion.

  Background:
    Given a running test cluster with timeline capture enabled

  Scenario: Consolidation ages hot events to cold after 1 day
    Given a hot event was appended yesterday
    When I run the consolidation pass
    Then the event's decay is cold (30)

  Scenario: Consolidation ages cold events to soft-deleted after 14 days
    Given a cold event was appended 3 weeks ago
    When I run the consolidation pass
    Then the event's decay is soft-deleted (1)
    And the event is hidden from default search

  Scenario: Access boost lifts cold events back to warm
    Given a cold event exists in the timeline
    When I query events in that range with default search floor
    Then the returned event's decay is warm (50)

  Scenario: Deep audit search does not resurrect soft-deleted events
    Given a soft-deleted event exists in the timeline
    When I query with minDecay=1 (audit mode)
    Then the event is returned with decay still at 1
```

### BehaviorMemoryIntegration.feature (NEW — blocked on neuron integration)

```gherkin
Feature: Behavior memory shapes neuron execution
  When a neuron fires, it retrieves similar behavior examples from memory.
  The retrieved examples constrain the LLM's response toward codified behavior.

  Background:
    Given a running test cluster with behavior memory and timeline capture
    And the behavior memory contains ingested scenarios from Neuron.feature

  Scenario: Firing a neuron retrieves relevant behavior examples
    Given a neuron named "planner" exists
    When the planner neuron fires with verb "plan_task"
    Then the behavior memory was queried for "plan_task"
    And the top hit's title mentions a known scenario

  Scenario: Retrieved examples are included in the LLM prompt
    Given a neuron named "planner" exists
    And the mock LLM is configured to echo its system prompt
    When the planner neuron fires with verb "plan_task"
    Then the LLM's received prompt contains a behavior example body
```

## Implementation plan

### Phase 1: Dispatcher.feature (covers the command shell — all surfaces)
1. Create `features/ino-new/InoNew.Tests/Features/Dispatcher.feature`
2. Create `Steps/DispatcherSteps.cs` + `Steps/DispatcherScenarioTests.cs`
3. Each scenario is a [Fact] that runs Background + Scenario steps
4. The existing `InoCommandDispatcherTests` become redundant once BDD scenarios cover the same verbs — merge or keep as complementary

### Phase 2: Decay.feature (covers the memory model invariants)
1. Create `features/timetravel/Timetravel.Tests/Features/Decay.feature`
2. Steps call `ConsolidateAsync` with synthetic timestamps and assert on returned decay values
3. Access-boost test already exists as a unit test; the BDD scenario wraps it in the contract language

### Phase 3: Auto-ingest hosted service
1. `InoNewHostedService` in `InoNew.Core`: scans `features/**//*.feature` at startup, ingests each into `IBehaviorMemory("global")`
2. Wire into Agents.Host and TestCluster via `AddInoNew()`
3. After this: every `.feature` file in the repo is simultaneously a test contract and a runtime behavior constraint

### Phase 4: Neuron-execution-time retrieval
1. Add `IBehaviorMemory` to `NeuronGrain` constructor
2. `FireAsync` calls `SearchAsync(synapse.Verb)` before executing
3. Top-k examples appended to the neuron's prompt context
4. BehaviorMemoryIntegration.feature scenarios become testable

## Test count summary

| Category | Current | After Phase 1 | After Phase 2 |
|---|---|---|---|
| .feature scenarios | 8 | 13 (+5 Dispatcher) | 17 (+4 Decay) |
| xunit [Fact]s | 94 | ~99 | ~103 |
| BDD step classes | 3 | 4 | 5 |
