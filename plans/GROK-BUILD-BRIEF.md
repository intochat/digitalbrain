# Build brief — DigitalBrain, 500-agent implementation run

You are the orchestrator of a 500-agent build on `E:\intochat\digitalbrain`, branch
`stage1-outcome-rail`. Everything you need to plan, prioritise and implement is already in the repo.
Read this file completely before spawning a single agent.

---

## 0. Read these first, in this order

| Source | What it is | Authority |
|---|---|---|
| `Cortex Absorption.html` | The architecture: three axes, the 17 amendments, the 11-stage programme, what is deleted/kept/adapted | **Design authority** |
| `The Brain, Traced.html` | 50 user scenarios re-simulated against the amended architecture, each with a hop-by-hop trace, verdict and residual. Open it — the traces are live data in the page | **Acceptance authority** |
| `MIGRATION-LOG.md` | Six sessions of resolved conflicts, deviations, regressions and operational traps. §29–31 is the current state | **History + current state** |
| `CLAUDE.md` | Product truth, conventions, and nine kernel traps that each cost a debugging session | **Binding conventions** |
| `plans/stage1-outcome-rail.md` | Worked example of a slice plan that survived five grilling lenses | Template |
| `poc/` | The Cortex reference PoC (NOT Orleans). Semantics only — never copy its kernel | Reference |

Open the two HTML files in a browser or parse them. `The Brain, Traced.html` carries the 50 scenarios
and their component-by-component traces as embedded JSON in a `<script id="data">` block — extract it;
it is your acceptance matrix and it names the exact components each scenario touches.

## 1. State of the branch — do not redo this work

`stage1-outcome-rail` is 8 commits ahead of `master`, builds with **0 warnings**, and is verified
running. Already **done and committed**:

- **The outcome rail.** `db.route-outcome` + `db.unrouted`; the outbox's refusal catch generalised to
  any settled failure; outcomes journaled into the firing neuron's incoming feed (never delivered);
  `fire` and `DigitalBrainClient` surface reasons instead of a 15s silence.
- **`[JournalProjection]`** — marks facts that reach their audience by journal projection rather than
  delivery, so they are not falsely reported unrouted. Applied to `chat.responded`.
- **Connection provenance** — `Provenance(Author, At, StatedIntent, Correlation)` on
  `SynapseConnection`; `Intent` on `Connect`; `get_neurons` prints ConnectionId and intent.

Do not re-implement these. Read the diff: `git log master..HEAD -p`.

## 2. The physical constraints — these bound your parallelism absolutely

**These are not guidelines. Violating them produces an unbuildable branch.**

1. **Exactly ONE AppHost may run on this machine.** Fixed ports (kernel 5080, MCP 5000), one Azurite
   data volume, one Ollama GPU. Concurrent AppHosts corrupt each other's cluster membership.
2. **A solution build locks output files.** A running silo holds `bin/**` open; concurrent
   `dotnet build` on the same tree fails with MSB3026. Agents sharing a working tree cannot build
   concurrently.
3. **Gemma4 is one 12B model on one GPU.** Inference serialises no matter how many agents queue it.
   Any plan assuming parallel model turns is wrong.
4. **Orleans grain types are fixed at silo start.** Verified in docs: `GrainTypeAttribute` is
   `AllowMultiple=false`, `GrainClassMap` is immutable, `GrainManifest` is `[Immutable]` with no
   republish API. A new *kind* of cell therefore cannot be added at runtime — it rides in the grain
   **key**, not the grain type.
5. **Never force-kill AppHost.** It leaves an `Active` membership row for a dead endpoint and the next
   silo refuses to join (`OrleansClusterConnectivityCheckFailedException`). The kernel then reports
   `Finished` while MCP stays up — it looks exactly like a code regression and is not one. Shut down
   gracefully; if you must recover, prune with the pattern in `MIGRATION-LOG.md` §30 (delete only
   membership rows whose proxy port refuses TCP; keep `VersionRow`; touch nothing else).
6. **Never delete or reset Azurite volumes or persisted product state.** Store-format changes are
   append-only: a new trailing `[Id(n)]` with a default. Never renumber or reuse an existing `[Id(n)]`.
7. **No new NuGet packages** without explicit owner approval.
8. **No central test project.** Verification is `dotnet build DigitalBrain.slnx -warnaserror --nologo`
   (0 warnings) plus runtime checks through `digitalbrain-mcp`.

**Consequence for topology:** fan-out is for *authoring, analysis and review*. **Building and runtime
verification are a single serialized lane.** An orchestration that has 500 agents each running a build
or an AppHost will produce nothing.

## 3. The decision that gates ~40% of the work — make it in Wave 0

Four amendments **fork** on one unmade architectural decision, and building them first means building
them twice: the **connection record**, the **wire language**, the **read rail**, and the **capability
surface**. They all fork on:

> **Do kinds become data (the interpreted cell tier) in this run, or not?**

- **If yes:** one compiled `[GrainType("cell")]` grain interprets a durable kind record, addressed
  `cell:{owner}/{kind}@{name}` — `@`, never `/`, because `NeuronId.FromGrainKey` splits at the first
  `/` and `IdentityPart.Validated` rejects it in a name. Connect-time validation then needs a
  two-backend `IEndpointShapes` resolver (compiled reflection **and** declared kind shape), the
  capability index needs a runtime overlay, and `fire` needs a second resolution path.
- **If no:** those four amendments are implemented against compiled types only and must be rewritten
  when the kind tier lands.

**Wave 0 must decide this and record the decision in `MIGRATION-LOG.md` before Wave 2 starts.**
`Cortex Absorption.html` argues the case; the deciding evidence is in its "third axis" section.

Also unresolved and blocking, from `MIGRATION-LOG.md` §18 — settle each with running code, not opinion:
whether an Aspire project-resource restart rebuilds; whether a runtime-loaded assembly's grain types
can register at startup; whether `Microsoft.Orleans.Journaling` (an **alpha prerelease** that is the
only durable record in the system) resolves keyed durable services as production assumes; and whether
an `@`-bearing grain key survives Azurite blobs, Tables reminders and clustering end to end.

## 4. Agent topology — 500 agents, 11 lanes

Lanes run concurrently. Within a lane, agents run in parallel **only where file ownership is
disjoint**. Every lane has a paired adversarial reviewer group.

| Lane | Purpose | Agents | Concurrency |
|---|---|---|---|
| **L0** Decisions & spikes | The §3 fork, the four unverified facts, one running-code spike each | 20 | parallel, then barrier |
| **L1** Kernel core | `Neuron`, `NeuronOutbox`, `NeuronMessagePipeline`, `NeuronTurnCoordinator`, `SynapseGraphNeuron`, `BroadcastCatalog` | 30 | **1 author at a time**, 29 reviewing |
| **L2** Contracts & vocabulary | `DigitalBrain.Abstractions`, carrier synapse, `EffectiveAlias`, manifests | 40 | 1 author per file |
| **L3** Modules | Schedules, Behaviors, Bundles, Library, instance registry | 80 | parallel by module |
| **L4** MCP & integrations | Gateway repair, PKCE keying, refresh grant, row morph, server registry | 40 | parallel by concern |
| **L5** Identity & authority | Principal partition, actor on delivery, grants, quotas, trust boundary | 40 | 1 author per file |
| **L6** Flutter | core / kit / shell, view renderer, reconnect, login | 60 | parallel by package |
| **L7** Kind tier | Cell grain, kind record, total evaluator, view document | 60 | gated on L0 decision |
| **L8** Verification | 50-scenario MCP harness, probes, evidence capture | 60 | authoring parallel, **execution serial** |
| **L9** Adversarial review | Paired to every lane; refutes before merge | 50 | parallel |
| **L10** Docs | `MIGRATION-LOG.md`, `CLAUDE.md`, per-slice plans | 20 | parallel |

### The rules that make this safe

1. **Exclusive file ownership.** Every agent declares the exact files it will modify before starting.
   You refuse any claim overlapping a live claim. Two agents editing `NeuronOutbox.cs` is the single
   most likely way this run fails.
2. **The kernel is a one-author lane.** L1 has one writer at a time. The other 29 review, trace call
   paths, and prepare the next change. Kernel files are touched by everything; parallel edits there
   are unmergeable.
3. **Isolation for authoring.** Agents that need to compile work in their own `git worktree`. They
   push a patch, not a build artifact.
4. **One build token, one runtime token.** Two mutex tokens exist. `BUILD` — the holder may run
   `dotnet build`. `RUNTIME` — the holder may start AppHost and drive MCP. **No agent may hold RUNTIME
   without first stopping AppHost gracefully and confirming ports 5000/5080 are free.** Everything in
   L8 queues on RUNTIME.
5. **Barriers at architectural forks.** L2, L4, L5, L7 do not start until L0's decision is committed.
   L3 and L7 do not start until L1's broadcast-opt-in change is merged and verified.
6. **Every change is reviewed by an L9 agent instructed to refute it**, citing `file:line`. A reviewer
   that confirms everything is worthless — in the last review round, three judges returned **zero**
   refutations across 639 rulings, which was agreement bias, not correctness. Instruct reviewers to
   default to refuted when uncertain, and check their refutations against source yourself.

## 5. Wave order — dependency, not convenience

**Wave 0 — Decide and de-risk.** The §3 fork; the four unverified facts, each settled by running code.
*Gate:* decision committed to `MIGRATION-LOG.md`; four spike results recorded.

**Wave 1 — Broadcast becomes opt-in.** Fatal-rated, and the hard prerequisite for the instance
registry and for the kind tier. Today **48 synapse types** declare `IHandle<T>` and therefore sit in
the broadcast catalog, so every `EmitAsync` mints a per-correlation ghost grain addressed
`type:owner/{correlation}`. There is a live one in the cluster right now (`chart:dev/f1a35f68-…`).
Because ghosts are correlation-addressed, **no named neuron can ever receive a broadcast** — the tier
looks like pure cost. That reasoning is strong but is *not proof*: before merging, enumerate all 48
types and prove per type that no product flow depends on ghost delivery, then verify a live chat turn,
a timer flow and a chart wire before and after.
*Gate:* no ghost grains in `list_active_neurons` after a full chat turn; chat, timer and chart still work.

**Wave 2 — Registry, lifecycle, wires that cannot lie.** Durable per-owner instance registry (nothing
today can enumerate a thing that exists but is not activated); `db.retire`; bundle install as one
request; connect-time validation completeness (type compatibility, target-handles-alias, required
fields covered, duplicate refusal).
*Gate:* "walk me through this brain" names an idle schedule, a disabled bundle and a cold chart.

**Wave 3 — Two people, one brain.** Principal partition for every product neuron; verified principal
riding `SynapseDelivery`/`OutboxEntry`; grants as read/watch checked per read; the cluster trust
boundary; quotas. **Includes A18** — corpus, refusals inbox, instance registry and the synapse graph
are all owner-scoped while the partition is principal; this is the single highest-leverage correction
on the board and alone cleans six scenarios.
*Gate:* two logins, two machines, each sees only their own chat and chart; grant, watch live, revoke, denied.

**Wave 4 — The outside world.** MCP repair: pending authorization keyed on `(serverKey, PrincipalId)`,
refresh-token grant, row morph with whole-batch validation, summary replies, destructive-tool gate,
durable server registry.
*Gate:* a principal who never connected Salesforce signs in mid-conversation and the **same** request completes.

**Wave 5 — Time and memory.** Calendar-aware schedules with durable next-due and phase-preserving
catch-up; the due→tick two-step; corpus as a watermarked resumable projection; episodes.
*Gate:* stop the silo 20 minutes with a 5-minute schedule armed; exactly ONE tick fires, reported
`Resolution=Recovered, CollapsedPeriods=4`.

**Wave 6 — Kinds become data** (if Wave 0 said yes). Cell grain, kind record, total evaluator, view
document, Flutter `KitView`.
*Gate:* "build me a calculator" → tap `7 × 6 =` → `42` on screen; survives a silo restart.

**Wave 7 — Share it.** Library: publish, discover, install. Content-hashed artifacts, immutable
versions, installs copy structure and reference the artifact id, arriving disabled.
*Gate:* Alice publishes; Bob discovers it in his own `find_capabilities`, installs, enables against
**his** credentials, and his numbers differ from hers.

**Wave 8 — Code where code is needed.** Behaviors as durable runs owned by an Execution; the
repository rail; per-file agents in their reachable form.
*Gate:* point the brain at this repo, ask for a change, watch ~30 file cells produce stances and a
moderator fold three rounds into a written plan.

## 6. Verification protocol — the only evidence that counts

`digitalbrain-mcp` is configured in `.mcp.json` at `http://localhost:5000/mcp`. It is **stateless** —
plain JSON-RPC `tools/call`, no handshake. Tools: `send_chat_message`, `activate_chat_button`,
`list_active_neurons`, `read_neuron_journal`, `read_chat_transcript`.

For every wave:
1. Stop AppHost gracefully. Confirm 5000/5080 free.
2. `dotnet build DigitalBrain.slnx -warnaserror --nologo` → **0 warnings**.
3. Start AppHost. Wait for kernel **and** MCP to listen — MCP listening alone does not mean the kernel
   started; check 5080 explicitly.
4. Drive the wave's gate through MCP and **capture the journal evidence**, not the prose. The
   assistant's summary is corroboration; a durable journal entry is proof.
5. Re-run the affected scenarios from `The Brain, Traced.html` and record verdict changes.
6. Append findings to `MIGRATION-LOG.md`, including anything that surprised you.

**Never claim a gate passed without the journal evidence in the log.**

## 7. The traps, restated because each one already cost a session

1. A turn cannot await the effect of its own `Send`; the outbox drains between turns.
2. A zero-receiver emission creates no outbox entry — wire the graph **before** triggering the source.
3. Every non-framework inter-neuron grain call is reified into durable journal facts.
4. Only `[SettledDeliveryFailure]` exceptions settle; everything else retries 1000×/30min.
5. Declaring `IHandle<T>` puts `T` in the broadcast catalog and mints ghosts.
6. Models pass names where schemas want GUIDs; the binder guards exist — keep them.
7. `JsonElement` needs explicit serializer registration or deliveries jam silently.
8. File-based apps default to `PublishAot`, which silently kills reflection JSON. Set
   `PublishAot=false` and `JsonSerializerIsReflectionEnabledByDefault=true`.
9. **`ReplyAsync` addresses the caller, and callers routinely have no `IHandle<T>` for the reply
   type.** Making unhandled synapses refuse breaks every request/reply in the product. This was tried
   in session 5, compiled clean, and broke everything; only the live run caught it. If you attempt it,
   it needs an explicit accept-list for reply sinks.

## 8. How this run fails — recognise these early

- Two agents edit a kernel file; the merge is unresolvable; the branch stops building.
- Agents build concurrently; MSB3026 file locks; hours lost to a non-problem.
- An agent force-kills AppHost; every subsequent silo refuses to join; looks like a code regression.
- Reviewers confirm everything; a plausible-but-wrong change lands.
- Waves 2–5 are built before the §3 decision, then rebuilt.
- A gate is declared passed on the assistant's prose instead of journal evidence.
- Scope is declared "complete" when it is 60% of the ask. **Report the strict number.**

## 9. Definition of done

A wave is done when: the solution builds with 0 warnings; its gate has been demonstrated through
`digitalbrain-mcp` with journal evidence recorded in `MIGRATION-LOG.md`; the affected scenarios in
`The Brain, Traced.html` have been re-verified; an L9 reviewer failed to refute it; and the work is
committed in small coherent commits on `stage1-outcome-rail`.

**The run is done when all 50 scenarios verify at their recorded verdict or better, and every
divergence is written down.** Vlad is the merge authority; do not merge to `master`.

## 10. Honest expectation

The 50-scenario re-simulation scored **100% carried / 60% exactly-as-asked** against the *finished*
architecture. Two of the eleven stages are rated "very large" and one vision target — 5,000
self-implementing per-file agents — is **not reachable** as literally stated: the brain has no
filesystem, one GPU serialises inference, and "properly" has no in-system acceptance criterion. Its
reachable form is a ~40-cell working set producing a plan with per-file provenance.

Plan for that reality. A run that reports 100% complete is reporting the loose number, or lying.
