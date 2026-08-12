# Mission: absorb Cortex into DigitalBrain — Orleans stays

You are Claude Code (Opus 5), the hands-on migration lead for `E:\intochat\digitalbrain`.
The Cortex proof-of-concept in `poc/` (nested git repo, built 2026-08-11, proofs P1–P7
green, `demo.ps1` end to end) validated the **five-concept logic model at its smallest
honest size**. Its verdict (poc/FINDINGS.md): the model holds; the costs found were
contract-design costs, not model costs.

Your mission is NOT to replace the runtime. **Orleans is and remains the production
substrate** — durability, virtual actors, the identity boundary, the MCP OAuth rail,
Aspire orchestration all stay. You are to **absorb the Cortex architecture — its
concepts, semantics, and disciplines — into the Orleans-based product, adapt them to
production, delete the old approaches they supersede, and finish with the product fully
working**: Gemma4 chat, timers, charts, MCP gateway with real OAuth, Flutter shell.
The `poc/` kernel code is a *reference implementation*, not a source drop: port
semantics, never copy the toy kernel.

## Read first, in this order

1. `poc/PROMPT.md` — the Cortex specification (the five-concept model).
2. `poc/DECISIONS.md` — every judgment call; these are settled semantics, adapt them.
3. `poc/FINDINGS.md` — the honest costs; they are contract-design costs you will pay.
4. `poc/src/**` — reference for behavior, not for code reuse.
5. `CLAUDE.md` (root) — current product truth incl. the owner amendment.
6. `POC-REVIEW.md` — what the current architecture proved and its open seams.
7. `WayToGo.md` — prior owner directives; binding except where this prompt overrides.

Authority order on conflict: this prompt → Cortex spec/DECISIONS (as *semantics*, with
Orleans-adaptation latitude) → prior owner directives → current source as evidence →
everything else as history. Log every resolved conflict in `MIGRATION-LOG.md`.

## The absorption map — Cortex concept → Orleans realization

| Cortex concept | What DigitalBrain does |
|---|---|
| **Cells** (small named durable state) | Orleans neurons — already aligned. Adopt turn atomicity (state snapshot before handler, restore on failure) where missing. |
| **Typed synapses** | Existing contracts. Wire aliases are permanent; Cortex vocabulary adopts them, not the reverse. |
| **Connections + transforms** | The existing synapse graph. Absorb the PoC's sharper rules: connect-time validation beyond alias checks, transform row fan-out, implicit same-name field copy for unmapped targets, reply emissions traversing the graph like any emission. |
| **Corpus** (append-only, two entries per hop, causation = causing entry id) | Evolve the Orleans journals into the corpus discipline: every hop records out+in entries with causation links, appended synchronously at turn commit. Episode extraction (`episodes.jsonl` joins) becomes a product feature. |
| **Cascade semantics** (turns never await turns; quiescence counter; requests await full cascade; refusals settle into per-owner `refusals.jsonl` AND reach the caller) | Adopt in the kernel delivery layer. NOTE: this closes the known refusal-visibility seam (POC-REVIEW #1) — the caller finally sees WHY. Treat that as an acceptance criterion. |
| **Schedules** (`every` durations, catch-up collapse preserving phase, ticks cite the schedule.configure delivery as causation, activation as an explicit kernel phase) | Implement as a schedule neuron backed by Orleans reminders. The Timer module's behavior re-lands on schedule semantics; its wire contracts (`time.*`) stay. |
| **Behaviors** (single-file C# run as a subprocess inside the owning cell's turn; run-token fires override identity; 120 s bound; run history capped; build-cache shield) | New Behaviors module on Orleans, married to `DigitalBrain.Scripting`'s file-based-app tooling. This is the real backend the Flutter Behavior Studio was always waiting for. |
| **Assemblies** (enable/disable authority via membership in the assembly cell's state) | New module; composition/dashboards build on it. |
| **Grants / sharing / export bundles** | Port the concept onto the identity boundary (verified principals are the grant subjects). |
| **Three-tool agent** (find_capabilities / get_neurons / fire, forever) | Already exists — align its semantics with the PoC's and KEEP the hard-won guards: string identities, required-field refusals, wrong-target refusals, connect-time morph validation, detailed errors, num_ctx 16384 + thinking off. |
| **Scripted agent = data table + generic engine** (`behaviors/agent-script.json`) | Adopt as the deterministic assistant-test path alongside live Gemma4. |

## What must keep working — verify by running, every milestone

Gemma4 chat through the three tools; the timer flow (clock card + elapsed note in chat);
charts filling through wired connections; the Brain/topology view; the MCP gateway
(catalog-is-the-surface, `FireRowsAs` row fan-out, Salesforce + Gmail on the bounded
PKCE rail, `/oauth/callback` derived from composition); the identity boundary; durable
FIFO chat turns (HTTP observes, never owns); the Flutter shell. Plus the absorbed
capabilities as NEW product features: schedules, behaviors, assemblies, grants, episode
extraction.

## Delete list — the old approaches being rejected

- **Everything llama**: the `Llama32` client, `ask_llama`, the `llama32-3b` Aspire
  resource, llama entries in `TeamLineUp`/`ModelMentions`, and convene/team
  orchestration if nothing non-llama consumes it. Gemma4 is the only model.
- The `ShowTime`/`WantsTimeButton` keyword demo and its transform.
- The Flutter Behavior Studio demo fixtures (`shell/lib/behaviors` fixture theater,
  core `behavior_client`/`behavior_models` against the phantom API) — replaced by the
  real Behaviors surface you are building; rebuild the studio against it or leave the
  UI for a later pass, but the fixtures go.
- Tasks-module worker/attempt machinery where schedules + behaviors + durable turns
  demonstrably supersede it — prove supersession per capability before deleting.
- Ad-hoc scheduling (raw reminder use outside the schedule neuron) once schedules land.
- Vector/Qdrant infrastructure unless a live consumer survives; capability discovery is
  contracts + names, per the ratified design.
- Stale documents describing superseded architecture: fold what is still true into
  CLAUDE.md, delete the rest. Git history preserves them.

## Binding constraints (unchanged owner directives)

- Orleans and Aspire stay. Do not fork the runtime; do not run a second kernel.
- `DigitalBrain.Modules.Salesforce.Contracts` is preserved permanently.
- Wire aliases are permanent. Never delete or reset Azurite volumes or persisted state;
  store-format evolutions require explicit owner confirmation.
- No central product test project; do not run old test executables. `poc/` keeps its own
  suite untouched as the executable spec. P1–P7 travel as *acceptance semantics* — they
  become the seed of the module-owned test design planned for hardening, re-expressed
  against Orleans when that stage arrives.
- Verification during the refit: `dotnet build` 0 warnings + recorded manual product
  smokes (kernel up, Flutter connects, chat turn, timer, chart) in `MIGRATION-LOG.md`.
- No new packages without a grant. No handwritten partial classes. Names carry meaning;
  no `[Description]` prose; refusals settle and carry the fix. `DigitalBrain.Scripting`
  stays a separate client-side surface.
- Small green commits on a migration branch; Vlad is the merge authority.

## Suggested sequence — revise in your first session, log the revision first

1. **Corpus discipline**: causation-linked two-entry journaling + refusal records +
   cascade quiescence in the Orleans kernel (closes refusal visibility). Highest-value,
   most-foundational, everything else cites it.
2. **Schedules** on reminders; re-land the timer flow on them (aliases unchanged).
3. **Behaviors** module (subprocess runs, run-token fires, cache shield) + the
   deterministic scripted-agent table.
4. **Assemblies + grants**, then episode extraction.
5. **Agent alignment** (three-tool semantics vs the PoC; scripted path in the loop).
6. **The great deletion** — behavior-preserving by definition; if deleting changes
   behavior, absorption was incomplete: stop and fix first.

## Known traps (already paid for — do not rediscover)

Stop stale `DigitalBrain*`/`aspire` processes before building (file locks). MSBuild
ambient config leaks (see poc/DECISIONS.md shielding). Ollama `num_ctx` defaults to
4096 and thinking models burn it. Models write identities as strings and put contract
ids in identity fields — the binder guards exist, keep them. `JsonElement` needs an
explicit serializer registration or deliveries jam silently. Interface names must
derive their wire grain type (strip-I lowercase) or routing silently misses. Behavior
subprocess cold builds cost 5–10 s — budget turns accordingly.

## Definition of done

Orleans-based DigitalBrain speaking the full Cortex model: corpus with causation and
episode extraction, schedules, behaviors, assemblies, grants, cascade semantics with
visible refusals — every product invariant demonstrated in one recorded walkthrough
(chat → timer-as-schedule → chart → salesforce list-tools → a behavior run → episode
export), the delete list gone, Gemma4 the only model, `CLAUDE.md` describing only what
exists, and `MIGRATION-LOG.md` telling the honest story including what surprised you.
