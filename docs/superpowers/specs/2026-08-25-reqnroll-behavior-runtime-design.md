# Reqnroll Behavior Runtime Design

**Status:** Approved for autonomous implementation on 2026-08-25
**Date:** 2026-08-25
**Supersedes:** The Smart Prompt natural-language document and fixed capability-grant runner where they conflict

## 1. Decision

DigitalBrain Behaviors are executable Gherkin feature files. Users author ordinary `Feature`, `Scenario`, `Given`, `When`, `Then`, `And`, tags, doc strings, and tables. Hand-written C# step libraries carry Reqnroll `[Binding]`, `[Given]`, `[When]`, and `[Then]` attributes. A deterministic runtime parses and binds those steps today; an assistant may generate feature text, but it may not invent executable logic.

Each feature contains two kinds of scenarios:

- `@behavior` scenarios define installed reactive automations.
- `@test` scenarios inject fake inputs and assert observable outcomes.

The source text is the program, the executable specification, and the user-visible artifact. Generated C# is never required to run a feature in this release.

## 2. Product example

```gherkin
Feature: Bitcoin tracker

  @behavior
  Scenario: Track Elon posts about Bitcoin
    Given X.Account("elonmusk")
    When a new X.Post is published
    And the post mentions "bitcoin"
    Then analyze the post as "bitcoin market signal" with Gemma
    And add UI.Chart.Point to UI.Chart("bitcoin_tracker")

  @test
  Scenario: An Elon Bitcoin post adds a linked chart point
    Given fake X.Post from "elonmusk" with text "Bitcoin reaches 95000" and value 95000
    When behavior "Track Elon posts about Bitcoin" runs
    Then UI.Chart("bitcoin_tracker") has point 95000 linking to the source
```

## 3. Runtime model

Saving a feature creates an immutable content-addressed revision. Compilation performs:

1. Parse using the same Gherkin parser used by Reqnroll.
2. Discover hand-written methods annotated with Reqnroll binding attributes.
3. Bind every step uniquely; missing and ambiguous steps are diagnostics.
4. Validate the reactive shape: one trigger-bearing `When`, optional filters, one or more actions.
5. Produce an immutable `BehaviorPlan` containing typed trigger keys and bound step calls.
6. Run all `@test` scenarios against isolated fakes.
7. Activate the revision only when compilation and tests are green.

The runtime deliberately does not keep a Reqnroll test runner blocked while waiting for an external event. It preserves Reqnroll syntax and binding semantics, then compiles a reactive scenario into a durable Orleans subscription. This is the necessary semantic bridge between BDD tests and a multi-tenant event runtime.

## 4. Orleans topology and 5,000-user fan-out

External providers have one shared ingress per DigitalBrain deployment and credential/project, not one connection per user.

```text
X filtered stream/webhook
  -> provider adapter (verify, normalize, acknowledge)
  -> EventInbox Neuron keyed by provider event id (durable dedup)
  -> TriggerDirectory Neuron keyed by canonical trigger key
  -> SubscriptionPartition Neurons (fixed shards)
  -> BehaviorRunner Neuron per owner + behavior revision
  -> typed actions (Chart Entity, Chat Neuron, AI reasoner)
```

`TriggerDirectory` knows only non-empty partition ids. Each partition owns a bounded set of owner/revision subscriptions and fans out grain calls in bounded batches. Five thousand users observing `x.post/account:elonmusk` therefore share one upstream rule and connection while retaining owner isolation downstream.

Orleans `BroadcastChannel` remains appropriate only for lossy live UI/activation notices. It is not used for Behavior triggers because it is best-effort and has no replay. Implicit stream subscriptions are appropriate at the fixed connector-to-router boundary when a persistent production stream provider is added; they are not used to create one implicit subscriber per user. The first release uses a journaled typed grain call into the inbox, which has deterministic TestCluster behavior and the same downstream routing topology.

Delivery is at least once. Event id plus behavior revision id is the idempotency key. A duplicate provider event cannot append a second chart point or repeat a chat action.

## 5. Step libraries

Step libraries are trusted module code. Each binding declares:

- a Reqnroll expression;
- whether it contributes setup, trigger, filter, action, fake input, invocation, or assertion;
- typed argument conversion;
- an executor operating only through `BehaviorExecutionContext` capabilities.

The initial catalog supports X posts, generic email, calendar, market price, file, health metric, GitHub issue, and geofence inputs; string/numeric filters; Gemma analysis; chart append; and chat notification. Generated step code is explicitly outside this release. Later generated libraries must be separately compiled, attested, sandboxed, and installed as modules.

## 6. Eight installed examples

1. X post -> filter Bitcoin -> Gemma market-context analysis -> linked chart point.
2. Urgent email -> classify content with Gemma -> chat notification.
3. Calendar event -> detect travel preparation -> Gemma checklist -> chat notification.
4. Market price -> threshold filter -> append portfolio chart point.
5. File arrival -> Gemma summary -> chat notification.
6. Health metric -> threshold/anomaly explanation -> health chart point.
7. GitHub issue -> Gemma triage -> chat notification.
8. Geofence entry -> context-aware reminder -> chat notification.

Every example contains a paired deterministic `@test` scenario. Fakes never require network access. AI steps use a deterministic test reasoner in TestCluster and the real configured `IGemma4` client in Aspire.

## 7. AI responsibilities

`BehaviorReasoner` is explicitly keyed to `IGemma4`; behavior reasoning does not silently use a cloud default. `IEmbeddingGemma` remains configured for semantic memory and catalog discovery. The assistant receives two tools:

- `generate_behavior_feature`: ask Gemma to emit a feature constrained by the current step catalog, validate it, and return diagnostics/source.
- `run_behavior_example`: publish one of the eight fake events through the real ingress and report the run.

The system prompt describes the Gherkin contract. Generated text is always compiled and tested before activation. Tool output cannot bypass validation.

## 8. HTTP and Flutter Behaviors IDE

Owner-scoped endpoints provide list, read, save/compile/test/activate, fake run, step catalog, and AI feature generation. The Flutter Behaviors tab becomes a functional IDE:

- feature list and eight examples;
- multiline Gherkin editor with keyword/string/tag syntax highlighting;
- completion suggestions sourced from the backend step catalog;
- compile/test diagnostics with line information;
- Run fake and Save/activate actions;
- last-run output.

The Chat composer shows actionable hint chips for generating a behavior, running the X demo, and running all examples. Selecting a hint sends the exact request through the existing assistant path.

## 9. Security and tenancy

- Owner identity is taken from the verified HTTP/grain actor, never from feature text or model tool arguments.
- Trigger payloads are normalized to an allow-listed field map before delivery.
- A behavior receives only the trigger view declared by its bound steps.
- Actions resolve owner-scoped grain ids; a behavior cannot write another owner's chart or chat.
- Feature size, scenario count, step count, fan-out batch size, and AI token budget are bounded.
- Unknown/ambiguous steps and unsupported Gherkin constructs fail closed.

## 10. Testing and observability

- Compiler tests prove parsing, unique binding, diagnostics, tags, and all eight features.
- Runtime tests prove subscription activation, shared trigger fan-out, owner isolation, deduplication, filters, actions, and test execution.
- A 5,000-subscription simulation proves one ingress event, bounded partitions, and exactly one delivery per revision.
- AI tests prove `IGemma4` is selected and generated output is rejected unless it compiles and tests green.
- HTTP tests prove owner-scoped CRUD/run/generate contracts.
- Flutter widget tests prove editor, highlighting, completions, diagnostics, fake-run controls, and chat hints.
- Aspire/Computer Use verifies the real Windows Flutter app, local Gemma reasoning, feature generation, fake X delivery, and the linked chart result.

OTel spans cover compile, test, activate, ingress, route, run, step, reason, and action. Logs include behavior/revision/event/correlation identifiers but not secrets or unredacted provider payloads.

## 11. Migration

Existing `SmartPromptDocument.BodyText` data remains readable, but the default catalog and Behaviors UI switch to feature source. Old fixed grant mapping is not extended. New code uses Behavior vocabulary; Smart Prompt contracts can remain temporarily for binary/storage compatibility until a later migration removes them.
