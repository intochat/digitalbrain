# Journals, Durable State, and History — who owns what

Three concepts share the word "journal" in this codebase's ancestry. This is the contract:

| Concept | Owner | What it is | Retention |
|---|---|---|---|
| Traffic journal | Every **neuron** (only neurons) | Incoming/Outgoing `SynapseDelivery` feeds: sequence-numbered observation windows with per-synapse-type tallies | Bounded: 512 entries / 512 KB per feed; reads past retention return a `ResetSnapshot` |
| Durable state | Every durable grain (neurons AND entities) | Orleans.Journaling persistence (`IDurableValue`/`IDurableList` over the `journal` blob connection) — infrastructure, not a domain concept; hosted via `DurableStateHosting.AddDigitalBrainDurableState` | Managed by Orleans.Journaling (append + compaction) |
| Corpus | The **owner** (or principal) | Watermarked, resumable story facts (`ICorpus`) — long-term history | Effectively unbounded |

## The rules

1. **Neurons own traffic journals. Entities own snapshots. Corpus owns history.**
   An `Entity<TState>` (`DigitalBrain.Core`) is a plain stateful grain: `Read()`/`SaveAsync()`
   over durable state. It has no journals and no synapse membrane, and it is never a
   synapse-graph endpoint.
2. **The session neuron is the owner's journal hub.** Owner-level views watch the session
   neuron's Outgoing journal (`OwnerSessionJournal`, the kernel SSE maps) and proxy-read
   subject neurons via `ISessionNeuron.ReadNeuronJournal`.
3. **Writes journal, reads don't.** Entity mutations are driven by neurons: a synapse fires,
   the handling neuron mutates the entity, and that neuron's Outgoing journal records the
   effect. Clients and UI read entities directly (`IDigitalBrain.GetEntity<TEntity>()`) —
   free and unjournaled.
4. **The word "journal" in domain code always means the traffic journal.** The persistence
   infrastructure uses durable-state language (`DurableStateHosting`, `DurableStateJson`,
   `DigitalBrainBuilder.DurableStateStore`). The blob resource/connection is still literally
   named `journal` (`DigitalBrainNames.Journal` / `JournalConnection`) — a deployed-name
   compatibility constraint, not vocabulary.

## Semantics pinned by tests (phase 2)

Resume sequences, the reset-snapshot path at the retention boundary, tallies,
checkpoint/restore, and watcher-drop behavior are pinned by the Tier 2 simulation suite
(`DigitalBrain.Testing`) — see the design spec §6 and §9.
