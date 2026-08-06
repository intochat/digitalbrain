# Core Quality 100 — architecture, refactor, and proof plan

**Status:** PLAN (not yet executed).  
**Date:** 2026-08-06.  
**Repo:** `digitalbrain-v2-core` branch `v2-core`.  
**Authority:** `CORE-ARCHITECTURE.md` → `architecture-grill/09-contradictions-resolved.md` FINAL LAW → `11-proof-catalog.md` → this plan (execution).  
**Honest bar today:** ~40% confidence for product attach (chat / Flutter / Gmail / SF). Green scenarios prove **choreography expressibility on mocks**, not kernel hardness.  
**Target bar:** ~100% confidence that **delivery, topology, storm bounds, isolation, and edge observation** are laws the suite will go red for — so modules written to the algebra “just work.”

---

## 0 · Why confidence is low (grill of current reality)

| What we have | What it actually proves | What it does **not** prove |
|---|---|---|
| ~16 physics tests | Some greeter / schedule / connect / fail paths | Depth budget, FIFO under dual drain, compaction, read interleave, storm loops |
| ~49 “scenario” tests | Vocabulary + mock modules can journal a story | Real IO, approval-gated CRM, multi-silo, UI render, LLM tools |
| 83 total green | No compile break; happy paths | Adversarial delivery physics; absence of infinite fan-out |
| Fat `Neuron` (~1.5k LOC across partials) | Everything in one activation | Deep modules; readable invariants; unit-testable delivery without Orleans |
| `DeliveryPolicy` without `MaximumDepth` | Horizons bound **failed** delivery | Successful A→B→C→A storms (FINAL LAW §25) |
| `Compact()` never called | Soft retention constants exist | Journals stay bounded under long life |
| Mock Salesforce auto-completes enrich | Fan-out wiring | Product-right propose → approve → apply |

**Diagnosis:** the suite is **use-case heavy and physics light**. Scenario DisplayNames read like product demos; many do not lock a named law with a minimal double. That is why “tests are green” does not create confidence.

**What “100%” means here (measurable):**

1. Every FINAL LAW row has ≥1 **BDD physics** feature that fails if the law breaks.  
2. Depth, compaction, FIFO hole, unforgeable Source, dual-answerer boot, self-proxy ban are green under adversarial doubles.  
3. Neuron public surface is thin; delivery/journal/topology are deep modules with unit hosts where possible.  
4. Multi-instance mechanisms (Name locus + Connect + declaration) are proven with **abstract kinds**, not “Elon only.”  
5. UI path is specified as **module synapses** with Core guarantees (journal visibility, same bus) — not Core widgets.  
6. Scenario suite is demoted to **expressibility pack** (optional / secondary gate); physics BDD is the **root gate**.

---

## 1 · Target Core architecture (improved shape)

### 1.1 One-line algebra (unchanged — do not grow)

```
hear (INeuron<T>) · say (Emit) · ask (Ask) · answer (return / Reply / deferred TReply)
· wire (Connect/Disconnect) · later (Schedule/Unschedule) · heal (hear outcomes)
· edge (Session Emit/Send/AskAsync + Brain.ReadAsync)
```

**Delete pressure:** no second bus, no in-neuron free Send (Stage-1), no streams as n2n, no fat Abstractions, no Answer<> reconstruction ABI.

### 1.2 Deep modules (replace fat Neuron god-object)

Today `Neuron` owns turn, routing, ask pins, schedule, connections, commit, drain, wire closers. That is the kitchen-sink disease the graveyard already killed (Projects Neuron 600+ LOC; v1 12-file partial with 15 shared fields).

**Target ownership (one deep module = small public surface, big internal correctness):**

```text
DigitalBrain.Core/
  Abstractions surface (via package DigitalBrain.Abstractions) — 4 types only

  Edge/
    Brain, Session                    # thin edge; poll sugar only

  Topology/
    Catalog                           # boot topology + fingerprint (pure, unit-testable)
    CoreSynapses                      # closed pack vocabulary
    SynapseRef, routing tables        # Connect resolution pure functions

  Journal/
    NeuronJournal                     # durable keys, append, watermark, pins, schedule table
    BodyCodec, JournalReading         # rehydrate / read model
    Compaction                        # floor + soft bounds (called from lifecycle)

  Delivery/
    DeliveryPolicy                    # ALL bounds including MaximumDepth=16
    Outbox                            # unsettled index, progress, FIFO blockedTargets
    Drain                             # post-commit deliver loop (unit-testable with fake transport)
    OutboxWakeup                      # Core-only IRemindable companion
    DeliveryEnvelope                  # Core-private wire envelope (+ private depth)

  Turn/
    TurnContext                       # staged emissions / schedule changes / unpin
    TurnCommit                        # stage batch + one WriteStateAsync + poison

  Neuron/
    Neuron                            # THIN: verbs + open turn + seal Obsolete APIs
    NeuronOfState                     # TState slot only
    Neuron.Transport / Session        # grain wire surfaces only
    NeuronConcurrency                 # activation refuse matrix

  Filters/ Hosting/ Exceptions/       # as now, split, no narrative dumps
```

**Neuron after slim (module author sees only):**

| Member | Job |
|---|---|
| `Id` | kind + name |
| `Emit` / `Reply` / `Ask` / `Schedule` / `Unschedule` | stage only |
| `State` (`Neuron<TState>`) | one slot |
| `HandleAsync` / `IAnswers` | module code |

Everything else is internal composition. **Partial classes only if a file > ~250 lines after split** — not as fake architecture.

### 1.3 Multi-instance is Name, not Type (Elon + Vlad)

This is **already** the intended mechanism; tests must **guarantee** it, not re-implement product X.

| Mechanism | Guarantee |
|---|---|
| `NeuronId(Kind, Name)` | Kind = lowercased class name (`xaccount`); Name = instance locus (`elonmusk`, `vlad`) |
| **Declaration fan-out** | `Emit` → every kind declaring `INeuron<T>` at **same Name** as emitter |
| **Connect** | Instance wiring: `Connect("xpost", new NeuronId("btcdot", "dashboard"))` on a **specific** emitter |
| **Ghost rule** | Connection for fact F to kind K suppresses same-Name declared K |
| **Activation** | Virtual actor: first delivery / schedule tick activates; journal survives deactivate |
| **No correlation-GUID broadcast instances** | Graveyard kill: every broadcast must land on **durable named** addresses |

**North-star composition (general, not Elon-only):**

```csharp
// Two activations of the same kind, different Names
// Grain keys: kind=xaccount, key=elonmusk | kind=xaccount, key=vlad
public sealed class XAccount : Neuron<XCursor>, INeuron<WatchAccount>, INeuron<PollX> { … }

// Watch both: edge Send WatchAccount into each instance (or a coordinator Emits)
await session.SendAsync(new NeuronId("xaccount", "elonmusk"), new WatchAccount(), ct);
await session.SendAsync(new NeuronId("xaccount", "vlad"), new WatchAccount(), ct);

// Behavior at dashboard locus hears ambient XPost only if declarations/connects say so
// Emit from xaccount/elonmusk fans out to listeners named "elonmusk" unless Connect redirects
```

**Law to prove (abstract doubles, not Twitter):**

> Two instances of kind `K` with names `A` and `B` never mix declared fan-out.  
> Connect on `K/A` does not alter topology of `K/B`.  
> Schedule on `K/A` does not tick `K/B`.  
> Watermarks and journals are per activation identity.

### 1.4 “Broadcast even when activated / anyone can subscribe”

**Correct Stage-1 meaning (do not resurrect stream bus):**

| Wanted product language | Lawful mechanism |
|---|---|
| “Anyone who cares hears” | Declare `INeuron<T>` at the right **Name** (or Connect foreign Name) |
| “Works if subscriber activates later” | Journal + outbox: deliver when receiver exists; watermark dedup; **not** Orleans stream subscription timing |
| “Late join history” | `Brain.ReadAsync` / module projection — **never** stream replay as authority |
| “Subscribe at runtime” | Stage-1: redeploy catalog (N+1 kind) or **Connect** rewire; Stage-3 Kernel: hot epoch |

**Forbidden resurrection:**

- GUID-named broadcast grains (v1 `BroadcastReceiver` — empty, unaddressable, dual path).  
- Streams as n2n.  
- String `[WireTo]` routing (`ReceiverNeUniformType` silent death).

**Activation guarantee to implement + prove:**

> An idle / deactivated receiver with unsettled inbound (sender outbox) or a due schedule **wakes** (drain timer + `OutboxWakeup` reminder). Delivery is at-least-once; subscriber need not be hot at emit time.

### 1.5 UI capabilities (forms, buttons, charts) — alignment without Core chrome

**Law:** UI is a **module synapse vocabulary**, not a Core subsystem. Core guarantees only that UI facts are first-class on the bus (journaled, Source unforgeable, fan-out/Connect, ReadAsync observable).

**Closed product vocabulary (module pack, e.g. `DigitalBrain.Ui` later — not Core ABI growth):**

```csharp
// Illustrative product vocabulary — lives in a UI module package, NOT Abstractions.
public sealed record UiSurface(string Pane, Widget Root) : Synapse;
public abstract record Widget;
public sealed record Label(string Text) : Widget;
public sealed record Column(IReadOnlyList<Widget> Children) : Widget;
public sealed record Form(string FormId, IReadOnlyList<Field> Fields, Synapse OnSubmit) : Widget;
public sealed record Field(string Name, string Kind, string? Value);
public sealed record Button(string Text, Synapse OnTap) : Widget;
public sealed record LineChart(string Title, IReadOnlyList<ChartPoint> Points) : Widget;
public sealed record ChartPoint(double X, double Y);
```

**Loop (assistant → owner → modules):**

```text
Assistant / enricher Emit(UiSurface(form|chart|buttons))
  → shell renderer neuron INeuron<UiSurface> (Flutter host is edge)
  → owner tap/submit → Session.EmitAsync(OnTap / OnSubmit body)  // typed fact, not string callback
  → domain neuron hears CompleteTask / ApproveEnrichment / …
  → optional new UiSurface
```

**Core guarantees that make this safe:**

| Guarantee | Why UI needs it |
|---|---|
| OnTap/OnSubmit are **Synapse bodies**, not free delegates | Journal-visible; replayable; no silent RPC |
| Source is Core-stamped | Injection cannot claim “button pressed by assistant” |
| Same bus as CRM/email | Approval buttons are not a second channel |
| ReadAsync during work (P20) | Progressive charts while long ask runs |
| Multi-instance Name | `chart/dashboard` vs `chart/mobile` independent |

**Core does not ship:** widget union, Flutter, RFW, layout engine. Those are modules + edge host.

### 1.6 Bottlenecks (design for them, prove under load doubles)

| Bottleneck | Mechanism | Risk if ignored | Proof shape |
|---|---|---|---|
| **Serialized turn per neuron** | One turn at a time | Chat occupied whole model+tool chain | Long-handler + concurrent Read (P20); force multi-turn tools |
| **Drain single-threaded with turn** | Drain on same activation | Outbox stalls while handler runs | Delivery continues after commit; no drain inside handler |
| **Fan-out N listeners** | Emit snapshot of receivers | Storm / depth | MaximumDepth + P18 |
| **Many instances (1000 X accounts)** | One grain each | Memory / reminder count | Schedule+wakeup scaling note; optional placement Stage-2 |
| **Edge AskAsync poll** | 75ms journal poll | Chatty edge | Document; Stage-2 WatchAsync same cursor |
| **BodyCodec JSON** | Rehydrate every deliver | CPU on large bodies | Soft size bounds; no second serializer |
| **Catalog reflection at boot** | One Build | Slow boot with huge packs | Unit: Build O(types); fingerprint stable |
| **Compaction off** | Compact never called | Unbounded journal RAM/disk | Wire Compact; prove floor |

---

## 2 · Physics that must be 100% (named laws → proofs)

Map 1:1 to BDD features. Prefer **abstract doubles** (`KindA`, `KindB`, `FactX`) over Gmail story names.

| ID | Law (short) | Proof style | Status today |
|---|---|---|---|
| L01 | Commit before dispatch | Cluster: emit then handler order | Partial |
| L02 | Handler throw → zero durable | Cluster: throw after stage | Partial |
| L03 | Commit fail → poison + reload | Cluster: fault provider | Partial |
| L04 | Watermark dedup silent success | Cluster: redeliver | Partial |
| L05 | FIFO per receiver + hole | Cluster: sticky fail then recover | **Missing** |
| L06 | DeliveryFailed on sender | Cluster | Partial |
| L07 | MaximumDepth=16 storm terminal | Cluster: 17-hop chain | **Missing** |
| L08 | Depth not on public metadata | Unit ABI scan | **Missing** |
| L09 | Self-proxy throws | Cluster/filter | Present |
| L10 | No Reentrant/MayInterleave | Unit | Present |
| L11 | No module IRemindable / extra grain iface | Unit | Partial |
| L12 | Durable key gate | Unit | **Missing/weak** |
| L13 | Declaration fan-out same Name | Cluster | Partial |
| L14 | Multi-instance Name isolation | Cluster: A vs B | Partial (locus) |
| L15 | Connect + ghost | Cluster | Present |
| L16 | Disconnect restores declared | Cluster | Partial |
| L17 | Connect refuse leaves table | Cluster | Present |
| L18 | Ask ≠ Emit(question) | Cluster | **Missing** |
| L19 | ≤1 answerer boot fail | Unit | Present |
| L20 | No-answerer DeliveryFailed fast | Cluster | Present |
| L21 | AskExpired + late reply no continue | Cluster | Present |
| L22 | Schedule tick ordinary turn | Cluster | Present |
| L23 | Schedule survives deactivate | Cluster | Partial (sc46) |
| L24 | ScheduleFailed then unschedule | Cluster | Present |
| L25 | Zero receivers legal | Cluster | Present |
| L26 | Unforgeable Source | Cluster + ABI | **Weak** |
| L27 | Compact under floor | Cluster | **Missing** |
| L28 | Read interleaves long turn | Cluster | **Missing** |
| L29 | Wakeup when only schedule | Cluster | Partial |
| L30 | Outbox wakes deactivated receiver | Cluster | **Weak** |
| L31 | Session Send no declaration fan-out | Cluster | Partial |
| L32 | Edge AskAsync journal observe | Cluster | Partial |
| L33 | Fingerprint stable | Unit | **Weak** |
| L34 | Reserved kinds not module-listenable | Unit | **Weak** |
| L35 | No infinite silent retry (attempts/horizon) | Cluster | Partial |

**Product scenarios (01–50)** become **expressibility pack**: run on CI, but **cannot alone gate “Core solid.”**

---

## 3 · Test architecture (how tests create confidence)

### 3.1 Three layers (never conflate)

```text
┌─────────────────────────────────────────────────────────────┐
│ L1 · Pure unit (no Orleans)                                 │
│ Catalog.Build, routing resolution, DeliveryPolicy constants │
│ depth arithmetic, fingerprint, ABI surface scan             │
├─────────────────────────────────────────────────────────────┤
│ L2 · BDD physics cluster (real silo, abstract doubles)      │
│ Reqnroll/xUnit + Gherkin: delivery, topology, time, ask     │
│ Journals are the oracle — assert said/heard/DeliveryFailed  │
├─────────────────────────────────────────────────────────────┤
│ L3 · Expressibility pack (optional / nightly)               │
│ Current scenarios 01–50 + mocks — “can compose a story”     │
└─────────────────────────────────────────────────────────────┘
```

**Root completion gate:** L1 + L2 green.  
**L3 green** never upgrades Core law confidence.

### 3.2 BDD feature map (general logic, not use cases)

Package suggestion: `DigitalBrain.Core.Bdd` (Reqnroll) **or** xUnit theories with Gherkin-as-DisplayName until Reqnroll is wired. Prefer real Gherkin files under `tests/features/physics/`.

**Example features (illustrative — each scenario maps to Lxx):**

```gherkin
# features/physics/delivery.feature
Feature: Journaled delivery
  Core delivers only after commit; failures are vocabulary; storms are bounded.

  Scenario: Handler throw leaves no durable journal lines (L02)
    Given a receiver that throws after staging an Emit
    When a fact is delivered to that receiver
    Then the receiver journal has no new heard or said entries
    And the sender still has unsettled outbox progress or retries per policy

  Scenario: Successful reaction chain exceeds depth budget (L07)
    Given a chain of 17 neuron kinds each Emitting to the next on hear
    When the edge Emits the head fact
    Then some sender journals DeliveryFailed with a depth reason on attempt 1
    And the chain does not run for the full retry horizon

  Scenario: Two instances of the same kind isolate declared fan-out (L14)
    Given kind Probe with names "alpha" and "beta"
    And kind Listener declared for ProbeFact
    When Probe "alpha" Emits ProbeFact
    Then Listener "alpha" hears it
    And Listener "beta" does not hear it
    And Probe "beta" journal is empty of that fact

  Scenario: Connect on one instance does not ghost the other (L14+L15)
    Given two emitters of kind Speaker named "desk-a" and "desk-b"
    When Connect for FactF to foreign Audience is applied only on desk-a
    Then desk-a ghost-suppresses same-name Audience
    And desk-b still declaration-fans-out to Audience "desk-b"

  Scenario: Deactivated scheduled neuron wakes without edge traffic (L23/L29)
    Given a neuron with a Schedule period armed
    And the activation is deactivated
    When time advances past NextDue
    Then the scheduled fact is heard with Cause pointing at the schedule record
```

```gherkin
# features/physics/ask.feature
Feature: Ask protocol
  Ask is not Emit; answers close pins; edge observes journals.

  Scenario: Emit of a question does not open an ask pin (L18)
  Scenario: Zero answerers terminal without horizon burn (L20)
  Scenario: Late reply after AskExpired does not run continuation (L21)
```

```gherkin
# features/physics/topology.feature
Feature: Topology
  Declaration is subscription; Connect is instance wire; refuse is loud.
```

```gherkin
# features/physics/concurrency.feature
Feature: Serialized turns
  Self-proxy dies; Reentrant refused; modules cannot open second wire.
```

### 3.3 Anti-trash test rules (enforced in review)

1. **Every test names a law id (Lxx) or FINAL LAW §** in DisplayName or Gherkin title.  
2. **No network, no LLM, no product brand** in L1/L2 (no Gmail strings required).  
3. **Journal oracle only** — no “we saw an event in memory list” doubles that skip commit.  
4. **One behavior per scenario** — split stories.  
5. **Delete scenario tests that only restate L2** without new expressibility.  
6. **No `Task.CompletedTask` assertions**; no synthetic NeuronIds from arithmetic.  
7. **Adversarial first:** throw, poison, dual drain, 17-hop, wrong Connect, missing answerer.

### 3.4 Quality scorecard (track to 100%)

| Axis | 40% today | 100% definition |
|---|---|---|
| Law coverage | ~40% of L01–L35 | 100% laws have green L1/L2 |
| Neuron fat | God partials | Verbs file <150 LOC; delivery/journal extracted |
| Typing | stringly via kinds OK; some multi-type dumps | Domain records; closed packs; no public primitive soup |
| Trash | Scenario narrative; dead Compact | Zero dead methods; zero narrative comments |
| UI alignment | Unspecified in Core plan | Documented module contract + one L3 demo feature |
| Multi-instance | Locus tests partial | L14+L15 abstract doubles green |
| Product attach honesty | Overclaim risk | README: physics gate vs expressibility pack |

---

## 4 · Execution model: “1000 agents” as work units (not thrash)

**Do not spawn 1000 concurrent processes.** That fights for the same tree and proves nothing.  
**Do** treat **1000 work units** as a backlog, executed in **batches of ≤50 units**, with **≤8 parallel agents per wave**, each owning a non-overlapping blast radius.

```text
20 waves × 50 units = 1000 units
Each wave: plan → implement → L1/L2 green on owning project → only then next wave
Root gate every 5 waves: full DigitalBrain.slnx Release
```

### Wave map (50 units each — adjust IDs as units complete)

| Waves | Units | Focus | Exit criteria |
|---|---|---|---|
| **1–2** | 1–100 | **Inventory + kill list** | Trash/dead API list; ABI public surface freeze; map Neuron LOC to target modules |
| **3–5** | 101–250 | **Extract Delivery** (Outbox, Drain, Policy+Depth) | L05–L08 green; Depth=16 live |
| **6–7** | 251–350 | **Extract Journal + Compaction** | L27 green; Compact called from drain |
| **8–9** | 351–450 | **Extract Topology pure routing** | L13–L17 unit+cluster; multi-instance L14 |
| **10–11** | 451–550 | **Slim Neuron + Turn** | Neuron verbs-only; no behavior change |
| **12–13** | 551–650 | **Concurrency / filters / durable gate** | L09–L12, L26, L33–L34 |
| **14–15** | 651–750 | **BDD harness + feature files** | Reqnroll or Gherkin pack; L1/L2 structure |
| **16–17** | 751–850 | **Ask / Schedule / Wake hardness** | L18–L24, L29–L32 adversarial |
| **18** | 851–900 | **Read interleave + bottlenecks docs/proofs** | L28; long-turn Read |
| **19** | 901–950 | **UI module contract sample + expressibility cull** | UiSurface sample outside Core; delete redundant scenarios |
| **20** | 951–1000 | **Final grill + scorecard 100%** | All Lxx green; fat gone; report |

Each **unit** is small enough for one agent:

- e.g. U-117: “Add `MaximumDepth` constant + private carry on said path.”  
- U-118: “Terminal DeliveryFailed when depth exceeded attempt 1.”  
- U-119: “BDD scenario L07 red then green.”  

Agents **must not** edit outside their unit’s files; run **smallest owning project** while iterating; full suite only at wave end.

---

## 5 · Refactor steps inside Core (technical)

### 5.1 Delivery extraction (highest confidence ROI)

1. Introduce `DeliveryPolicy.MaximumDepth = 16`.  
2. Carry depth **Core-private** on said outbox shape / envelope internal fields — **not** `SynapseMetadata`.  
3. Edge-born + schedule-born depth = 1; each hop +1.  
4. Exceed → `DeliveryFailed(..., "depth", attempts: 1)` on sender.  
5. Extract `Outbox` + `DrainCoordinator` from `Neuron.Dispatch` so FIFO/blockedTargets is unit-testable with a fake `ITransport`.  
6. Prove L05, L07, L08, L30.

### 5.2 Journal extraction

1. Keep durable key set closed; gate tests L12.  
2. Call `Compact` after cursor advance / idle drain.  
3. Floor = `min(cursor, oldest ask pin, …)`.  
4. Tallies outlive compaction (already intended).  
5. Prove L27 with forced large journal.

### 5.3 Topology pure functions

```csharp
// Conceptual pure API (names illustrative)
internal static class EmitRouter
{
    public static IReadOnlyList<NeuronIdEntry> Resolve(
        string emitterName,
        Type factType,
        Catalog catalog,
        IReadOnlyDictionary<string, IReadOnlyList<NeuronId>> connections);
}
```

Unit-test ghost rule, zero receivers, ask route, without silo.

### 5.4 Neuron slim

Order: extract → green → delete from Neuron partials → green.  
Never “rewrite Neuron in place” in one PR.

### 5.5 Typing / paradigm

| Prefer | Avoid |
|---|---|
| `sealed record` facts | open inheritance hierarchies for speech |
| `NeuronId` / `SynapseRef` | raw `(string,string,long)` tuples in public APIs |
| Enum-like closed Core packs | stringly outcome reasons without tests |
| File-scoped namespaces + folders | multi-type dumps (except closed vocabulary files) |
| Names over comments | narrative headers |

---

## 6 · How UI + multi-watch “just work” once Core is 100%

### 6.1 Multi-watch (Elon + Vlad + N)

```text
For each account name N:
  Session.Send(xaccount/N, WatchAccount)
  → XAccount/N Schedule(PollX)
  → PollX turn Emit(XPost) at Name=N
  → Behaviors / charts that declare INeuron<XPost> at Name=N hear
  → Or Connect from xaccount/N to chart/shared-dashboard for cross-locus
```

**Core guarantees:** isolation of journals, independent schedules, Connect locality, reactivation.  
**Module owns:** X API client, rate limits, cursor in `TState`.

### 6.2 Assistant form / button / chart

```text
Assistant Emit UiSurface(Form|Button|Chart) at chat Name
Shell INeuron<UiSurface> → Flutter edge projection (SSE/poll journal)
Owner submit → Session.Emit(OnSubmit synapse)
Domain neuron handles typed approve/submit
Optional Reply / Emit result + new UiSurface
```

**Core guarantees:** same bus, durable audit, unforgeable Source, multi-device same Name work journal.  
**Not Core:** pixel layout, RFW, theme.

### 6.3 What still will not “just work” even at Core 100%

| Piece | Owner |
|---|---|
| v1 Chat/Gmail/SF assemblies | Rewrite to Stage-1 (no GrainFactory / IDurable*) |
| LLM streaming tools | Multi-turn Ask protocol in AI module |
| OAuth | Host + module services |
| Hot install behaviors | Kernel Stage-3 |
| Shared-silo multi-owner | Deployment isolation Stage-1 |

---

## 7 · Risks and non-goals

### Risks

| Risk | Mitigation |
|---|---|
| Refactor changes behavior silently | Physics BDD red-first for each extract |
| Agents thrash same files | Hard blast-radius ownership per unit |
| Scenario pack blocks progress | Demote L3 from root gate |
| Depth breaks “valid” long pipelines | Depth=16 is storm bound; multi-hop product flows must not be 17 pure reaction hops without design |
| UI vocabulary freezes wrong widgets | Keep UI in module package; append-only records |

### Non-goals (this plan)

- Porting v1 modules  
- Flutter implementation  
- Real Gmail/SF network  
- Growing Abstractions beyond 4 types  
- In-neuron Send  
- Streams as n2n  
- 1000 concurrent subagents  

---

## 8 · Immediate next actions (before any “50 agents”)

1. **Freeze public ABI list** (script: public types in Abstractions+Core).  
2. **Implement L07 depth** + tests (single wave, human or 1–2 agents).  
3. **Wire Compact** + L27.  
4. **Add multi-instance abstract physics test** (L14) without product names.  
5. **Introduce BDD project skeleton** + first 5 features (delivery, topology, ask, schedule, concurrency).  
6. **Only then** open waves 3–5 extraction at batch 50.

---

## 9 · Definition of Done (Core Quality 100)

Check all boxes before claiming confidence for product attach:

- [ ] All L01–L35 green under L1/L2  
- [ ] `MaximumDepth` live; P18/P19 green  
- [ ] `Compact` called; L27 green  
- [ ] Neuron verb surface thin; delivery/journal/topology extracted  
- [ ] BDD features describe laws; scenarios demoted  
- [ ] Multi-instance Name isolation proven abstractly  
- [ ] Self-proxy, durable gate, dual answerer, unforgeable Source proven  
- [ ] UI path documented as module synapses; sample L3 optional  
- [ ] Root gate: `dotnet test DigitalBrain.slnx -c Release` = L1+L2 (+ optional L3)  
- [ ] README honesty: physics vs expressibility  

**One-line north star for this plan:**

> Core becomes a **small instruction set with brutal delivery physics**, proven by **BDD laws**, so any module that only uses the algebra — including multi-instance watches and UI-as-facts — is guaranteed by the bus, not by hope.

---

## 10 · Appendix — mapping previous confidence grill → this plan

| Previous recommendation | This plan |
|---|---|
| Depth first for product | Depth is **wave 3–5 first physics**, still required for 100% |
| UiEdge/Chat first for product demo | Product attach **after** L2 hardness; else demos lie |
| 50 scenarios as gates | **Demote** to L3 expressibility |
| Folder layout done | Necessary but **insufficient** (done on `28364053`) |
| v1 won’t plug in | Reaffirmed — rewrite modules to algebra |

---

*End of plan. Execute in waves; quote suite totals; never claim 100% on scenario green alone.*
