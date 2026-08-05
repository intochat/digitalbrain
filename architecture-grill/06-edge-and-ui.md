# 06 · Edge — Brain, Session, rich chat, multi-device, journal observation

Date: 2026-08-05. Status: **RATIFIED** (edge surface + observation model).  
Inputs: `CORE-ARCHITECTURE.md` (§4 verbs, G14/G16/G21, streams policy), `CORE-DESIGN.md`
(§ edge), `CORE-RESEARCH.md` (sync R/R graveyard), live `src/DigitalBrain.Core/Brain.cs`,
v1 UiEdge/session journal patterns, scenarios **06, 10, 13, 28, 29, 38, 42, 47**.  
Method: recommendation → strongest attack → defend or fold. Delete-first.

---

## 0 · What the edge is (one paragraph)

The edge is how a **non-neuron process** (HTTP/SSE host, Flutter shell, test, MCP tool host)
speaks into the brain and learns what happened. It is **not** a second runtime, not a god
facade, and not a stream bus. Speech is three verbs on a **session neuron**. Truth is
**journals**. UI is **module vocabulary** (synapses), not Core widgets.

---

## 1 · Decision board

| # | Question | Recommendation | Stand? |
|---|---|---|---|
| 1 | Is Session a neuron? | **Yes.** Kind `session`, name = context. Full journal / watermark / outbox / connections. | **Stand** |
| 2 | AskAsync: poll journal vs one-way observers | **Stage 1: fire once + poll session journal.** Stage 2 may replace the poll with a one-way observer **behind the same cursor contract**. Journal is the ask. | **Stand** |
| 3 | How do UI facts look? | **Ordinary sealed `Synapse` records** from modules (`UiSurface`, `ChartSpec`, …). Edge reads them as `JournalFact` bodies. No Core UI envelope. | **Stand** |
| 4 | Watchers + AlwaysInterleave | **Read/watch may interleave; Deliver never does.** `[AlwaysInterleave]` / `[ReadOnly]` only on Core transport **read** methods. Modules refuse both. Watchers are one-way committed slices — never mutate. | **Stand** |
| 5 | Fat `IDigitalBrain`? | **No.** Core edge = `Brain` + `Session` only. Product hosts compose HTTP/SSE/auth **around** that. | **Stand** |

---

## 2 · Scenario force (why the edge cannot be "chat RPC")

| Scenario class | Force on the edge |
|---|---|
| **06 / 38** rich multimodal | One turn: image in, chart + table + buttons out. Bodies are **facts** (blob refs, chart specs, button OnTap synapses) — not opaque SSE tokens as authority. |
| **29 / 30** progressive / replan | Multi-minute jobs emit **intermediate** surfaces; chat must stay usable in **other** contexts. Edge must observe **progress facts** without holding one grain turn for the whole job. |
| **10 / 47** live dashboards | Ambient fan-out of KPI/chart facts to many panes. UI is a **subscriber to module emissions**, late-join via snapshot+watermark — not full day replay over a stream. |
| **13** multi-device handoff | Phone and desktop are **views**. Durable work lives on `chat/{thread}` (and peers). Handoff = rebind subscriptions + snapshot asks; not transcript export. |
| **42** share pane not journals | Guest gets **derived projection facts**, never `ReadAsync` of owner journals. Edge policy lives at host/Kernel — Core still exposes only speech + journal read. |
| **28** stream wake | Streams wake **ingress adapters** that journal first. Not the UI authority path. |

---

## 3 · Self-grill

### Q1 · Session is a neuron?

**Recommendation:** Session **is** a Core-owned neuron kind (`NeuronId.KindOf(Session)` →
`session`), instance name = **context** (conversation locus, shell desk, dashboard bind
key). Edge-born facts carry `Cause: null`. Emit / directed Send / Ask each open **one**
session turn and return **after commit**. Delivery drains like any other neuron.

**Strongest attack:** A session grain is wasted activation cost; the edge could stamp
`source = edge-proxy` and call `Deliver` directly into chat without a session journal.

**Defense:** Without a session journal: (1) Ask has nowhere durable to pin the open ask and
match `Answers`; (2) multi-device and crash recovery lose the only place that reconstructs
"what did this UI say?"; (3) directed `Connect` from the edge has no emitter identity;
(4) you reintroduce a non-journaled ingress path — dual truth. Proven: greeter flow, restart
tests, and `AskOutcomeUnknownException` all hang on **session journal = ask**.

**Fold?** No. Session stays a neuron. There is no "edge ghost source".

**Corollary — multi-device:** Devices do **not** each need a distinct session neuron for the
*work* locus. Prefer:

- **Work context** = durable domain name (`chat/northwind-renewal`, `metrics/owner-revenue`).
- **Edge session context** = one bind key the product chooses (`desk`, `phone-shell`, or
  per-connection id for pure push).

Handoff (sc13): desktop session (or same context with new SSE) **reads** chat/approval
journals and re-attaches interest; phone parks. Dual typing conflicts are domain facts
(`DraftRevisionConflicted`), serialized on the work neuron — not on the edge facade.

---

### Q2 · AskAsync poll vs one-way observers

**Recommendation:**

```
AskAsync = commit ask on session (exactly once)
         → observe session journal from askRef.Sequence
         → matching TReply (Answers == askRef)
           | DeliveryFailed | AskExpired
           | cancel (journal still holds / will hold outcome)
```

Stage 1 implementation: **poll** `ITransport.ReadAsync` with backoff (today: 75ms).  
Stage 2: optional one-way **journal observer** (grain callback or stream **mirror of
committed positions**) that advances the **same cursor**. Callers do not change.

**Strongest attack A — latency:** Polling is too slow for HTTP and token streaming UX.

**Defense:** Correctness forbids same-turn reply ride-back (G16: second delivery path
bypasses outbox FIFO). Latency fixes:

1. First read is **immediate** (zero delay on warm path).
2. Short backoff; optional Stage-2 push.
3. Progressive **UX** is not AskAsync — it is `ReadAsync`/`Watch` on **chat / projector**
   journals while the long ask is still open (partial `UiSurface` facts).
4. Token streams, if any, are **product edge projections** of module facts or model IO
   adapters — never the causal bus and never a substitute for `AssistantResponded`.

**Strongest attack B — observer is cleaner; delete poll forever.**

**Defense:** Poll is the dumbest correct observer. Shipping only push reintroduces v1
`WatchNeuron` on the session broker, sticky observers across deactivation, and dual
"am I watching?" state. Poll needs **zero** durable edge state. Stage 2 push is an
optimization of the **same** contract: `afterPosition → JournalFact*`.

**Fold?** No. Journal observation remains the only Ask completion path. Task is volatile
sugar. Wire failure → `AskOutcomeUnknownException` → **read session journal, never refire**.

**UI progressive path (sc29):**

| Concern | Mechanism |
|---|---|
| Fire user turn | `session.Emit` / `Send` / `Ask` of chat vocabulary |
| Progress bars, partial tables | Module `Emit(UiSurface|ResearchPartial|…)` → journals of projector/orchestrator |
| Edge shows progress | Host loops `Brain.ReadAsync(projectorOrChat, after)` (or Stage-2 watch) |
| Terminal chat answer | Directed reply fact; AskAsync (if used) or same journal watch matches it |
| Other chats concurrent | **Other context names** — other session and chat neurons |

---

### Q3 · How UI facts look

**Recommendation:** UI remains **module synapses**. Core does not ship widgets, RFW blobs,
or "UI payload" envelopes on the ABI.

Canonical shape (product/modules — illustrative, not Core types):

```text
UiSurface(blocks: Markdown | Chart | ImageRef | Table | ButtonBar | Progress | …)
Button(label, onTap: Synapse)     // tap IS a fact the shell Emits/Sends
ChatAttachmentAdded(blobRef, mime, …)
ChartSpec / ChartBuildAnswered
AssistantResponded(text, artifactRefs?)
KpiTileUpdated / ChartPointAppended / DashboardSnapshot
```

Edge public **read** shape stays Core:

```text
JournalFact(Position, Entry, Kind, Metadata, To?, Body)
NeuronReading(Journal, Connections)
```

`Body` is null when the kind is unknown to the running catalog (journals outlive code).

**Strongest attack:** Multimodal needs a Core `RichMessage` with media slots so every
client shares one schema.

**Defense:** Schema consensus is a **module pack** (shell/chat/UI projector), versioned with
the product — not Core physics. Putting UI in Core recreates Projects/digitalbrain kitchen
sink (palette, render, RFW). final/self-improving already proved: closed widget union as
**synapses**, renderers as listeners. sc42 needs **redacted projection facts**, not a
shared Core rich type that leaks email snippets by default fields.

**Fold?** No Core UI types. Product may pin a shell module vocabulary; Core only guarantees
delivery + journal identity.

**SSE / Flutter:** Host maps `JournalFact` → wire DTO. Mapping is host-private (v1 lesson:
SSE uses session journal watch, **not** product client god-API, not OpenTelemetry).

---

### Q4 · Watchers on journals — AlwaysInterleave risk

**Load-bearing physics:**

- Turns are **serialized**. `Deliver` / drain / schedule ticks **must not** interleave.
- Long model turns occupy a neuron for seconds–minutes.
- UI and AskAsync **must** read committed journal slices during that occupation or the shell
  freezes and progressive UI dies.

**Recommendation:**

| Surface | Interleave? | Why |
|---|---|---|
| `ITransport.Deliver*` / drain / session Emit·Send·Ask fire | **Never** | Mutation + lineage |
| `ITransport.ReadAsync` (committed watermark only) | **Yes** (Core-owned) | Pure read of committed truth |
| Module-declared methods / extra grain ifaces | **Forbidden** | Boot refuse `AlwaysInterleave`, `MayInterleave`, `Reentrant`, non-Core `ReadOnly` |
| Stage-2 journal observer callback | One-way, **no** handler re-entry into subject | Push bytes; never `Deliver` from observer |

Implementation note (honest): Stage 1 code marks transport reads `[ReadOnly]`. Orleans
**interleave under load** for non-reentrant grains requires **`[AlwaysInterleave]` on the
read method of the Core transport interface** (or equivalent Core-owned interleave
predicate). That attribute is **surgical and Core-owned** — `NeuronConcurrency` continues
to refuse it on every **module-visible** method. Reads serve `journal[0..committedCount)`
only so an interleaved read never surfaces a row a failed commit will retract.

**Strongest attack A — AlwaysInterleave on Read is the v1 deadlock class again.**

**Defense:** v1 died on AlwaysInterleave + **awaiting** capability completion **inside** a
serialized chat turn (sync R/R + poll inside tool). Here:

1. Interleave is **only** for committed journal **reads**.
2. Readers never open turns, never stage emissions, never call `Deliver`.
3. AskAsync poll runs **outside** any neuron handler (edge process).
4. Neurons still **never await neurons**.

If a read implementation ever stages state or calls out to modules, it is a bug — not a
reason to ban reads.

**Strongest attack B — put WatchNeuron on the session grain (v1) so UI has one broker.**

**Defense:** Session-as-watch-broker couples deactivation, observer fan-out, and
authorization into the speech neuron. Prefer:

- Stage 1: edge host polls `Brain.ReadAsync(subject, after)`.
- Stage 2: `Brain.WatchAsync(subject, after, ct)` as `IAsyncEnumerable<JournalFact>` —
  client-side loop or thin Core helper; optional grain-side one-way notify that only
  wakes waiters after commit.

No durable "subscription table" on Session for Stage 1.

**Strongest attack C — Orleans streams for every UI update.**

**Defense:** Streams lose late join and become a second bus (G4/G21). Allowed: **mirror**
of already-committed facts to SSE fans (egress adapter). Authority + watermark remain
journals. Dashboard reconnect = snapshot ask + `afterPosition`, not stream replay.

**Fold?** No interleave on Deliver. Yes interleave on Core committed reads. Watchers are
observation of journals, not a parallel nervous system.

---

### Q5 · No fat IDigitalBrain

**Recommendation:** Core public edge is exactly two types:

- `Brain` — enter sessions; read any neuron's journal (+ connections).
- `Session` — speak (Emit / Send / Ask).

**Forbidden on Core edge (non-exhaustive):**

| Temptation | Why banned |
|---|---|
| `Get<TNeuron>` / grain proxy | Type-coupled orchestration; modules name classes across boundaries |
| `ActivateAsync` brain lifecycle | Hosting / Kernel |
| `WatchNeuron` + observer registry on mega-interface | Freezes wrong broker; v1 shape |
| Capability install / behavior CRUD | Kernel |
| Owner / multi-principal IdP | Deployment + host |
| Stream subscribe APIs as product surface | Streams stay internal adapters |
| `SendAsync<TNeuron>(name, …)` | Type-coupled send — use `NeuronId` |

**Strongest attack:** Product ergonomics need one injectable `IDigitalBrain` for samples,
compositions, and MCP.

**Defense:** A **product** facade may exist **outside** Core (`DigitalBrain.Client` /
OS host) as a thin wrapper: owner binding, auth, maybe activate. It must not grow journal
watch, topology edit, and speech into one frozen interface that Core owns. v1 already
proved the split: `IDigitalBrain` speech-only; journal watch host-private
(`OwnerSessionJournal`); tests enforce SSE does not take `IDigitalBrain` for watch.

Core stays:

```text
Brain.Session / Brain.ReadAsync
Session.EmitAsync / SendAsync / AskAsync
```

Samples and MCP take `Brain` (or a one-line product wrapper), not a kitchen sink.

**Fold?** No Core `IDigitalBrain`. Product may wrap; Core does not ratify the mega-interface.

---

## 4 · Rich chat & progressive UI — edge choreography (canonical)

```text
[Flutter / HTTP]
    |  Session("desk") or Session(threadKey)   // product chooses context policy
    |-- Emit/Send UserMessaged + ChatAttachmentAdded
    |-- (optional) AskAsync(StartTurn) if a single typed terminal reply is enough
    |
    |  parallel observation (does not hold chat turn):
    |-- ReadAsync(chat/thread, after)     → transcript + AssistantResponded
    |-- ReadAsync(uiProjector/shell, after) → UiSurface progressive blocks
    |
[Chat neuron @ thread]  long serialized turn OR multi-turn with TState join
    |-- Ask vision / CRM / chart (facts; deferred answers OK)
    |-- Emit intermediate UiSurface / ChatArtifactProduced
    |-- Reply/Emit AssistantResponded (+ buttons as synapses)
    |
[Shell] tap → Session.Send/Emit OnTap synapse → ActionRouter (new turn)
```

Rules:

1. **Images/charts** travel as refs + specs in synapse bodies; blob bytes live in blob store.
2. **Partial failure** is a fact (`ChartFailed` block), not a blank widget.
3. **Cancel/replan** are domain facts; committed emissions are not un-said.
4. **Multi-device:** both devices observe the **same work journals**; speech sessions may
   differ; do not fork chat instance names per device unless isolation is intended.

---

## 5 · Streams vs journals for UI (final cut)

| Job | Mechanism |
|---|---|
| Causal history, audit, Ask completion, reconnect watermark | **Journal** (`ReadAsync` / Stage-2 watch) |
| Live many-pane fan-out of module facts | **Emit** → catalog∪connections → each dashboard neuron journals → edge reads **those** journals (or mirrors) |
| High-volume ingress wake | Stream → **adapter** → journal first (sc28) |
| SSE fan-out to browsers | Host/egress adapter; optional stream mirror of **committed** positions |
| Guest share without journals | Derived `GuestUiSurface` facts only (sc42) — never grant `ReadAsync` of owner |

---

## 6 · RATIFIED edge surface (methods only)

Stage 1 — **this is the complete Core public edge.** No other speech or observation methods
on Core types.

```csharp
namespace DigitalBrain;

public sealed class Brain
{
    public Session Session(string context);

    public Task<NeuronReading> ReadAsync(
        NeuronId neuron,
        long afterPosition = 0,
        CancellationToken cancellationToken = default);
}

public sealed class Session
{
    public NeuronId Id { get; }

    public Task EmitAsync(
        Synapse fact,
        CancellationToken cancellationToken = default);

    public Task SendAsync(
        NeuronId receiver,
        Synapse fact,
        CancellationToken cancellationToken = default);

    public Task<TReply> AskAsync<TReply>(
        Synapse question,
        CancellationToken cancellationToken = default)
        where TReply : Synapse;
}
```

### Method contracts (normative)

| Method | Returns when | Durable effect |
|---|---|---|
| `Session(context)` | Immediately | Addresses `session/{context}` (no I/O required) |
| `EmitAsync` | After **session turn commit** | Said entry; declaration∪connection fan-out staged |
| `SendAsync` | After **session turn commit** | Said entry; **exactly** named receiver |
| `AskAsync` | After typed reply **heard on session journal**, or terminal Core failure fact, or cancel | Ask said once; open-ask pin; Task is not durable |
| `ReadAsync` | After committed slice loaded | None (read of `journal(afterPosition..committed]`) + connection table |

### Explicit non-methods (Stage 1)

- No `Watch*`, `Subscribe*`, `Get*`, `Activate*`, `Install*`, `Stream*`.
- No `IDigitalBrain`.
- No UI/widget types on Core.
- No in-neuron edge Send beyond Session (already ratified Stage 1).

### Stage 2 (optional, same cursor — not required for Stage 1 green)

```csharp
// Brain — sugar only; must be implementable as ReadAsync + await delay
IAsyncEnumerable<JournalFact> WatchAsync(
    NeuronId neuron,
    long afterPosition,
    CancellationToken cancellationToken = default);
```

Push may replace the delay; **must not** change AskAsync or ReadAsync contracts; **must not**
interleave Deliver; **must not** land as a fat facade method bag.

### Failure types (already Core)

- `AskFailedException` — journaled `DeliveryFailed` / `AskExpired` for the ask.
- `AskOutcomeUnknownException` — single fire wire failure; recover via `ReadAsync(session.Id)`.

---

## 7 · AlwaysInterleave / concurrency ratification (edge-relevant)

| Rule | Ratified |
|---|---|
| Module methods may use `[AlwaysInterleave]` / `[Reentrant]` / `[MayInterleave]` | **No** — boot refuse |
| Core `ITransport.ReadAsync` (and pure health) may interleave | **Yes** — Core-owned only |
| Core `Deliver*`, drain, session mutators interleave | **No** |
| Watcher/observer may call back into subject `Deliver` | **No** |
| AskAsync may run inside a neuron handler awaiting another neuron | **No** — edge/process only |

---

## 8 · Grill log (compact)

| ID | Attack | Decision |
|---|---|---|
| E1 | Edge without session neuron | **Reject** — loses durable ask + ingress identity |
| E2 | Same-turn reply for HTTP | **Reject** — dual delivery path (G16) |
| E3 | Delete poll; push-only | **Defer** push as Stage 2; poll is correct Stage 1 |
| E4 | Core `RichMessage` / UI envelope | **Reject** — module synapses |
| E5 | Streams as UI truth | **Reject** — journal authority; streams mirror/egress only |
| E6 | Session hosts WatchNeuron registry | **Reject** Stage 1; host polls `ReadAsync` |
| E7 | AlwaysInterleave on Deliver for "responsive UI" | **Reject** — deadlock class |
| E8 | AlwaysInterleave on Core Read | **Accept** — surgical; committed watermark only |
| E9 | Fat `IDigitalBrain` | **Reject** — `Brain` + `Session` only |
| E10 | Device = chat instance name | **Reject** as default — devices are views; work neurons hold state |
| E11 | Guest ReadAsync of owner journals | **Reject** — derived projection facts (sc42) |
| E12 | Token SSE as substitute for AssistantResponded | **Reject** — optional projection; journaled fact is terminal truth |

---

## 9 · What this document does *not* ratify

- HTTP routes, SSE event names, Flutter widget tree (product UiEdge).
- Blob storage API for image bytes.
- Exact shell module synapse names (`UiSurface` is illustrative).
- Multi-owner IdP and share-token crypto (Kernel/host).
- Kernel behavior install surface.

---

*Prefer delete. Two observation paths for one job → keep journal cursor. UI is facts.
Session is a neuron. No fat facade.*
