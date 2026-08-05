# 11 · Stage-1 proof catalog

Date: 2026-08-05. Status: **DEFINITIVE CATALOG** (catalog only — no implementation claim).  
Inputs: architecture grill `01`–`08`, `CORE-ARCHITECTURE.md`, `CORE-DESIGN.md` delivery
physics, `scenarios/` (50 stress cases), live Core surface (`DeliveryPolicy`,
`NeuronConcurrency`, `Catalog`, journal/outbox/schedule), existing tests
(`GreeterTests`, `PlannerDiaryTests`, `RestartSurvivalTests`).

**Bar:** each proof is a green-or-red assertion a suite can own. DisplayName prose is the
contract language (`[Fact(DisplayName = "…")]`). A product scenario going green does **not**
replace these. Stage-1 claims require the owning proof(s) green under
`DigitalBrain.Core.Tests` (cluster) or a pure unit host where marked.

---

## 0 · How to read this catalog

| Field | Meaning |
|---|---|
| **ID** | Stable proof id (`P01`…); never renumber for gaps — append |
| **DisplayName** | Exact test contract language (xUnit `DisplayName` style) |
| **Physics** | Law locked — cite grill / architecture, not product feature names |
| **Failure if missing** | What the brain does wrong when this proof is absent or red |
| **Cluster** | `unit` (no silo / pure reflection) · `cluster` (Orleans test host / `BrainTestClusters`) |
| **Scenarios** | Stress-set ids that **force** this law (not every scenario that merely uses it) |

### Clusters (execution homes)

| Tag | Home | When |
|---|---|---|
| **unit** | `DigitalBrain.Core.Tests` pure methods, catalog `Build`, concurrency refuse, policy constants, ABI inventory | No journal I/O, no drain race |
| **cluster** | `DigitalBrain.Core.Tests` + `DigitalBrain.Testing` fixture | Journal, Deliver, watermark, poison, schedule, ask |

### Coverage map (user-required surface → proofs)

| Required surface | Proof IDs |
|---|---|
| Greeter ask | P01 |
| Declaration fan-out | P02 |
| Connect + ghost rule | P03, P04 |
| Watermark redelivery | P05, P06 |
| Poison | P07, P08 |
| DeliveryFailed | P09, P10, P11 |
| Self-proxy throw | P12 |
| Schedule tick | P13, P14 |
| ScheduleFailed | P15 |
| AskExpired | P16 |
| Zero receivers | P17 |
| Depth / storm | P18, P19 |
| Journal read interleave | P20 |
| Catalog dual answerer boot fail | P21 |
| Fingerprint | P22 |
| No reentrancy attrs | P23, P24 |
| Durable key gate | P25 |
| *(supporting Stage-1 completeness)* | P26–P35 |

---

## 1 · Proofs

### P01 · Greeter ask round-trip

| | |
|---|---|
| **DisplayName** | A session ask reaches the greeter and the typed reply returns with the round trip in both journals |
| **Physics** | Ask is a distinct verb: routes to catalog answerer @ asker context; answer stamps `Answers`; edge `AskAsync` observes **session journal** (not same-turn RPC). Commit-before-dispatch; no neuron-awaits-neuron. |
| **Failure if missing** | Silent RPC veneer; answers without journal truth; dual path for edge vs neuron ask; reentrancy hang under fan-out. |
| **Cluster** | cluster |
| **Scenarios** | 07, 09, 15, 37, 50 (ask/answer spine); greeter is the minimal form of every multi-tool turn |
| **Notes** | Existing: `GreeterTests.GreetRoundTrip`. Assert via=`ask`, Source/Sequence, `Answers` ↔ ask position. |

---

### P02 · Declaration fan-out alone

| | |
|---|---|
| **DisplayName** | A planned day reaches the bodiless diary by declaration alone, with the planner as source |
| **Physics** | Emit resolution = declared `INeuron<T>` listeners @ **emitter.Name**; speaker names nobody; Source is Core-stamped emitter identity. |
| **Failure if missing** | Modules invent Send-loops or registries; broadcast becomes address-book orchestration; Source forge games. |
| **Cluster** | cluster |
| **Scenarios** | 02, 10, 21, 32, 36, 47, 50 (declaration = subscription) |
| **Notes** | Existing: `PlannerDiaryTests`. `to[]` carries `via: declared`. |

---

### P03 · Connect wires instance; ghost rule suppresses same-context kind

| | |
|---|---|
| **DisplayName** | After Connect, only the connected instance hears the fact and the same-context ghost kind does not |
| **Physics** | Topology = declarations ∪ connections; **ghost rule**: connection for fact F to kind K suppresses declared fan-out of F to K at that emitter. Snapshot into said entry at commit. |
| **Failure if missing** | Parallel ghost pipelines at account locus (north-star XPost → Behavior@accountName); dual truth of "who heard." |
| **Cluster** | cluster |
| **Scenarios** | 02, 05, 19, 36, 47, 49 (instance wiring / behavior mint) |
| **Notes** | Compose: emitter + declaring ghost kind + connected foreign-name instance. Edge `Send(Connect)`. |

---

### P04 · Disconnect restores declared route

| | |
|---|---|
| **DisplayName** | Disconnect removes the connection and declared same-context listeners receive again, with DeclaredRouteSurvives when applicable |
| **Physics** | Rewire is journaled speech; after Disconnect, ghost suppression lifts; topology honesty is a fact. |
| **Failure if missing** | Permanent mute after one-shot Connect; silent topology after unwire. |
| **Cluster** | cluster |
| **Scenarios** | 05, 19, 24, 26, 49 |
| **Notes** | Assert connection table empty for kind; subsequent Emit hits declared listener. |

---

### P05 · Journals and watermarks survive deactivation

| | |
|---|---|
| **DisplayName** | Journals and watermarks survive deactivation and the brain keeps planning |
| **Physics** | Durable journal + watermark are truth; activation is disposable; idle reminder / reactivation reloads committed snapshot only. |
| **Failure if missing** | "Memory brain" that forgets on deactivate; late work after 30d (sc46) impossible. |
| **Cluster** | cluster |
| **Scenarios** | 03, 04, 34, 39, 46, 48 (dormancy / recall) |
| **Notes** | Existing: `RestartSurvivalTests.SurvivesDeactivation`. |

---

### P06 · Redelivery swallowed by watermark; no dual reception

| | |
|---|---|
| **DisplayName** | A sender crash around delivery forces redelivery and the watermark swallows the duplicate |
| **Physics** | At-least-once + per-source watermark on `(Source, Sequence)`; duplicate → **silent success** (never throw); no dual truth on receiver. |
| **Failure if missing** | Double handler side effects; or throw-on-dup mints false `DeliveryFailed` on sender. |
| **Cluster** | cluster |
| **Scenarios** | 09, 35, 39, 41 (retry under flake); all durable pipelines |
| **Notes** | Existing: `RestartSurvivalTests.RedeliveredEmissionDoesNotDuplicate`. Fault-inject sender commit; third emission proves FIFO settlement. |

---

### P07 · Poison on failed turn commit; handler throw does not poison

| | |
|---|---|
| **DisplayName** | A failed WriteStateAsync poisons the activation while a handler throw leaves zero durable trace and does not poison |
| **Physics** | Post-handler staging; commit failure → poison + deactivate + reload; handler throw → clear memory only, zero durable line, sender retries. |
| **Failure if missing** | Dual truth (memory ≠ storage); v1 retraction maze; false poison on ordinary throw. |
| **Cluster** | cluster |
| **Scenarios** | 24, 30, 35, 41 (fault mid-workflow) |
| **Notes** | Two asserts in one proof (or split P07a/b if suite prefers). Sticky journal fault vs throw listener. |

---

### P08 · Poisoned activation refuses Deliver until fresh reload

| | |
|---|---|
| **DisplayName** | Deliver against a poisoned activation throws until deactivation reloads committed truth and redelivery converges |
| **Physics** | F12: never continue work on poisoned activation; write-landed-ack-lost handled by poison+reload + watermark. |
| **Failure if missing** | Half-staged durable structures accept more work; dual truth under ambiguous commit. |
| **Cluster** | cluster |
| **Scenarios** | 35, 41 |
| **Notes** | Pairs with P06/P07; assert no uncommitted emit observed on peer. |

---

### P09 · DeliveryFailed on sender after retry exhaustion

| | |
|---|---|
| **DisplayName** | After bounded retry exhaustion the sender journal holds exactly one DeliveryFailed for that synapse and receiver |
| **Physics** | Never silent loss; terminal outcome on **sender**; only Core drain mints transport `DeliveryFailed`. |
| **Failure if missing** | Lost facts with no audit; heal composition impossible (sc35). |
| **Cluster** | cluster |
| **Scenarios** | 35 (primary), 23, 31, 41 |
| **Notes** | Sticky-fault receiver; assert Attempts/Reason; exactly one terminal line per hole. |

---

### P10 · Permanent refuse terminals on attempt one

| | |
|---|---|
| **DisplayName** | Unknown kind or no handler fails terminal on attempt one with DeliveryFailed on the sender |
| **Physics** | Permanent vs transient class; no horizon burn for "will never succeed." |
| **Failure if missing** | 30-minute silent spin on typos and missing modules. |
| **Cluster** | cluster |
| **Scenarios** | 05, 35, 49 (bad Connect / missing kind) |
| **Notes** | Missing kind composition or non-declaring target. |

---

### P11 · DeliveryFailed is listenable for self-heal composition

| | |
|---|---|
| **DisplayName** | A heal neuron that declares INeuron of DeliveryFailed receives the Core-journaled failure and can Emit an alternate path |
| **Physics** | Failure is vocabulary; healing is composition, not try/catch inside one mega-agent; modules do not mint transport failure. |
| **Failure if missing** | Product reimplements error RPC; dual bus for "errors." |
| **Cluster** | cluster |
| **Scenarios** | 35 (primary), 23, 31 |
| **Notes** | Minimal HealRouter double; assert alternate said entry after terminal. |

---

### P12 · Proxied self-call throws (self-delivery rule)

| | |
|---|---|
| **DisplayName** | An outgoing grain call that targets the same activation throws naming the self-delivery rule |
| **Physics** | Self-delivery is direct method call only; outgoing filter converts self-proxy into loud fail (deadlock class). |
| **Failure if missing** | Drain↔Deliver reentrancy hang under schedule ticks / self Emit. |
| **Cluster** | cluster |
| **Scenarios** | 07, 30, 37, 46 (self paths) |
| **Notes** | Force proxy path in test double or filter unit with grain call context if fixture allows. |

---

### P13 · Schedule tick is an ordinary self-sourced turn

| | |
|---|---|
| **DisplayName** | A scheduled fact arrives as a heard entry after the period and the handler may Emit without a second bus |
| **Physics** | Modules schedule **facts**; Core owns wake; tick = ordinary turn (Cause = schedule position); no module `IRemindable`. |
| **Failure if missing** | Idle pollers die (sc31); nightly/morning intent has no physics (sc39/50). |
| **Cluster** | cluster |
| **Scenarios** | 02, 31, 39, 46, 50 |
| **Notes** | Controllable `TimeProvider` / `NeuronTime`. |

---

### P14 · Dormant schedule wakes without inbound traffic

| | |
|---|---|
| **DisplayName** | After deactivation with only a schedule armed, the neuron wakes and delivers the due fact without new edge speech |
| **Physics** | OutboxWakeup / reminder backstop while schedules exist; arm-before-commit; empty outbox ≠ no intent. |
| **Failure if missing** | sc46 30-day review never fires; conflates outbox wakeup with product scheduler. |
| **Cluster** | cluster |
| **Scenarios** | 39, 46, 50 |
| **Notes** | Deactivate; advance clock past due; await heard scheduled fact. |

---

### P15 · ScheduleFailed after consecutive tick failures

| | |
|---|---|
| **DisplayName** | After the schedule failure limit consecutive tick failures Core journals ScheduleFailed and removes the entry |
| **Physics** | Never silent infinite tick retry; terminal listenable outcome; then unschedule. |
| **Failure if missing** | Poison-loop timers; no heal surface for time half of the nervous system. |
| **Cluster** | cluster |
| **Scenarios** | 31, 35, 39 |
| **Notes** | Throw-on-tick listener; assert `ScheduleFailureLimit` behavior and entry gone. |

---

### P16 · AskExpired when answer never arrives

| | |
|---|---|
| **DisplayName** | When no answer arrives within AskHorizon the asker journals AskExpired and a late reply does not dispatch the open-ask continuation |
| **Physics** | Ask pins bound compaction floor and horizon; `AskHorizon = 2 × RetryHorizon`; late reply may journal but must not close a dead ask. |
| **Failure if missing** | Infinite pin of journal storage; edge Tasks hang forever; dual close races. |
| **Cluster** | cluster |
| **Scenarios** | 07, 15, 24, 29, 30, 37 |
| **Notes** | Controllable clock; answerer that never returns. |

---

### P17 · Zero-receiver Emit is legal

| | |
|---|---|
| **DisplayName** | Emit with no declared listeners and no connections journals a said entry with empty receivers and does not throw |
| **Physics** | Uninstalled modules / introspection; `to: []` legal; never silent throw for empty fan-out. |
| **Failure if missing** | Modules cannot announce during partial install; boot order becomes orchestration. |
| **Cluster** | cluster |
| **Scenarios** | 05, 44, 49 (partial catalog); 03, 34 (emit-for-audit) |
| **Notes** | Composition with sole emitter kind for that fact. |

---

### P18 · Depth budget terminals successful reaction storms

| | |
|---|---|
| **DisplayName** | A causal emission chain that exceeds maximum depth journals DeliveryFailed on the sender on attempt one with a depth reason |
| **Physics** | Storm control ≠ delivery identity; depth Core-private, stamped on said path; successful multi-kind cycles are not bounded by retry horizon alone. `MaximumDepth = 16`. |
| **Failure if missing** | sc36-class infinite Emit loops burn silo forever; heal cascades explode. |
| **Cluster** | cluster |
| **Scenarios** | 23, 35, 36 (primary storm), 37 |
| **Notes** | Chain of 17 listeners; assert attempt-1 terminal; no 30-min burn. Implement depth before claiming green (grill 07 gap). |

---

### P19 · Depth survives redelivery and is not module-resettable

| | |
|---|---|
| **DisplayName** | Depth carried on a said emission is preserved across redelivery and no module API can reset the budget |
| **Physics** | Depth not on public `SynapseMetadata`; not RequestContext authority; no `EmitAtDepthAsync`. |
| **Failure if missing** | Storm budget restart under retry; modules game the bound. |
| **Cluster** | cluster (+ unit surface scan) |
| **Scenarios** | 35, 36 |
| **Notes** | Fault mid-drain; ABI scan for depth verbs. |

---

### P20 · Committed journal read interleaves a long turn

| | |
|---|---|
| **DisplayName** | A committed journal read completes while another activation holds a long non-mutating wait without queuing forever behind the turn |
| **Physics** | Surgical `[AlwaysInterleave]` + `[ReadOnly]` on Core **read** surface only; Deliver/drain never interleave; modules refuse both attributes. |
| **Failure if missing** | Edge/UI stuck behind multi-minute model turns (sc29); or concurrent mutation if attr leaks to modules. |
| **Cluster** | cluster |
| **Scenarios** | 03, 04, 10, 29, 34, 47 |
| **Notes** | Long-handler double + concurrent `Brain.ReadAsync`. |

---

### P21 · Dual answerer kinds fail catalog boot

| | |
|---|---|
| **DisplayName** | Catalog build refuses two answerer kinds for the same question type with an asserted boot failure |
| **Physics** | At most one answerer kind per question; overhearers use `INeuron<T>` without answering; bare convention undecidable. |
| **Failure if missing** | Duplicate replies; non-deterministic Ask route; ghost answerers. |
| **Cluster** | unit |
| **Scenarios** | 07, 12, 15, 37, 49 |
| **Notes** | `Catalog.Build` with two `IAnswers<Q,R>` types. |

---

### P22 · Catalog fingerprint is stable and silo-visible

| | |
|---|---|
| **DisplayName** | Two identical type sets produce the same catalog fingerprint and a different hear or answer row changes it |
| **Physics** | Fingerprint = SHA-256 of sorted topology rows; heterogeneous silos must not form one brain. |
| **Failure if missing** | Silent split topology across silos; N+1 install invisible (sc44/49). |
| **Cluster** | unit |
| **Scenarios** | 05, 24, 26, 44, 49 |
| **Notes** | Stage-1 proves hash purity; cluster join refusal may be later seam (document if not yet wired). |

---

### P23 · Reentrant and MayInterleave neuron attributes fail activation contract

| | |
|---|---|
| **DisplayName** | A neuron type annotated Reentrant or MayInterleave fails RequireSerializedTurns with a message naming serialized turns |
| **Physics** | Serialized mutating turns; journal order and watermark progression assume one turn at a time. |
| **Failure if missing** | Drain↔Deliver deadlock class returns; dual watermark races. |
| **Cluster** | unit |
| **Scenarios** | 07, 30, 37 (concurrency pressure) |
| **Notes** | `NeuronConcurrency.RequireSerializedTurns` on annotated test types. |

---

### P24 · Module AlwaysInterleave, IRemindable, and extra grain interfaces fail activation

| | |
|---|---|
| **DisplayName** | A neuron that declares AlwaysInterleave on a module method, implements IRemindable, or adds a second IAddressable grain interface is refused at the concurrency contract |
| **Physics** | Orleans power sealed in Core; modules schedule facts; only Core transport wire. |
| **Failure if missing** | Second bus; timer-swallowing; module reminder maze (v1 countdown trap). |
| **Cluster** | unit |
| **Scenarios** | 31, 39, 46 (time seal); all module installs |
| **Notes** | Three refuse shapes can be one theory method with cases. |

---

### P25 · Durable key gate admits only Core journal keys

| | |
|---|---|
| **DisplayName** | Registering a durable state name outside NeuronJournal CoreKeys fails at the gated state manager |
| **Physics** | One batch commit; module state = `TState` slot only; no module-minted `IDurable*`. |
| **Failure if missing** | Unenlisted durable mutation; dual truth; atomicity hole. |
| **Cluster** | unit (gate) / cluster (if only visible at activation) |
| **Scenarios** | 03, 34, 48 (journal authority); all durable turns |
| **Notes** | Assert against `NeuronJournal.CoreKeys` closed set. |

---

### P26 · Ask with zero answerers fails immediately

| | |
|---|---|
| **DisplayName** | Ask when the catalog has no answerer for the question journals DeliveryFailed with a no-answerer reason without burning the retry horizon |
| **Physics** | Permanent topology fault on attempt 1; Ask ≠ Emit of question. |
| **Failure if missing** | Horizon burn on missing answerer; edge hangs until AskExpired only. |
| **Cluster** | cluster |
| **Scenarios** | 07, 12, 37, 49 |
| **Notes** | Distinct from P16 (answerer present, silent). |

---

### P27 · Emit of a question does not take the answerer role

| | |
|---|---|
| **DisplayName** | Emit of a question-shaped fact reaches overhearers only and does not register an open ask or force the answerer path |
| **Physics** | Ask is protocol (pin + answerer route); Emit is announce. |
| **Failure if missing** | Dual fire paths; accidental open asks; cardinality chaos. |
| **Cluster** | cluster |
| **Scenarios** | 07, 21, 37 |
| **Notes** | Answerer + overhearer composition; assert no ask pin / no answerer invocation. |

---

### P28 · ConnectionRefused on invalid Connect leaves table untouched

| | |
|---|---|
| **DisplayName** | Connect to a non-declaring or illegal target journals ConnectionRefused and does not mutate the emitter connection table |
| **Physics** | Local catalog validation at handling; loud topology; virtual-actor silent worlds refused for Connect. |
| **Failure if missing** | Typos mint parallel dead journals; heal cannot see refusal. |
| **Cluster** | cluster |
| **Scenarios** | 05, 19, 35, 49 |
| **Notes** | Edge Send Connect; assert table + refused fact. |

---

### P29 · Per-receiver FIFO holds across retry and dual drain

| | |
|---|---|
| **DisplayName** | When a receiver fails transiently on sequence N later sequences to the same receiver wait until N settles or a committed DeliveryFailed opens the hole |
| **Physics** | blockedTargets FIFO; abandonment barrier: commit terminal hole **before** unblocking; no parallel out-of-order watermark advance. |
| **Failure if missing** | Seq 5 handled before 4; crash redelivers 4 after 5 → dual effects or silent loss of order. |
| **Cluster** | cluster |
| **Scenarios** | 09, 21, 23, 35, 39 |
| **Notes** | Sticky then recover; third emission proves order. |

---

### P30 · Source is Core-stamped and unforgeable from verbs

| | |
|---|---|
| **DisplayName** | Said entry Source equals the emitting neuron id and no public module API accepts Source Sequence Cause or Answers as verb parameters |
| **Physics** | Provenance is structure; public metadata is identity for **readers**; authors pass bodies only. |
| **Failure if missing** | Prompt-injection "I am Source=assistant" becomes true; multi-owner forge; audit lies. |
| **Cluster** | cluster (+ unit ABI inventory) |
| **Scenarios** | 25, 27, 42, 43 (primary adversarial) |
| **Notes** | Hostile body round-trip still keeps ingress Source (pair with P31). |

---

### P31 · Hostile body text never opens a second wire or changes Source

| | |
|---|---|
| **DisplayName** | A fact body containing injection strings is delivered and journaled as data without altering Source or bypassing the turn pipeline |
| **Physics** | Core is injection-agnostic; body is opaque JSON; no eval path. |
| **Failure if missing** | Core becomes a broken policy engine; or worse, elevates text to dispatch. |
| **Cluster** | cluster |
| **Scenarios** | 43 (primary), 36 |
| **Notes** | Policy modules own trust tags; this proof is Core-only. |

---

### P32 · Two context names isolate journals under declaration fan-out

| | |
|---|---|
| **DisplayName** | Parallel sessions at different context names never deliver declared fan-out into each other's journals |
| **Physics** | Within-brain isolation by `NeuronId` locus; declaration fans at emitter **Name** only; Stage-1 multi-owner is deployment, not OwnerId. |
| **Failure if missing** | Cross-talk between desks; false multi-tenant story. |
| **Cluster** | cluster |
| **Scenarios** | 13, 25, 27, 42 |
| **Notes** | sc25/27 product multi-owner **not** claimed green by this alone. |

---

### P33 · Compaction never drops below cursor or oldest ask pin

| | |
|---|---|
| **DisplayName** | Soft compaction retains every unsettled said position and every open ask pin even when soft entry and byte targets are exceeded |
| **Physics** | Hard floor = min(cursor, oldest ask pin, floorLimit); soft targets subordinate; no v1 unconditional feed eviction. |
| **Failure if missing** | Drop unsettled outbox lines; redelivery impossible; or unbounded pin if inverted. |
| **Cluster** | cluster |
| **Scenarios** | 03, 24, 29, 34, 48 |
| **Notes** | Force many said entries + open ask; compact; assert floor survivors. |

---

### P34 · DeliveryPolicy constants match ratified bounds

| | |
|---|---|
| **DisplayName** | DeliveryPolicy exposes MaximumAttempts one thousand, RetryHorizon thirty minutes, AskHorizon twice RetryHorizon, ScheduleFailureLimit five, and MaximumDepth sixteen when depth ships |
| **Physics** | One home for bounds; never silent infinite retry; never unbounded pin. |
| **Failure if missing** | Drift between docs and runtime; accidental product knobs. |
| **Cluster** | unit |
| **Scenarios** | 31, 35, 39, 46 (bound consumers) |
| **Notes** | Equality tests only; depth constant lands with P18. |

---

### P35 · Session directed Send does not fan out to declarations

| | |
|---|---|
| **DisplayName** | Session Send to an exact neuron id journals a single receiver and does not deliver to other declared listeners of that fact type |
| **Physics** | Stage-1 in-neuron Send absent; edge Send = exact id, no declaration/connection fan-out; Connect delivery path. |
| **Failure if missing** | Directed edge speech becomes accidental broadcast; Connect undeliverable as precise wire. |
| **Cluster** | cluster |
| **Scenarios** | 05, 13, 19, 42, 47 |
| **Notes** | Compose two listeners; Send to one; assert other silent. |

---

## 2 · Traceability matrix (scenario → proofs)

Compact force map — scenario needs these proofs green for Core to **express** it safely.
Product modules remain non-Core.

| Scenarios | Forced proofs |
|---|---|
| 01–02, 10, 47 | P02, P03, P17, P35 |
| 03–04, 34, 48 | P05, P20, P30, P33 |
| 05, 19, 49 | P03, P04, P21, P22, P28 |
| 06, 38 | P01, P20, P35 (edge observation) |
| 07, 15, 30, 37 | P01, P16, P23, P26, P27 |
| 09, 21, 23 | P06, P09, P29 |
| 12, 44 | P21, P22 |
| 13, 25, 27, 42 | P32, P35 (multi-owner product later) |
| 24, 26 | P07, P16, P22 |
| 28 | Streams-as-ingress is non-claim for n2n; P02 still holds |
| 29 | P16, P20, P33 |
| 31, 39, 46, 50 | P13, P14, P15, P24, P34 |
| 35 | P09, P10, P11, P15, P18 |
| 36 | P18, P19, P31 |
| 41 | P06, P07, P08, P09 |
| 43 | P30, P31 |
| 45 | Stateless worker non-neuron later; P24 forbids SW **on** neurons |
| 50 | P01, P02, P13, P14 |

---

## 3 · Grill / physics index

| Physics law | Proofs |
|---|---|
| Commit-before-dispatch; no same-turn reply truth | P01, P06, P12 |
| No neuron-awaits-neuron / serialized turns | P12, P23, P24 |
| Declaration ∪ Connect + ghost rule | P02, P03, P04, P28 |
| Journal-as-outbox; at-least-once + watermark | P05, P06, P29 |
| Poison; no mid-handler durable mutation | P07, P08 |
| DeliveryFailed / AskExpired / ScheduleFailed family | P09–P11, P15, P16, P26 |
| Schedule facts; Core wake only | P13, P14, P15, P24 |
| Depth storm budget | P18, P19, P34 |
| Catalog cardinality + fingerprint | P21, P22, P26, P27 |
| Durable key seal + Source integrity | P25, P30, P31 |
| Read interleave surgical only | P20, P24 |
| Locus isolation; edge Send exact | P32, P35 |
| Compaction floor; policy bounds | P33, P34 |
| Zero receivers legal | P17 |

---

## 4 · Explicit non-proofs (do not ship as Stage-1 Core green)

| Tempting claim | Owner instead |
|---|---|
| Shared-silo multi-tenant hard isolation | Kernel / product (deployment isolation Stage 1) |
| Prompt injection "solved" / model tool denial | Policy + assistant modules |
| Stream late-join causal history | Forbidden; journal read |
| In-neuron Send | Deferred until Kernel consumer |
| Hot-reload without redeploy / marketplace ALC | Kernel Revision |
| Heal always finds alternate channel | HealRouter module policy |
| Cron / TZ / countdown product UX | Time module on Core Schedule |
| Orleans Transactions multi-grain ACID | Non-goal |
| Legal hold completeness | Compliance module + Kernel |

---

## 5 · Suite ownership & ordering

### Suggested file map (when implementing — not a claim)

| Suite / file (suggested) | Proofs |
|---|---|
| `GreeterTests` | P01 |
| `PlannerDiaryTests` / `DeclarationTests` | P02, P17 |
| `ConnectionsTests` | P03, P04, P28 |
| `RestartSurvivalTests` | P05, P06 |
| `PoisonTests` | P07, P08 |
| `DeliveryFailedTests` | P09, P10, P11, P26, P29 |
| `SelfDeliveryFilterTests` | P12 |
| `ScheduleTests` | P13, P14, P15 |
| `AskHorizonTests` | P16, P27 |
| `DepthStormTests` | P18, P19 |
| `JournalReadInterleaveTests` | P20 |
| `CatalogBootTests` | P21, P22 |
| `NeuronConcurrencyTests` | P23, P24 |
| `DurableKeyGateTests` | P25 |
| `ProvenanceTests` | P30, P31 |
| `LocusIsolationTests` | P32 |
| `CompactionTests` | P33 |
| `DeliveryPolicyTests` | P34 |
| `SessionSendTests` | P35 |

### TDD order (fail first, minimal fan-out)

1. **Boot seals:** P21–P25, P34 (cheap unit; refuse wrong shapes).  
2. **Happy bus:** P01, P02, P17, P35.  
3. **Topology:** P03, P04, P28.  
4. **Durability:** P05, P06, P29, P33.  
5. **Failure family:** P07–P11, P16, P26.  
6. **Time:** P13–P15, P14.  
7. **Storm + seal residual:** P12, P18–P20, P27, P30–P32.

### Root gate rule

Never claim Stage-1 Core complete on filtered project runs alone. Root
`dotnet test` on the Core solution (or package gate that includes every proof project)
is the completion bar. Existing greeter / diary / restart proofs count as **P01, P02,
P05, P06** when their DisplayNames and asserts still match this catalog.

---

## 6 · Count & ratification

| | |
|---|---|
| **Proof count** | **35** (target band 25–40) |
| **Unit** | P21–P25, P34 (+ ABI half of P19/P30) |
| **Cluster** | P01–P20, P26–P33, P35 |
| **Already green in tree (subset)** | P01, P02, P05, P06 — verify DisplayName/assert still match |
| **Known implement-before-claim gap** | P18/P19/P34 depth constant (grill 07); fingerprint **join refuse** may lag pure hash P22 |

**RATIFIED as catalog 2026-08-05:** this file is the Stage-1 proof inventory. Adding a
proof requires a new id; deleting one requires a grill note that the physics is obsolete
or covered by a stricter replacement. Implementation lives in tests, not in this document.

---

*Prefer delete. One mechanism per job. Journals are the audit; proofs lock physics, not product demos.*
