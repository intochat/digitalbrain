# Architecture grill dossier

> **Status:** historical exploration. These records informed earlier designs but
> do not define the current module/runtime seam. See
> [the current architecture](../CORE-ARCHITECTURE.md).

**Current architecture:** [`../CORE-ARCHITECTURE.md`](../CORE-ARCHITECTURE.md)
**Historical scenarios:** [`../scenarios/`](../scenarios/README.md)

| # | File | Topic | Outcome |
|---|---|---|---|
| 01 | [01-time-and-reminders.md](01-time-and-reminders.md) | Timers / reminders / Schedule | Hybrid: modules schedule **facts**; Core wakes; Orleans timers sealed |
| 02 | [02-communication-bus.md](02-communication-bus.md) | Emit / Ask / Reply / Connect / streams | One bus; edge Send only Stage-1 |
| 03 | [03-journal-durability.md](03-journal-durability.md) | Journal-as-outbox, poison, FIFO | Post-handler stage; no v1 retraction |
| 04 | [04-modules-behaviors.md](04-modules-behaviors.md) | Catalog, behaviors = neurons | Kernel owns ALC; Core epoch hook |
| 05 | [05-orleans-sealed.md](05-orleans-sealed.md) | Full Orleans, sealed from modules | Feature matrix |
| 06 | [06-edge-and-ui.md](06-edge-and-ui.md) | Brain / Session / multimodal | Thin edge; UI = module synapses |
| 07 | [07-failure-isolation.md](07-failure-isolation.md) | Deadlock, depth, provenance | Depth 16 storm budget; unforgeable Source |
| 08 | [08-delete-pass.md](08-delete-pass.md) | Maximum deletion | Stage-1 inventory |
| 09 | [09-contradictions-resolved.md](09-contradictions-resolved.md) | Cross-grill conflicts | **BINDING FINAL LAW** |
| 10 | [10-product-os-fit.md](10-product-os-fit.md) | OS product fit | Core = kernel physics, not full product OS |
| 11 | [11-proof-catalog.md](11-proof-catalog.md) | Test obligations | P01–P35 |

## User correction locked in

> Timers and reminders might come from modules; Core might not expose those.

**Meaning in law:** modules never call `RegisterGrainTimer` / `IRemindable` / reminder APIs.  
Modules use `Schedule(fact, period)` / `Unschedule`. Product “remind me” / cron / TZ = **Time module**.  
Core still uses timers/reminders **internally** for outbox drain + schedule ticks + dormant wake.
