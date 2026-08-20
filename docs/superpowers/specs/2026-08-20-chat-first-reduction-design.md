# Chat-First Reduction Design

**Goal:** Reduce DigitalBrain to a small, durable chat-and-voice product while retaining the Aspire composition layer, the reusable testing framework, the Flutter UI kit, and small Time and Memory modules.

## Decisions and constraints

- The Flutter projects and all `src/Modules/**` code are untouched during the first reduction pass.
- `src/Aspire/**` remains. Its resource graph may be simplified only when an eliminated product capability no longer requires a resource.
- `src/Testing/**` and `tests/**` remain. Product-specific tests are replaced by a small chat/voice/time/memory suite; reusable test host, simulation, and E2E infrastructure stays.
- The Flutter UI kit remains.
- The only durable product identity is `ConversationId`, represented by the Orleans grain key and the HTTP chat name. Owner, principal, actor, partition, entity, neuron, correlation, and command identity wrappers are removed in the destination architecture.
- The product is an anonymous chat experience. Authentication, provider authorization cards, and multi-user isolation are not part of the destination.
- No MCP surface, generic capability discovery, graph routing, behavior studio, chart/surface rendering, windowing, gallery, or activity product surfaces remain.

## Target runtime

```text
text  -> ChatNeuron -> AssistantNeuron -> ChatStateEntity -> SSE -> Flutter
voice -> VoiceToTextNeuron -> ChatNeuron -> AssistantNeuron -> same SSE stream
time  -> TimerNeuron | ScheduleNeuron -> ChatNeuron
memory <-> FactMemoryNeuron <-> AssistantNeuron
```

`ChatNeuron` is the conversation aggregate. It owns the ordered transcript, a single in-flight turn, and cancellation. `ChatStateEntity` is the UI projection of that transcript; it contains only messages and the status required for the chat screen. There is no generic UI renderer or entity discovery mechanism.

`AssistantNeuron` uses one configured LLM and has direct, explicit dependencies on `FactMemoryNeuron`, `TimerNeuron`, and `ScheduleNeuron`. It does not discover or invoke arbitrary capabilities. Voice is a direct endpoint-to-neuron flow that transcribes an upload and submits its text to the same chat aggregate.

`FactMemoryNeuron` stores and searches short conversation facts. Vector-memory/Qdrant is excluded until a concrete product need proves lexical fact search insufficient. `TimerNeuron` is a one-shot reminder; `ScheduleNeuron` is a recurring reminder. These are the only retained Time and Memory behaviours.

## Target source tree

```text
src/
├─ Aspire/                              # retained composition layer
│  ├─ DigitalBrain.AppHost/
│  ├─ DigitalBrain.Aspire/
│  ├─ DigitalBrain.Aspire.Hosting/
│  └─ DigitalBrain.ServiceDefaults/
│
├─ Kernel/
│  ├─ DigitalBrain.Contracts/
│  │  ├─ ConversationId.cs
│  │  ├─ Chat/{IChat.cs, ChatMessage.cs}
│  │  ├─ AI/{IAssistant.cs, IVoiceToText.cs}
│  │  ├─ Memory/IFactMemory.cs
│  │  └─ Time/{ITimer.cs, ISchedule.cs}
│  ├─ DigitalBrain.Runtime/
│  │  ├─ Neuron.cs
│  │  ├─ Entity.cs
│  │  ├─ GrainStorage.cs
│  │  └─ RuntimeHosting.cs
│  └─ DigitalBrain.Kernel/
│     ├─ Program.cs
│     ├─ ChatEndpoints.cs
│     ├─ VoiceEndpoints.cs
│     └─ ChatStream.cs
│
├─ Modules/
│  ├─ AI/{AssistantNeuron.cs, VoiceToTextNeuron.cs, AiHosting.cs}
│  ├─ Memory/FactMemoryNeuron.cs
│  ├─ Time/{TimerNeuron.cs, ScheduleNeuron.cs}
│  └─ UI/{ChatNeuron.cs, ChatStateEntity.cs}
│
├─ Testing/                             # retained reusable framework
│  ├─ DigitalBrain.Testing/
│  ├─ DigitalBrain.Testing.Bdd/
│  └─ DigitalBrain.Testing.E2E/
│
└─ Modules/UI/Flutter/                  # retained; reduced in a later phase
   ├─ kit/
   ├─ core/
   └─ shell/

tests/                                  # retained, smaller product suite
├─ DigitalBrain.Simulation.Tests/
├─ DigitalBrain.E2E.Tests/
└─ DigitalBrain.Aspire.Tests/
```

Names and project boundaries may be migrated incrementally rather than renamed in one destructive change. The behavioural constraints above, not the intermediate names, are the contract.

## Reduction phases

### Phase 1: non-module perimeter reduction

This is the next implementation scope. Do not edit Flutter or module code.

1. Remove the MCP executable, tools, AppHost MCP resource, and MCP-only tests.
2. Remove CodeGraph-proven graph-era kernel remnants and their tests, including orphaned `Brain*`, graph-event, and route/projection code.
3. Keep the existing kernel chat, voice, SSE, durable runtime, client, and Aspire wiring that unchanged modules require.
4. Keep all test projects and reusable test helpers. Replace only tests for deleted product surfaces with focused chat/voice smoke coverage.
5. Re-index CodeGraph and record the remaining dependency paths before proposing the module pass.

### Phase 2: module and Flutter compaction

This begins only after a separate approval. It replaces generic capability and identity infrastructure with the direct chat/time/memory seams above, then trims Flutter to chat and voice while retaining the UI kit.

## Deletion rules

- A file can be deleted only after CodeGraph finds no retained production consumer, or its retained consumer is changed in the same commit.
- Aspire and Testing project directories are never deleted by this initiative.
- A removed product behaviour loses its associated tests; the testing framework and tests for retained behaviours stay.
- Each reduction commit must build the solution and pass the retained test suite.
- The user-owned untracked `docs/research/` directory is out of scope.

## Acceptance criteria

After Phase 1, the solution still builds and supports the unchanged Flutter chat and voice flow; it contains no MCP process or AppHost MCP resource, no remaining graph-era production route, and no tests asserting deleted topology behaviour. Aspire and testing projects remain present and operational.

After Phase 2, the product supports text chat, voice-message transcription, persisted transcript, direct factual memory, one-shot and recurring reminders, and SSE delivery to Flutter. It has no generic capability discovery or multi-layer identity wrappers.
