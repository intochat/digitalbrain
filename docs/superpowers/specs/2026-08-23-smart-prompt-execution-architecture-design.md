# Smart Prompt & Execution Architecture

**Status:** Ratified in brainstorm 2026-08-23; written-spec review pending  
**Date:** 2026-08-23  
**Supersedes:** [2026-08-22-type-safe-behavior-event-architecture-design.md](./2026-08-22-type-safe-behavior-event-architecture-design.md) (Behavior naming and hop-DTO Effect-chain model), and the Smart Prompt section of [ARCHITECTURE.md](../../ARCHITECTURE.md) where they conflict  
**Inspiration:** [IAW PR #35](https://github.com/InteractiveAgents/IAW/pull/35) (TaskLedger, approvals, context providers, explainability) — adapted to DigitalBrain’s typed Synapse/Neuron/Entity model, not copied as stringly Core goo

## 1. Decision summary

DigitalBrain is a full intelligent assistant harness from day one: multi-chat, Smart Prompts (English + binding chips), hybrid agent/script execution, preferences, context providers, multi-agent teams, explainability, and Flutter dynamic UI — proven first with **Aspire fakes** and local Gemma, with real Gmail/Salesforce MCP swapped in later behind the same ports.

### Packaging (Approach A)

| Layer | Owns |
|---|---|
| `DigitalBrain.Abstractions` | Concepts: Synapse, EntityRef, ExecutionId, ContextPath/Patch, contract vocabulary |
| `DigitalBrain.Core` | Neuron membrane, Entity base, thin FactLedger primitive |
| `DigitalBrain.Modules.Execution` | **Execution Neuron** + **ExecutionContext Entity** + Effect broker + policy/approvals + hybrid drivers + `Execution.Sdk` |
| `DigitalBrain.Modules.SmartPrompt` | NL+chips documents, revisions, triggers, learning evidence, script codegen requests |
| `DigitalBrain.Scripting` | Out-of-process build/run of generated C# (never loaded in Kernel) |
| Façade modules (Gmail, Salesforce, Search, …) | Chips, Account Entities, Capability verbs, `I*Transport` (Fake now / MCP later) → `ContextDelta` |
| UI / AI / Time | Chat, kit Entities, agents, schedules, context providers |
| `DigitalBrain.Kernel` | Auth, HTTP/SSE, composition only |

### Product vocabulary

- **Smart Prompt** — user-facing name (English + chips). Prefer this over “Behavior” in UI and product docs.
- **Execution** — generic Run aggregate (chat turns and Smart Prompts are workloads).
- **ExecutionContext** — durable per-Execution working memory Entity.
- **Neuron** — aggregate (Synapses). **Entity** — richer persistence/query (no Synapses).

## 2. Goals

- One Execution spine for chat user prompts and Smart Prompts.
- Multi-chat (ChatGPT-style) and in-chat context switching via `ActiveExecutionId`.
- Operation-specific Context that tools populate/modify — **no hand-written hop DTOs** mirroring MCP schemas.
- Allow-listed typed Capability portals for assistant and scripts.
- Preferences + `IExecutionContextProvider` pipeline (IAW-style).
- Explainability with evidence refs (chat message, Execution, Context paths, Entities, preferences).
- Script codegen + multi-agent teams on the same Sdk.
- Aspire fakes flag: real Orleans/Execution/Gemma/Flutter/traces; fake integration payloads.
- Simple modules, thoroughly tested public surfaces; Aspire-observable.

## 3. Non-goals (for the first integration cut only)

- Real Gmail/Salesforce MCP transports (ports and fakes ship; live MCP later).
- Event-sourcing Chart/Form Entities.
- Treating traffic journals as authoritative Run history.
- Loading generated C# inside Kernel/silo.
- Global shared Context across chats.

## 4. Invariants

1. **Neuron = aggregate; Entity = persistence/query.** Entities may expose parameterized `Query` and validated writes from trusted callers; they do not receive Synapses.
2. **One ExecutionContext Entity per Execution Neuron.** Never a process-global bag.
3. **Chat × N per owner;** each Chat holds `ActiveExecutionId?` and related Execution ids. Switching focus rebinds assistant tools to that Context.
4. **Follow-up messages start a new Execution**, seeded with admitted lineage (transcript slice, EntityRefs from cards, read grant to related prior Contexts) — not silent reuse of the Smart Prompt Run unless the product explicitly continues it.
5. **MCP-shaped data stays schema-addressed inside Context** (`ContextDelta` + schema hash + payload/blob ref). Façades do not redefine `CompanyResearch`-style hop records.
6. **Assistant tools are allow-listed projections** of Capability / Entity-query / Neuron-command contracts — not a parallel string API.
7. **Kernel never loads generated code.** Script driver is out of process.
8. **Fakes implement the same Capability contracts** as real MCP transports.

## 5. Execution spine

### 5.1 Execution Neuron

Aggregate responsible for:

- `StartExecution(workload, seed, grants, driverKind)`
- Creating/binding `ExecutionContext` Entity and seeding providers
- Driver lease (agent and/or script participants)
- Effect dispatch, policy, approvals, cancel, terminal outcomes
- Append Run Events; contribute to FactLedger
- Sole trusted writer of Context patches

### 5.2 ExecutionContext Entity

Durable working document for one Operation/Run:

- `Read()` / `Query(ContextQuery)` — on-demand context for assistant and Flutter
- Path-addressable content: seed, MCP/façade payloads, model notes, EntityRefs, cursors
- Patches append-only with digests (replayable with Effects)
- No Synapse membrane

### 5.3 Execution.Sdk

Shared by ChatTurnWorker, Smart Prompt runner, agent participants, and script sandboxes:

- `CallAsync(capabilityRequest)` → Effect + merge `ContextDelta`
- `Context.ReadAsync` / `QueryAsync`
- `OfferEntityCard(EntityRef)`
- `RequestApproval(...)`

### 5.4 Hybrid drivers

- **Agent driver (default):** loop over goal + Context + allow-listed Capabilities until done / approval / fail.
- **Script driver:** out-of-process single-file C# against Sdk only; used for Smart Prompt codegen and repair. Regular users still see English+chips.

### 5.5 Multi-agent teams

Parent Execution owns the goal and shared ExecutionContext. Team members are leased participants calling through the same grants/Sdk. Child Executions with linked Contexts are allowed when isolation is required; FactLedger correlates them.

## 6. Multi-chat and follow-ups

```
Owner
 └── Chat Neuron × N
      ├── transcript / turn queue
      ├── ActiveExecutionId?
      └── related ExecutionIds[]

Execution × N
 └── ExecutionContext Entity (1:1)
```

**Follow-up example:** Smart Prompt produces Chart card from Execution E1/Context C1. User asks “list new customers from today” → Execution E2/Context C2 seeded with Chart EntityRef + read lineage to C1 + façade grants → agent Queries C1/Chart or calls Salesforce façade again (fake/real).

## 7. Smart Prompts

### 7.1 Authoring UX

Structured document: text segments + **binding chips** (Gmail account, Salesforce, Chart, schedule, …) with logos/highlights. Not a raw string; not shown as C# by default.

### 7.2 Lifecycle

1. Save document + resolve chips → Capability grants + Account EntityRefs + Trigger subscriptions  
2. Activate revision (agent and/or script artifact)  
3. Triggers: schedule (Time), manual Run now, system/API demand  
4. Each fire → `StartExecution(SmartPrompt workload)` → new Context → driver  
5. Cards offered into a Chat without necessarily stealing `ActiveExecutionId`  
6. LearningEvidence / preferences propose **new revisions** — do not mutate the active revision in place  

### 7.3 MCP façades

Per-product modules (`Gmail`, `Salesforce`, …):

- Chip catalog, Account Entity, Capability verbs, approval policy  
- `IGmailTransport` / `ISalesforceTransport`  
- `Fake*Transport` when Aspire fakes enabled  
- `Mcp*Transport` later  
- Handlers return `ContextDelta`, not hand hop DTOs  

## 8. Preferences, context providers, explainability

### 8.1 `IExecutionContextProvider`

Modules register providers that contribute prompt blocks and Context seed patches at Execution start. Built-ins include:

- PreferenceContextProvider  
- TranscriptContextProvider  
- RelatedExecutionProvider  
- FactLedgerContextProvider  
- CapabilityCatalogProvider  

### 8.2 Preferences

Owner preference store (Neuron + queryable Entity projection). Corrections become durable rules injected on new Executions.

### 8.3 Explainability

“Why did you do it this way?” resolves a typed `ExplanationTrace` from:

- Chat CommandId / message that requested the work  
- ExecutionId + Run Events / Capability calls  
- Context paths touched  
- Preference rules that applied  
- EntityRefs (Chart, …)  

## 9. Orleans, observers, telemetry

Prefer DigitalBrain-native patterns over cloning IAW streams wholesale:

| Concern | Mechanism |
|---|---|
| Live observation / SSE wakeups | Neuron traffic journals + `IJournalObserver` |
| Ephemeral UI fan-out | Orleans BroadcastChannel (existing activation pattern) |
| Durable collaboration facts | Core thin FactLedger + Execution Run Events |
| Commands | Typed Synapse grain calls |

**Telemetry:** OTel spans for `execution.start`, `effect.call`, `context.patch`, façade capabilities, `driver.agent.turn`, approvals — correlated by `ExecutionId`, `ChatId`, `ActiveExecutionId`. Aspire dashboard + Aspire/digitalbrain MCP for agent diagnosis.

## 10. Fakes, Gemma, and test bar

### 10.1 Aspire fakes

`WithDigitalBrainFakes()` / `DigitalBrain:Fakes:Enabled`:

- Fake façade transports; real Kernel, Orleans, Execution, Context, Flutter SSE  
- Local Gemma (Ollama) for real chat reasoning unless a test explicitly uses AI testing clients  
- Deterministic sample emails/companies/SF ids for framework proof  

### 10.2 Tests (simple modules, thorough public surface)

- Simulation: ExecutionNeuron, Context patches, leases, Effect idempotency, ActiveExecutionId switch, lineage seed, approvals  
- Contract/golden: ContextDelta schema hashes, kit wire contracts  
- Aspire model: fakes flag + module wiring  
- E2E: chat → Execution → fake Capability → Chart card; Smart Prompt schedule fire with fakes; explainability returns evidence refs  

Every public `Execution.Sdk` and façade port path is covered. Avoid ceremony on private helpers.

## 11. Errors and approvals

- Capability failure → typed Effect failure; optional error stub in Context  
- Unknown dispatch outcome → `OutcomeUncertain`, pause (no retry storms)  
- Mutating façade Capabilities may require HITL approval (Flutter button); approval does not widen grants  
- Tests: fakes can auto-approve; E2E exercises UI approval path  

## 12. Type safety without hop-DTO hell

| Seam | What is typed |
|---|---|
| Grants / chips | CapabilityId + Account EntityRef |
| Calls | Allow-listed Capability requests; MCP input validated by tool schema at façade edge |
| Context | Path + schema hash + digest + payload/ref |
| UI | `Entity<TState>` / `EntityRef<TState>` |
| Assistant tools | Generated/bound from allow-listed contracts |

Not required: hand-authored `CompanyFromEmail` / `CompanyResearch` per scenario.

## 13. Build order (implementation plans)

Umbrella spec — separate plans per slice:

1. **Execution vertical:** Neuron + Context Entity + Sdk + FactLedger hook + OTel + simulation tests + fakes hook  
2. **Chat-on-Execution:** ChatTurnWorker uses Sdk; ActiveExecutionId; follow-up lineage seed; Flutter still works  
3. **Context providers + preferences + explainability**  
4. **Smart Prompt module:** editor doc/chips, triggers, activate, fake façades (Gmail/SF/Search)  
5. **Script codegen + Scripting driver**  
6. **Multi-agent team participants**  
7. **Real MCP transports** behind existing façade ports  

## 14. Documentation follow-ups

After written-spec approval:

- Update root `CONTEXT.md` vocabulary (Smart Prompt, ExecutionContext, ContextDelta, ActiveExecutionId)  
- Align `ARCHITECTURE.md` Smart Prompt section with this doc  
- Mark 2026-08-22 Behavior design as superseded  

## 15. Open points deferred to implementation plans

- Exact `ContextQuery` / path language  
- Preference Neuron vs Entity projection split details  
- MAF adapter shape for team participants  
- Chip editor Flutter UX polish  
