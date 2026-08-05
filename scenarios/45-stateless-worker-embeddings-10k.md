# Scenario 45: Stateless worker fan-out for embeddings of 10k notes

## User intent
The owner imports ~10k notes and wants them embedded into vector memory quickly. The UI should show progress; the brain must not pin one grain with 10k sequential embeds; failures retry per chunk without redoing completed work.

## Trigger
`NotesImportCompleted(count≈10000)` or user “Index my notes” command.

## Imagined modules
- Notes importer
- Embedding stateless workers
- VectorMemory index
- Progress UI
- Blob/chunk store

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| NotesIndexer / import-3 | Orchestrates chunks; journal progress |
| EmbedWorker (stateless) | EmbedChunk answers |
| VectorMemory / life | Upsert vectors |
| UiProjector / shell | Progress bar + ETA |
| Pulse / import-3 | Optional progress ticks |

## Synapse choreography
1. NotesIndexer journals import manifest; splits into chunks (e.g., 50 notes).
2. Fan-out many Ask `EmbedChunk(chunkId, texts)` to stateless workers (parallelism capped).
3. Each Answer `EmbeddedChunk` → NotesIndexer turn continues; Ask `UpsertVectors`.
4. Periodically Emit `UiSurface(Progress n/10000)` and `IndexProgress` broadcast.
5. On chunk failure: journal `ChunkFailed`; Schedule retry; do not reset global progress.
6. Final `NotesIndexReady`; Assistant can notify.
7. Workers hold no durable owner state; all durability in NotesIndexer + VectorMemory journals/stores.

## Orleans / Core surface exercised
Stateless workers; fan-out/fan-in via orchestrator journal; DurableGrain journals; Schedule retries; placement; outbox; request context owner on each embed ask; streams optional for progress.

## Rich experience
Full-screen indexing pane: progress bar, failed chunk list with Retry, throughput chart, cancel import button (`IndexCancel`).

## Failure / adversarial cases
- Double upsert same chunk after retry → chunkId idempotency in VectorMemory.
- Orchestrator reentrancy if it awaits all embeds in one turn → must use Continue answers.
- Worker pool starvation affecting interactive chat → separate priority / max-inflight.
- Cancel mid-flight → in-flight answers ignored if epoch cancelled in journal.

## Capability claim
DigitalBrain can blast heavy embedding work through stateless workers while a durable orchestrator neuron owns progress and truth—something a single-threaded chat tool call cannot scale to 10k notes.
