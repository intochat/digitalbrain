# Scenario 37: Nested asks: chat asks memory asks vector store

## User intent
The owner asks a personal question: “What did we decide about the Berlin office last time it came up?” Chat must Ask memory; memory may Ask vector search and a transcript store; answers propagate back as continuations without blocking stacks or losing provenance.

## Trigger
Chat `UserMessaged` with recall intent.

## Imagined modules
- Assistant / Chat
- EpisodicMemory neuron
- VectorStore / embeddings
- TranscriptArchive
- Ui citations projector

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / desk | User turn |
| Assistant / desk | Plans recall capability |
| EpisodicMemory / life | Answers RecallDecision; asks children |
| VectorStore / life | Answers VectorSearch |
| TranscriptArchive / life | Answers FetchSnippet |
| UiProjector / shell | Citation cards |

## Synapse choreography
1. UserMessaged → Assistant Ask `RecallDecision(query)` (directed to EpisodicMemory).
2. EpisodicMemory turn 1: Ask `VectorSearch(q)`; Ask optional `FetchSnippet(ids)`—non-blocking Continues.
3. VectorStore answers `Answer<VectorSearch, Hits>` → EpisodicMemory turn 2 journals; if snippets missing, waits (journal join).
4. TranscriptArchive answers → turn 3: EpisodicMemory returns `DecisionRecall` reply to original asker (Assistant).
5. Assistant Emits `AssistantResponded` + `UiSurface(Citations)`.
6. Overhear UsageMemory may record that RecallDecision was asked (fine-tune corpus), without being the answerer.
7. Entire chain shares correlation id; each journal shows source NeuronId.

## Orleans / Core surface exercised
Ask/Answer/Continue; DurableGrain journals as join state; restart survival mid-nest; request context; serialized turns; no reentrancy (memory must not be asked by vector in a way that calls back same turn into memory awaiting itself); outbox for answer delivery.

## Rich experience
Answer with footnote chips opening snippet panes; “show chain” debug revealing nested asks; latency waterfalls per hop.

## Failure / adversarial cases
- Deadlock if Answer delivery re-enters open turn → Core forbids reentrancy; continue is mandatory.
- Ambiguous two memories answering RecallDecision → catalog boot failure.
- Vector hits from wrong owner index → identity on VectorStore grain key.
- AskExpired on slow vector → EpisodicMemory hears AskExpired and returns degraded answer, not hang.

## Capability claim
DigitalBrain expresses nested cognitive asks as durable multi-turn facts with join-on-journal—not a synchronous call stack inside one agent tool.
