# Chat-First Reduction Design

**Goal:** Reduce DigitalBrain to a small, durable chat-and-voice product while retaining the Aspire composition layer, Client and MCP product surfaces, the reusable testing framework, the complete Flutter prototype UI, and small Time and Memory modules.

## Decisions and constraints

- The Flutter projects, including the Windowing tab and prototype UI, and all `src/Modules/**` code are untouched during the first reduction pass.
- `src/Aspire/**` remains. Its resource graph may be simplified only when an eliminated product capability no longer requires a resource.
- `src/Testing/**` and `tests/**` remain. Product-specific tests are replaced by a small chat/voice/time/memory suite; reusable test host, simulation, and E2E infrastructure stays.
- The Flutter UI kit remains.
- `src/Kernel/DigitalBrain.Client/**` remains as the product-facing Orleans client seam.
- `src/Kernel/DigitalBrain.Mcp/**` remains as a product surface built on `DigitalBrain.Client`; it becomes a thin direct façade over retained chat, voice, memory, and time operations.
- Qdrant remains during the first reduction pass. Its future retention is a separate Memory-module decision and is not authorized by this design.
- The only durable product identity is `ConversationId`, represented by the Orleans grain key and the HTTP chat name. Owner, principal, actor, partition, entity, neuron, correlation, and command identity wrappers are removed in the destination architecture.
- The product is an anonymous chat experience. Authentication, provider authorization cards, and multi-user isolation are not part of the destination.
- Generic capability discovery and graph routing do not remain. MCP, Client, and the Flutter prototype surfaces remain.

## Target runtime

```text
text  -> ChatNeuron -> AssistantNeuron -> ChatStateEntity -> SSE -> Flutter
voice -> VoiceToTextNeuron -> ChatNeuron -> AssistantNeuron -> same SSE stream
time  -> TimerNeuron | ScheduleNeuron -> ChatNeuron
memory <-> FactMemoryNeuron <-> AssistantNeuron
MCP   -> DigitalBrainClient -> the same chat, voice, memory, and time seams
```

`ChatNeuron` is the conversation aggregate. It owns the ordered transcript, a single in-flight turn, and cancellation. `ChatStateEntity` is the UI projection of that transcript; it contains only messages and the status required for the chat screen. There is no generic UI renderer or entity discovery mechanism.

`AssistantNeuron` uses one configured LLM and has direct, explicit dependencies on `FactMemoryNeuron`, `TimerNeuron`, and `ScheduleNeuron`. It does not discover or invoke arbitrary capabilities. Voice is a direct endpoint-to-neuron flow that transcribes an upload and submits its text to the same chat aggregate.

`FactMemoryNeuron` stores and searches short conversation facts. Qdrant remains during the first reduction pass; whether factual memory keeps a vector implementation is an explicitly deferred Memory-module decision. `TimerNeuron` is a one-shot reminder; `ScheduleNeuron` is a recurring reminder. These are the only retained Time and Memory behaviours.

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
│  ├─ DigitalBrain.Client/
│  │  ├─ IDigitalBrain.cs
│  │  └─ DigitalBrainClient.cs
│  └─ DigitalBrain.Kernel/
│     ├─ Program.cs
│     ├─ ChatEndpoints.cs
│     ├─ VoiceEndpoints.cs
│     └─ ChatStream.cs
│  └─ DigitalBrain.Mcp/
│     ├─ Program.cs
│     ├─ ChatTools.cs
│     ├─ MemoryTools.cs
│     └─ TimeTools.cs
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
└─ Modules/UI/Flutter/                  # retained prototype UI
   ├─ kit/
   ├─ core/
   └─ shell/                            # includes Windowing and demos

tests/                                  # retained, smaller product suite
├─ DigitalBrain.Simulation.Tests/
├─ DigitalBrain.E2E.Tests/
└─ DigitalBrain.Aspire.Tests/
```

Names and project boundaries may be migrated incrementally rather than renamed in one destructive change. The behavioural constraints above, not the intermediate names, are the contract.

## Reduction phases

### Phase 1: non-module perimeter reduction

This is the next implementation scope. Do not edit Flutter or module code.

1. Keep the Client, MCP executable and tools, AppHost MCP resource, and their retained test coverage.
2. Remove CodeGraph-proven graph-era kernel remnants and their tests, including orphaned `Brain*`, graph-event, and route/projection code.
3. Keep the existing kernel chat, voice, SSE, durable runtime, Client, MCP, and Aspire wiring that unchanged modules require.
4. Keep all test projects and reusable test helpers. Replace only tests for deleted product surfaces with focused chat/voice smoke coverage.
5. Re-index CodeGraph and record the remaining dependency paths before proposing the module pass.

### Phase 2: module compaction

This begins only after a separate approval. It replaces generic capability and identity infrastructure with the direct chat/time/memory seams above. Flutter, including its Windowing and prototype screens, remains out of scope.

## Deletion rules

- A file can be deleted only after CodeGraph finds no retained production consumer, or its retained consumer is changed in the same commit.
- Aspire and Testing project directories are never deleted by this initiative.
- Client, MCP, and Flutter project directories are never deleted by this initiative.
- Qdrant is not deleted without a separate explicit decision for the Memory module.
- A removed product behaviour loses its associated tests; the testing framework and tests for retained behaviours stay.
- Each reduction commit must build the solution and pass the retained test suite.
- The user-owned untracked `docs/research/` directory is out of scope.

## Acceptance criteria

After Phase 1, the solution still builds and supports the unchanged Flutter chat and voice flow; Client, MCP, the AppHost MCP resource, Qdrant, Aspire, and testing projects remain present and operational. No graph-era production route or test assertion remains.

After Phase 2, the product supports text chat, voice-message transcription, persisted transcript, direct factual memory, one-shot and recurring reminders, and SSE delivery to Flutter. It has no generic capability discovery or multi-layer identity wrappers.
