# 04 · Modules, catalog, behaviors, N+1, hot-reload, grain versioning

Date: 2026-08-05. Status: **RATIFIED** package boundaries (Core vs Kernel vs Module).  
Method: brainstorm + self-grill. Inputs: `CORE-ARCHITECTURE.md` §§5–11 (esp. G18, G23, G24),
`CONTEXT.md`, `Catalog.cs`, scenarios 05 / 19 / 24 / 25 / 26 / 27 / 36 / 44 / 49, `OS.md`
growth model, `CORE-RESEARCH.md` Revision concept.

Companion physics: thin Abstractions (4 types), journal = causal bus, no neuron-awaits-neuron,
catalog-owned declaration topology, streams = edge only.

---

## 0 · Verdict in one page

| Question | Stage-1 answer | Later (Kernel) |
|---|---|---|
| What is a module? | Compiled assembly: sealed synapses + `Neuron` kinds + private helpers/DI | Same shape; may arrive via content-addressed artifact, not only AppHost refs |
| How is catalog built? | One pure reflection pass `Catalog.Build(neuronTypes)` at silo boot | Same pure function; input set becomes epoch-versioned projection of journaled activations |
| Fingerprint? | SHA-256 of sorted `(kind hears\|answers\|continues fact)` rows; silos must match | Fingerprint **is** the epoch id; cluster agrees on epoch, not on "whatever is loaded" |
| Behavior = neuron? | **Yes.** No second runtime, VM, or RPC veneer | Kernel authors/compiles/gates; Core only dispatches ordinary neurons |
| Hot-reload / marketplace N+1? | **Deploy-shaped** (blue-green or stop-the-world catalog rebuild) — do not fake | Revision lifecycle + catalog epoch swap with drain/cutover policies |
| Multi-owner? | **One owner per deployment** | Product isolation remains deployment-primary; tenant keys not Core address space |

**Never clog Core:** Roslyn, collectible ALC, marketplace, NL prompts, approval UI, signed
package store, BDD product gate, sandbox policy product surface — **Kernel**. Core keeps
catalog fingerprint/epoch *seam*, identical dispatch, kind-collision refusal, journal physics.

---

## 1 · What is a module in Stage 1?

### Definition (ratified)

A **module** is an independently shipped **vocabulary + handlers**:

1. Sealed `Synapse` / `Synapse<TReply>` records (facts the brain can name),
2. `Neuron` / `Neuron<TState>` classes declaring `INeuron<>` and optionally answerer forms
   (`INeuron<,>` / Core `IAnswers<,>` as the composition evolves — see Abstractions grill),
3. Private helpers and host-registered services (`HttpClient`, model clients, stores).

It is **not** a plugin registration facade, not a service-collection mega-interface, not a
workflow package.

```text
modules/foo/
  FooFacts.cs          # sealed records : Synapse
  FooWorker.cs         # : Neuron, INeuron<FooFact>, …
  (optional DI types)  # never GrainFactory, never IRemindable, never extra grain ifaces
```

### Composition (Stage 1 host)

```csharp
builder.UseOrleans(silo => silo.AddDigitalBrain(
    typeof(GmailIngress).Assembly,
    typeof(Tasks).Assembly,
    typeof(Chat).Assembly));
```

- Discovery = **explicit type set** (assembly convenience over `IEnumerable<Type>`).
- No `IModule` in Core. No runtime plugin registry on the emit path.
- Kind = `NeuronId.KindOf(type)` = lowercased class name; collisions **fail boot**.
- Module assemblies reference `DigitalBrain.Abstractions` + `DigitalBrain.Core` (for `Neuron`
  base and Core synapses they listen to). Modules **never** import Orleans types in public
  contracts.

### Boot-enforced module rules (already in catalog / hosting)

| Rule | Failure mode |
|---|---|
| Sealed concrete facts only | Boot refuse abstract / open generics / unsealed |
| Exact-type dispatch (no wildcard base listeners) | Boot refuse `INeuron` over abstract synapse |
| At most one answerer kind per question type | Boot refuse dual answerer |
| Kind is listener **or** answerer for a given question, never both | Boot refuse dual claim |
| Reserved Core kinds (`Connect`, `Disconnect`, `Schedule`, `Unschedule`) not hijacked as module listeners | Boot refuse |
| Answerer must override Handle/Answer (dead `null` forever claim banned) | Boot refuse via `GetInterfaceMap` |
| `TState` default-constructible + codec-round-trippable | Hosting validation |
| No extra grain interfaces on neuron types | Hosting / filters whitelist |

### Stage 1 non-goals for "module"

- Dynamic load from disk without redeploy.
- Per-module grain version negotiation in the ABI.
- Module-owned second DI container.
- `System.Type` / AQN in durable addresses (kinds are strings forever).

---

## 2 · Catalog: build, fingerprint, silo agreement

### Build (pure function of types)

`Catalog.Build(IReadOnlyList<Type> neuronTypes)` is the **sole** topology derivation for
declaration routing. One reflection pass. Held **per-silo in DI**, never static (test clusters
compose independently).

What it produces:

| Map | Use |
|---|---|
| `kinds` | kind string → neuron CLR type (activation / grain type resolution) |
| `factKinds` / `factKindNames` | fact kind ↔ type (journal body codec) |
| `listeners` | fact type → set of hearing neuron kinds (Emit fan-out) |
| `answerers` | question type → single answerer kind (Ask route) |
| `continuations` | (neuronKind, question) claims for ask-guard / Answer reconstruction |
| `shapeFingerprints` | per-fact property-name hash (drift guard before Answer rehydrate) |
| `Fingerprint` | cluster agreement token |

Core vocabulary facts are seeded so reserved names collide loudly and Core can journal
`Connect` / `DeliveryFailed` / … without a module declaration.

### Fingerprint algorithm (Stage 1)

```text
rows = ordered unique lines:
  "{kind} hears {factKind}"
  "{kind} answers {factKind}"
  "{kind} continues {questionKind}"
payload = join(rows, '\n') with ordinal sort
Fingerprint = hex(SHA256(UTF8(payload)))
```

**Meaning:** two silos that disagree on who hears/answers what are **not the same brain
composition**. Join refusal is the contract (announcement exists today; cluster-join hard
refuse is the deferred hosting piece — stated, not silent).

### Dual-derivation ban (load-bearing)

Predecessor death mode: Roslyn source-gen string table **and** runtime reflection of the same
declarations → silent skew. Rule:

- **One derivation:** reflection catalog at composition boundary.
- When Revision lands, the catalog becomes a **projection of journaled activation facts**;
  the pure `Build` function still runs over the **activated type set** for that epoch —
  still one function, different input authority.
- Never: emit-path registry repair, never: second table maintained by attributes.

### Shape fingerprint (orthogonal, per fact type)

Separate from composition fingerprint: hash of camel-cased public property names. Used so a
drifted question shape never rehydrates into a silently defaulted `Answer` dispatch view.
Journals outlive code; unknown kinds read with `Body = null` — shape guard is for
**known-but-drifted** types.

---

## 3 · Behavior = neuron? Activation without clogging Core

### Decision

**A behavior is a neuron** — not a script VM, not a workflow engine, not
`await brain.CallAsync<Greeter>(...)`.

From `CONTEXT.md`:

> Behavior: A neuron created or installed by Kernel to express an owner-requested capability.
> It is not a separate execution abstraction.

Author surface (product intent, scenarios 05 / 19 / 36):

```csharp
// Owner-authored C# — same ABI as first-party modules
sealed class VipEmailToTask : Neuron, INeuron<EmailReceived>
{
    public async Task HandleAsync(EmailReceived mail, CancellationToken ct)
    {
        // Ask / Emit / Reply only — never GrainFactory, never neuron-await-neuron
    }
}
```

### Lifecycle split (who owns what)

| Stage | Owner | Core involvement |
|---|---|---|
| Author (NL or C#) | Kernel / BehaviorStudio | None |
| Compile (Roslyn) | Kernel / BehaviorHost | None |
| Gate (generated test in collectible ALC) | Kernel | None |
| Content-address artifact store | Kernel | None |
| Activate / deactivate / supersede | Kernel journals lifecycle facts; Core applies **catalog epoch** | Catalog epoch hook + dispatch |
| Run turns | **Core only** | Ordinary Deliver → handle → commit → outbox |
| Rewire instances | Facts: `Connect` / `Disconnect` | Core connection tables |
| Rollback | Kernel points at prior epoch | Core swaps fingerprint/epoch |

### How activation works without clogging Core

**Stage 1 (today's honest shape):** "install behavior" = ship/compile into the **composition
type set** and redeploy (or blue-green silos with matching fingerprint). Speakers that only
`Emit` need **zero** code change when a new listener kind appears — that is the N+1 property
of **declaration fan-out**, not of a hot registry.

**Stage 3 (Kernel Revision — designed, not built):**

```text
RevisionProposed → Built → Evaluated (ALC test green) → Activated → Retired
catalog_epoch_N  = Build(types_of_epoch_N)
silos agree on epoch_N fingerprint
new activations resolve kinds via epoch_N maps
```

Core's **only** new surface for that future:

1. **Catalog as epoch-addressable value** (immutable snapshot + fingerprint id),
2. **Atomic pointer** "current epoch" for resolve/dispatch,
3. **Refusal** of kind collision / dual answerer at epoch build (same refusals as boot),
4. Identical physics for compiled and script-activated kinds (no dual dispatch path).

Core does **not** grow: Roslyn host, ALC unload policy, marketplace HTTP, permission manifests
as product UI, capability approval cards. Those are Kernel modules/neurons speaking facts.

### Grill: "scripts need RPC ergonomics"

**Attack:** Authors want `await CallAsync` / `brain.GetGrain`.  
**Defense:** Reentrancy deadlock class + dual bus + non-journaled coordination.  
**Decision:** Scripts program **on** synapses. Continuations are later turns
(`INeuron<TReply>` / `TState`). No RPC veneer in Core (G18).

### Grill: "BehaviorHost is a second runtime"

**Attack:** Isolated worker process implies a second execution model.  
**Defense:** Process isolation is **hosting** (sandbox blast radius), not a second ABI.
Once loaded, kinds are still `Neuron` subclasses resolved by catalog. Host process may die;
journals + outbox remain the truth.  
**Decision:** BehaviorHost = Kernel deployment unit. Core API surface unchanged.

### Grill: "dynamic kinds clog grain versioning"

**Attack:** Every script revision needs a new Orleans grain interface version.  
**Defense:** Durable address is `(kind string, name/locus)` — **revision is not part of the
address** (otherwise every upgrade mints a new brain — CORE-RESEARCH). Grain implementation
generation may roll under Orleans versioning; journal identity stays kind/name.  
**Decision:** Kind names stable across revisions; epoch/generation fences in-flight work,
not new NeuronIds.

---

## 4 · Core hooks for epoch / revision **without** marketplace

Marketplace (scenario 49) is product. Core must not implement a store. Core **must** leave a
seam so Kernel can land Revision without rewriting physics.

### Stage-1 hooks that already exist (or are one rename away)

| Hook | Present shape | Why it is the epoch seed |
|---|---|---|
| `Catalog.Build(types)` | Pure, testable | Epoch materializer |
| `Catalog.Fingerprint` | SHA-256 of declaration rows | Epoch identity |
| `CatalogFingerprint` DI + lifecycle announce | Silo start log | Future: join gate input |
| Boot refusals | Kind / answerer / reserved / shape | Same rules on every epoch build |
| `Connect` / `Disconnect` | Journaled emitter tables | Instance rewiring **without** code deploy |
| Zero-receiver Emit | Legal `to: []` | Missing module is visible, not a crash |
| Kind-not-found delivery | Terminal `DeliveryFailed` on first attempt | Tables outlive code; no silent poison retry |
| Journal bodies by kind string | Codec + `Body = null` if unknown | Journals outlive epochs |

### Explicit non-hooks (do not add "for marketplace")

| Temptation | Why rejected |
|---|---|
| `IModuleRegistry` grain on emit path | v1 Subscribe / timeout retract death |
| Hot-swap `Catalog` fields without epoch id | Dual derivation + dual answerer races |
| Per-message "handler version" on Abstractions metadata | ABI bloat; revision ≠ envelope |
| Core-owned package blob store | Kitchen-sink; Kernel concern |
| `InstallModule` on `Neuron` base | Clogs Core; no Stage-1 consumer in Core tests |

### Drain / cutover (scenarios 24 / 26) — ownership

| Policy | Who decides | What Core must guarantee |
|---|---|---|
| Drain | Kernel activation policy | In-flight turns on generation N finish under N's handlers; serialized turns; answers not delivered to wrong generation |
| Cutover | Kernel | Open asks get journaled terminal (`DeliveryFailed` / abandoned vocabulary — Kernel facts); no silent drop |
| Fence | Core + storage | Journal ETag / single-writer; stale activation cannot commit |

Stage 1: **no hot epoch**. Document scenarios 24/26/49 as Kernel+Core epoch work. Faking
hot-load now recreates dual derivation (G24).

### Rolling module grain version (scenario 44)

Orleans grain versioning / placement for **implementation** rollouts is Core **hosting**
power (silo deploy), not marketplace. Compatibility backbone = journals + outbox +
string kinds + sealed append-only vocabularies. Schema break → `DeliveryFailed` /
domain `SchemaRejected`, not corrupt journal. Two versions answering the same Ask kind
in one catalog epoch remains **boot/epoch fail**.

---

## 5 · Multi-owner: deployment isolation vs tenant keys

### Language (`CONTEXT.md`)

| Term | Meaning |
|---|---|
| **Owner** | The person/org whose memory and behavior the brain is |
| **Brain** | Durable body for one owner |
| **Deployment** | One isolated instance of a brain |

_Avoid:_ Tenant, multi-tenant keys in Core addresses.

### Stage-1 decision (ratified, G23)

**One owner per deployment.** Isolation = separate AppHosts / storage / catalogs / network
edges — not `OwnerId` inside `NeuronId`.

Why not tenant keys in Core now:

1. Dual identity schemes (kind/name **and** owner) tempt stream namespaces, filters, and
   journals to fork "who am I?" truth.
2. Virtual actors make typo'd foreign names "succeed" into parallel worlds; owner partitions
   without deployment boundaries become another silent-success class.
3. No Core test consumer requires shared-silo multi-owner today; scenarios 25/27 are product
   pressure, not Stage-1 Core physics.

### How isolation scenarios still pass

| Scenario claim | Stage-1 mapping |
|---|---|
| Two owners never mix journals | Two deployments / two storage partitions / two fingerprints |
| Same kind names (`chat/desk`) | Same kind strings in different brains — different grain key spaces by deployment |
| Malicious cross-connect | Cannot address foreign deployment; within one brain, `ConnectionRefused` for bad kinds |
| Shared module **code** | Same assemblies, different host processes — not shared activations |

### What Kernel/edge may add later (not Core ABI)

- IdP principal → deployment routing (which brain URL/silo set).
- Break-glass ops elevation with audit (product security module).
- **Only if proven:** owner partition inside a mega-host — then as Kernel capability filters +
  grain key encoding owned by Kernel, **not** as a fourth field on Abstractions `NeuronId`.

### Grill: "shared silo is cheaper"

**Attack:** One cluster, many owners, filter by RequestContext owner header.  
**Defense:** Filters that trust forgeable headers are theater; real isolation needs
non-forgeable binding + storage isolation. Deployment isolation is the cheap correct default.  
**Decision:** Stage 1 deployment isolation. Revisit only with a Kernel identity design and
proof that shared-silo multi-owner does not dual-derive topology.

---

## 6 · RATIFIED package boundaries

```text
DigitalBrain.Abstractions     # ZERO deps — ABI only
  Synapse
  INeuron<in TSynapse>        # (answerer form placement per Abstractions grill)
  NeuronId(Kind, Name)
  SynapseMetadata             # identity only (source, sequence, timestamp)

DigitalBrain.Core             # Orleans body; runtime physics
  Neuron / Neuron<TState>
  Catalog (+ Fingerprint / future Epoch snapshot)
  Journal / outbox / watermark / DeliveryFailed
  Connect·Disconnect·Schedule vocabulary
  Ask protocol / open asks
  Brain + Session edge
  Grain call filters, timers, reminders, placement seams
  Streams: edge adapters ONLY
  Hosting: AddDigitalBrain(assemblies|types)
  # HOOKS ONLY: catalog epoch pointer, fingerprint join gate
  # FORBIDDEN: Roslyn, ALC product host, marketplace, OwnerId ABI,
  #            IModule registry grain, script RPC, IDigitalBrain kitchen sink

DigitalBrain.Testing          # real clusters, clocks, journal asserts
  Composes explicit type sets → independent catalogs

DigitalBrain.Kernel           # LATER — deployable OS on Core
  Behavior authoring / Studio edge
  Roslyn compile + collectible ALC gate ("no neuron without green test")
  Content-addressed artifact store
  Revision* lifecycle facts + activation policy (drain|cutover)
  Capability reification / owner tap for privileged topology
  Marketplace client (optional product) → produces RevisionProposed, not Core APIs
  Multi-deployment routing / IdP (optional)

modules/*                     # product vocabularies
  Neurons + synapses + private IO
  Reference Abstractions + Core
  Never Orleans in public module contracts
  Never install/activate other modules (Kernel)

samples/*                     # demos; not ABI consumers that force Core types
```

### Boundary tests (reject PRs that fail these)

1. **Does Core gain a type whose only consumer is Behavior Studio?** → move to Kernel.
2. **Does a module import Orleans?** → fail.
3. **Does activation mutate catalog without a new fingerprint/epoch?** → fail (dual derivation).
4. **Does a behavior get a call path that is not Deliver/journal?** → fail (causal leak).
5. **Does NeuronId gain OwnerId / TenantId / RevisionId?** → fail Stage 1; force Kernel design
   doc first.
6. **Is N+1 install claimed green without either (a) redeploy fingerprint or (b) real epoch
   machinery?** → claim is false; do not ship the lie.

---

## 7 · Self-grill log (this topic)

### M1 · Module needs `IModule.Register(IServiceCollection)`

**Attack:** DI and "capabilities" need a module entrypoint.  
**Defense:** Hosting already takes assemblies/types; DI is host composition. Entry mega-interface
becomes plugin ceremony and second truth beside catalog.  
**Decision:** No `IModule` in Core. Host wires services; catalog wires hearing.

### M2 · Hot catalog mutation is required for scenario 49 today

**Attack:** Marketplace N+1 is a product claim; Stage 1 must hot-register.  
**Defense:** Claim is Kernel Stage 3. Stage 1 proves N+1 **listener add by redeploy** with zero
speaker changes (declaration fan-out). Faking hot-load without epochs = dual answerer races.  
**Decision:** G24 stands. Scenarios 24/26/49 labeled Kernel+Core epoch.

### M3 · Fingerprint should hash IL / assembly MVID

**Attack:** Declaration rows miss private helper changes.  
**Defense:** Topology and answerer cardinality are what make silos disagree on **delivery**.
Private IL drift without declaration change is ordinary grain versioning / deploy, not catalog
skew. Hashing MVID couples fingerprint to every rebuild noise.  
**Decision:** Fingerprint = declaration rows only (+ separate shape fingerprints for facts).

### M4 · Behavior kinds should be namespaced (`behavior.vipemail`)

**Attack:** Lowercased class names collide with first-party modules.  
**Defense:** Collision already fails boot/epoch. Authors choose class names; Kernel can mint
unique type names at compile. String prefixes in Core are a second naming scheme.  
**Decision:** One kind convention everywhere. Kernel generator ensures uniqueness.

### M5 · Core should host Roslyn "because modules need it for tests"

**Attack:** Core.Tests might compile snippets.  
**Defense:** Testing helpers that compile code belong in Kernel or a dedicated test util —
not in the runtime package modules depend on.  
**Decision:** No Roslyn reference in `DigitalBrain.Core`.

### M6 · Epoch pointer as Orleans grain directory

**Attack:** Store current catalog in a grain so all silos read it.  
**Defense:** Emit-path or activation-path remote lookup reintroduces registry latency and
split-brain if the directory lags. Epoch is a **cluster membership / deploy agreement**
problem (like today's fingerprint match), not a per-Deliver fetch.  
**Decision:** Immutable epoch snapshots local to silo; agreement via join/version channel,
not per-message directory.

### M7 · Multi-owner via Name prefix (`ada:desk`)

**Attack:** Encode owner in the locus/name string; keep single deployment.  
**Defense:** Convention isolation fails under malice and under accidental Connect. Not physics.  
**Decision:** Deployment isolation. Name is locus/context within one brain, not a tenancy scheme.

### M8 · Uninstall removes answerer while asks open

**Attack:** Catalog drops answerer kind; open asks hang.  
**Defense:** Epoch swap must treat answerer removal like cutover: terminal facts for open asks
or refuse deactivation until drain.  
**Decision:** Kernel policy + Core open-ask table; never silent open forever.

### M9 · Script sandbox = Core capability filter

**Attack:** Put network deny in Core filters for all neurons.  
**Defense:** Capability reification is Kernel Stage 2/3 (external effects as facts). Core Stage 1
is provenance, not prevention, for topology; IO sandbox for scripts is BehaviorHost policy.  
**Decision:** Core filters: envelope integrity + interface whitelist + anti self-proxy.
Script deny-lists: Kernel host.

### M10 · "Module" vs "behavior" is a Core type distinction

**Attack:** Need `class Behavior : Neuron` special base.  
**Defense:** Same dispatch, same journal, same refusal rules. Special base invites special
paths.  
**Decision:** Naming is product language. Runtime type is `Neuron`. Lifecycle authority is
Kernel facts, not a subclass.

---

## 8 · Stage map (honest shipping)

| Stage | Catalog | Behaviors | Multi-owner | Grain versioning |
|---|---|---|---|---|
| **1 — now** | Boot `Build` + fingerprint; redeploy to change | Compiled modules only; "script" = ship code | One owner / deployment | Silo/app deploy; journals survive |
| **2** | Fingerprint join refuse hardened | Capability seam for external effects (Kernel) | Edge IdP → deployment | Same |
| **3** | Epoch snapshots; fingerprint = epoch | Roslyn+ALC gate; Revision facts; drain/cutover | Still deployment-primary | Generation fence + Orleans impl versioning |
| **Product** | Marketplace installs produce RevisionProposed | Studio UX, signed packages | Ops multi-brain fleet | Rolling module deploys (sc44) |

---

## 9 · Recommendations (owner-facing)

1. **Ratify:** Module = assembly of sealed facts + neurons; no `IModule` in Core.  
2. **Ratify:** Catalog fingerprint = declaration-row SHA-256; heterogeneous silos refuse join.  
3. **Ratify:** Behavior = neuron; Kernel owns compile/gate/lifecycle; Core owns dispatch only.  
4. **Ratify:** Stage 1 N+1 = redeploy composition; hot Revision is Stage 3 with epoch hook only
   in Core until then.  
5. **Ratify:** Multi-owner = deployment isolation; no OwnerId on `NeuronId`.  
6. **Strongest objection:** Product demos want live Behavior Studio **now**.  
   **Defense:** Demo on redeploy or a Kernel side-host that blue-greens a silo; do not
   pollute Core with a fake hot registry.  
7. **Fold if:** A single live consumer forces in-process epoch swap before Kernel exists —
   then implement **only** immutable catalog snapshots + atomic pointer + fingerprint, still
   without Roslyn in Core.

---

## 10 · Traceability

| Claim | Evidence |
|---|---|
| Catalog pure build + fingerprint | `src/DigitalBrain.Core/Catalog.cs` |
| Composition hosting | `Hosting/DigitalBrainSiloExtensions.cs` |
| Module / behavior language | `CONTEXT.md` |
| Stage-1 module model + Kernel split | `CORE-ARCHITECTURE.md` §5, G18, G23, G24 |
| Revision as journaled code lifecycle | `CORE-RESEARCH.md` concept 5; `OS.md` growth |
| Product pressure scenarios | `scenarios/05,19,24,25,26,27,36,44,49` |

---

*Prefer delete. One topology derivation. Behaviors are neurons. Marketplace is Kernel.
Core stays thin.*
