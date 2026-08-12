# Stage 1 — the outcome rail: implementation plan

Branch `stage1-outcome-rail`. Goal: **every non-delivery becomes a durable, addressed, readable fact
carrying a reason and a fix path.** Today a settled refusal is caught in `NeuronOutbox`, written to an
OpenTelemetry span, and reported as delivered — so the assistant sees a 15-second silence and cannot
self-correct. Six of the seventeen amendments are refusal-shaped; this slice gates the rest.

Verification is TDD at the product surface: the probe is written and made to FAIL first, then the
implementation makes it pass. No central test project is created (owner amendment); the probe is a
`DigitalBrain.Scripting` file-based app plus a runtime pass through `digitalbrain-mcp`.

---

## 1. Contracts (new) — `DigitalBrain.Abstractions/Synapses/`

### `RouteOutcomeKind.cs`
```
Delivered | Refused | Failed | Abandoned | Unrouted | Expired | Disabled
```
`[GenerateSerializer]` enum. Only `Refused`, `Abandoned` and `Unrouted` are produced in this slice;
the rest are declared so later stages add producers without a wire change.

### `RouteOutcome.cs` — `[Alias("db.route-outcome")]`
```
SynapseId Delivery      Id(0)   the delivery whose route failed
string    Alias         Id(1)   the effective alias that failed to land
NeuronId  Receiver      Id(2)   the intended receiver
RouteOutcomeKind Kind   Id(3)
string    Reason        Id(4)   the refusal's own message, verbatim
string    FixPath       Id(5)   what to do instead — written for an LLM reader
CorrelationId Correlation Id(6) the failed delivery's correlation, so a caller can match it
```

### `Unrouted.cs` — `[Alias("db.unrouted")]`
```
SynapseId Delivery Id(0) · string Alias Id(1) · NeuronId Source Id(2) · CorrelationId Correlation Id(3)
```
Emitted when an emission resolves **zero** receivers. Today that emission is journaled, creates no
outbox entry, and is silently never delivered (kernel trap 2) — the single most confusing failure in
the product.

### `IInbox.cs` — `[Alias("inbox")]`, `INeuron`
One read verb `ReadOutcomes(long afterSequence)`. **It declares NO `IHandle<T>`** — deliberately.
Declaring `IHandle<RouteOutcome>` would put `RouteOutcome` in the broadcast catalog and mint a
per-correlation ghost inbox on every emission (kernel trap 8). Outcomes reach it by **directed send**
and are handled in `OnUnboundSynapseAsync`, the pattern `ConnectionRelayNeuron` and `ChatTurnWorker`
already use to stay off the catalog.

**Scope note (A18):** the inbox is `inbox:{owner}/main` in this slice, not per-principal. Per-principal
requires the verified principal to ride `SynapseDelivery`, which is Stage 5 — the outbox drain runs on
a grain timer with no ambient actor, so there is nothing to key on yet. Building a half-mechanism now
would be worse than sequencing it. Recorded as A18 follow-up.

## 2. Kernel changes

### `NeuronOutbox.cs` — the seam
1. `catch (NeuronAuthorizationException refusal)` becomes
   `catch (Exception failure) when (NeuronDeliveryMemory.Settles(failure))`, so sender and receiver
   agree on what settles. `McpAuthorizationRequiredException` and `McpAuthorizationDeniedException`
   are `[SettledDeliveryFailure]` and currently fall to the generic catch and retry 1000×/30 min.
   The `OperationCanceledException` catch stays **first** — an attempt timeout is a retry, not a settle.
2. **Never emit from inside the drain loop.** `DrainAsync` indexes and mutates `entries` while
   iterating; a nested `FireAsync` with `turn.Handling == null` would `outbox.Add` mid-iteration and
   re-enter `CommitAsync`. Instead the catch stages into `List<PendingOutcome> _pending`, and the list
   is flushed **after the loop terminates**, before the existing single `CommitAsync` — so the outcome
   facts land in the same durable write as the drained entries.
3. `Abandon` (depth exceeded, attempts exhausted, retry horizon) stages `Abandoned` the same way.
4. **Recursion guard, mandatory:** a delivery whose synapse is `RouteOutcome` or `Unrouted` never
   produces another outcome. Without it a refusing inbox loops forever.

### `NeuronMessagePipeline.cs` — zero receivers stop being silent
In `FireAsync`, when `receivers.Length == 0` and the synapse is not itself an outcome fact, stage an
`Unrouted` directed at the owner's inbox. Guarded by the same recursion check.

### `Neuron.cs`
Add `internal Task SendOutcomeAsync(NeuronId receiver, Synapse outcome)` delegating to the pipeline's
directed send, so the outbox can address an outcome without touching `EmitAsync` (which would consult
the graph and the broadcast catalog).

### Addressing — who receives an outcome
Both, deliberately:
- **the delivery's `Caller`** — for the model's `fire` path this is exactly the originating requester,
  because `SessionNeuron.Fire` makes the session the sender. The session has no handler for
  `RouteOutcome`, but `StageInboundCause` journals every delivery regardless, so it lands in the
  session's incoming feed with no new handler and no broadcast exposure.
- **the owner's inbox** — for graph-routed multi-hop failures, where `Caller` is a relay that already
  knows and cannot act.

This is Cortex's "reaches the caller AND settles into the refusals log", adapted.

## 3. Making `fire` tell the truth — `SystemTools.cs`
`SendAsync` already polls the session's incoming journal by correlation for a reply of the expected
type. Extend the same loop to also match `RouteOutcome` on that correlation and return its `Reason`
plus `FixPath` immediately, instead of spinning to the 15-second deadline and returning
"No {Reply} reply … the target may be unconfigured or refusing."

One poll loop, one extra type check. No fourth tool, no new endpoint.

## 4. TDD order (probe first, and it must fail first)

1. **Write `src/Kernel/DigitalBrain.Scripting/outcome-probe.cs`** — a file-based app that connects as a
   cluster client and, in order:
   - fires `db.connect` with a deliberately malformed morph (a target field that does not exist) and
     asserts a refusal **reason** comes back rather than silence;
   - fires a fact at a live neuron with no connection and asserts `db.unrouted` appears;
   - fires at a non-existent target type and asserts an `Abandoned` outcome.
2. **Run it against HEAD** → it must fail on all three (that is the current defect, and the failure is
   the proof the probe measures the right thing).
3. Implement §1–§3.
4. Run it again → all three pass.
5. `dotnet build DigitalBrain.slnx -warnaserror --nologo` → 0 warnings.
6. **Runtime pass**: stop stale processes, `aspire run`, then drive `digitalbrain-mcp` at
   `http://localhost:5000/mcp` to simulate a user: send a chat message that makes the assistant fire a
   malformed `db.connect`, and confirm the refusal reason reaches the conversation.

## 5. Hazards this plan is designed around
- Mutating `entries` while `DrainAsync` iterates it → staged list, flushed after the loop.
- Outcome facts producing outcome facts → explicit recursion guard.
- `IHandle<RouteOutcome>` minting ghost inboxes per correlation → `OnUnboundSynapseAsync` instead.
- An attempt timeout being mistaken for a settle → `OperationCanceledException` caught first.
- Store-format: all additions are NEW types plus trailing `[Id(n)]`; nothing existing is renumbered,
  and `SynapseDelivery` is not touched in this slice.
- Stale `aspire.exe` / `DigitalBrain*` processes hold output files → killed before every build.
