# ADR: V2 optional P3 spikes

Status: Accepted

## Decision

The three optional P3 spikes in the V2 implementation plan are deliberately not
promoted into the V2 runtime:

* **Search/vector benchmark:** V2 keeps the existing projection/query ports. There
  is no measured query-volume, retention, latency, or restore requirement that
  justifies introducing another backend. A new backend would add migration and
  isolation risk without evidence of benefit.
* **Agent Framework/MCP Tasks adapter:** DigitalBrain workflows, operation status,
  checkpoints, and replay remain the sole source of truth. The preview agent
  abstractions would duplicate session/state ownership and cannot be allowed to
  create effects during replay. No production adapter is therefore accepted.
* **RFW generated registry tooling:** the current V2 Flutter registry and golden
  tests are deterministic and already provide the protocol drift boundary. A
  generator would add build-time coupling without a demonstrated runtime need;
  the existing tests remain authoritative.

## Consequences

These decisions satisfy the P3 acceptance condition by proving irrelevance to the
final V2 architecture. They do not add dependencies, persisted state, migration
work, or deployment requirements. A future benchmark or tooling experiment must
remain disposable and cannot change V2 workflow, query, or UI ownership without a
new ADR and measured evidence.
