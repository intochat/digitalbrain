# InoLang RFC — Should the language exist, and in what form?

Status: Gate 0 executed 2026-06-12; B (rule interpreter) demoted per §6.1 kill (12/20 60% first-try <80%); A+D only. Research record only.
Decision: **A+D (after Gate 0 demotion of B); C already deleted. D (L2 codegen) is the documented escape hatch behind Trust L2. Full B design + Gate 0 evidence preserved in this RFC for potential future re-spike.**
D2 (U0 session): InoLang-B FROZEN per owner default. Landed RuleHostNeuron + parser + interpreter + green BDD preserved; no grammar growth, no new statement kinds. New behavior routes L2/L3 only. (UNIFICATION-PLAN D2.)
Owner: Vlad. Produced 2026-06-12 from full-tree archaeology (E:\Projects) + external prior art.

---

## 0. Archaeology — every prior InoLang incarnation

| # | Incarnation | Shape | Execution model | What worked | What killed it |
|---|---|---|---|---|---|
| 1 | **v1 full InoLang** — `v1/inolang/DigitalBrain.InoLang/` (Lexing/ Parsing/ Planning/ Runtime/Interpreter.cs, Diagnostics, InoCompiler.cs, InoGate.cs), samples `v1/digitalbrain.ino`, `v1/examples/*.ino` | Rich language: `let`/`ask`, `if`/`else`, `foreach`, `log`, `count`, telemetry attrs, **speculate/commit/rollback/verify** transactional branches, flow mappings, `write`, embedded `scenario` with `given X returns` mocks. Even the OS boot was a `.ino` (`digitalbrain.ino` registers Aspire resources) | Tree-walking interpreter over an ExecutionPlan; everything evaluated to **strings**; `INeuronHost.AskAsync(port, prompt)` string protocols (`"set scope:key=value"`); hot reload on file save; DynamicNeuronGrain | Dual visual/code authoring demoed; speculation sandboxes with COW vars and failure handlers actually ran; hot-reload topology was real | Stringly-typed evaluation decayed into hardcoded builtins (`is-successful-spawn`, `extract-path` regex with a literal `"D:/"` fallback in `Interpreter.cs`); 440+ sequential tests; per PROGRESS-ARCHAEOLOGY: "heavy InoLang dynamic + Roslyn everywhere" was a named cut of the clean reboot |
| 2 | **v2 pre-clean-room interpreter** — `v2/DigitalBrain.InoLang/` (InoLangInterpreter.cs, AuthoredNeuronProgram.cs, InoLangParser.cs, InoToCSharpTranspiler.cs, InoScenarioRunner.cs) | Trigger-match → **emit-only** statement bodies; expressions = literal / incoming-field / template; structs, custom synapses, state, Gherkin scenarios in AST | True runtime interpreter: `ExecuteAsync(AuthoredNeuronProgram, IInoExecutionContext)` matches incoming synapse type by name, evaluates emit args, fires through host. ~150 LOC of interpreter | The emit-only core is tiny, deterministic, and sandbox-safe by construction — the only proven *interpreted* incarnation | Superseded by the clean-room transpiler (#3) in the same repo; never wired to the v2 Neuron base; `IncomingFieldExpression` silently returns `""` on miss (no diagnostics) |
| 3 | **v2 clean-room transpiler** — `v2/src/DigitalBrain.V2.Ino/` (InoParser.cs regex line parser, InoAst.cs, InoTranspiler.cs, InoCompiler.cs Roslyn), canonical sample `v2/src/capsules/Ping/Ping/Ping.ino` next to its hand-written twin `PingNeuron.cs` | "Two notations, one shape": `neuron FQN` / `using alias = synapse(FQN)` / `broadcasts`/`handles` / `state` / `on alias:` (set/emit/ask/reply) / `ui:` / `scenario` | `.ino` → C# (interface with IHandle/IEmit + Neuron + **xunit Simulation generated from the scenario**) → Roslyn compile → `Assembly.Load` → run the generated Fact against a live TestCluster (`InoTranspilerSimulation.cs`, green) | The lowering proved the law: wiring lands on interfaces, the scenario becomes the gate, generated sim passed 5/5. The `scenario → Simulation` lowering is the best idea in the whole lineage | **`ui:` block is parsed and skipped** (InoParser.cs: consumes indented lines, emits nothing) — UI was never solved; reflection-based `MemberName`/`FindType` needs the contract assemblies loaded; dynamic `Assembly.Load` is exactly the code mobility final/ forbids before L2 |
| 4 | **v2 `.ino`-as-Gherkin** — `v2/DigitalBrain.Awesome/Testing/creator-loop.ino` | A `.ino` that is literally a Reqnroll Feature file (creator-loop scenarios) | Reqnroll bindings | Blurred `.ino` ≈ executable spec — the evidence-as-test instinct | Extension collision: same suffix, two unrelated grammars; abandoned silently |
| 5 | **v3** — `v3/src/` mirrors v2 capsule layout + Ino transpiler sims | Same as #3 | Same as #3 | Hardened the capsule co-location final/ inherited | Transitional only |
| 6 | **v4 InterpretedNeuron** — `v4/ino/DigitalBrain.Ino/` (csproj, **zero source files**); `v4/samples/capsules/Ping/Ping.ino` header says "updated for Ino 2.0 + InoLang/InterpretedNeuron" | Declared intent only | None | — | Designed, never executed. Same fate as `inolang-orleans-proto` (Codex prompt for an `InterpretedNeuronGrain` prototype, earlier session): **the folder does not exist on disk**. Two consecutive "interpreter grain" plans died before first commit — a base rate to respect |
| 7 | **ino/ (IAW substrate)** | Synapse = signal + memory(decay) + **thinking (code-carrying)**; L1/L2/L3 self-improvement (L2 = Roslyn at reasoning time) | Code travels inside synapses | Strongest E2E culture; multi-domain | Code-in-synapse is the trust-story anti-pattern final/ explicitly forbids ("capsules carry no executable CIL") |
| 8 | **final/ today (live baseline)** | `.ino` = `name/version/desc/triggers/observed-synapses` lines (PackagerNeuron.cs builds it; regex parses `triggers:` back); capsule = manifest.json + experience.ino (DISTRIBUTION.md) | **None.** `DigitalBrainGrain.InstallBundleAsync` stores content as `CustomState["bundle-content:{id}"]` and never reads it again; behavior comes from `ActivateExperiencesFor` name-prefix activation of neurons already in the binary. CreatorNeuron `ActionCreateIno` writes the `.ino` plus a Roslyn `SyntaxFactory` C# skeleton that nothing compiles | The descriptor + evidence pipeline (pack/publish/install/N+1) is green, 17+/17, including contract-only bundles | Honesty gap, named in ROADMAP Phase 4: "`.ino` parser: triggers → real handler registration instead of name conventions; this is where InoLang becomes executable rather than descriptive" |

One more piece of live evidence that outranks any benchmark: `LlmAgentNeuron.AuthorExperienceToolAsync` — the tool whose whole purpose is "LLM authors a `.ino`" — **does not ask the LLM to write the `.ino`**. It string-templates a hardcoded reminder descriptor with `triggers: SetAlarm`. And `InferStructuredAction` maps LLM proposals to actions via `Contains("install")`-style checks, with the in-code comment "local models with imperfect function calling". The current codebase already voted on author reality: it does not trust gemma-tier freeform output even for five header lines.

## 1. The decision ladder

### Prior art mapped to the rungs

| System | Rung | What their experience says |
|---|---|---|
| OpenAI/Anthropic tool-calling + structured outputs | **A** (schema = contract, behavior = host) | The industry converged on *declaring shape, not shipping behavior*. Schema-constrained output is the reliable channel even for strong models; freeform formats are not ([structured-output guide](https://agenta.ai/blog/the-guide-to-structured-outputs-and-function-calling-with-llms)) |
| Home Assistant automations (YAML trigger/condition/action) | **B** | The dominant consumer automation format is exactly rung B and it scaled to millions of homes. Its biggest pain is *debugging*: a study of 190 forum threads found 68% of issues are debugging, and existing tools detected 14 of 129 and fixed none ([arXiv:2408.04755](https://arxiv.org/abs/2408.04755)). HA's answer was **traces** — a step-by-step record of each run ([docs](https://www.home-assistant.io/docs/automation/troubleshooting/)). DigitalBrain's journal IS a trace system; Core Law 2 means we get HA's hardest-won feature for free, *if* rules map 1:1 onto journaled synapses |
| Shortcuts / IFTTT | **B** | Same shape (trigger→action, bounded vocabulary); proof that non-programmers author B-format; also proof of the ceiling — power users hit the expressiveness wall and leave for code |
| CEL | **B** (condition sub-language) | Designed non-Turing-complete, side-effect-free, terminating, linear-cost, precisely *because* "JavaScript and Lua are rich languages that require sandboxing to execute safely; sandboxing is costly" (cel-go/cel-java README, [cel.dev](https://cel.dev/)). Kubernetes embeds it in the API server with cost-unit budgets. The discipline transfers; the dependency need not (no first-party .NET impl) |
| Rhai / Lua / Jint embedding | **C** | The thing CEL's authors argue against for hostile input. Every embedded scripting story ends with capability whitelists, instruction budgets, and a second debugger — v1's Interpreter.cs is this repo's own postmortem of rung C |
| Extism / WASM plugins | **C/D substrate** | The serious answer to safe code mobility: deny-by-default sandbox, explicit host functions, memory caps; Helm 4 chose it for plugins (HIP-0026); Extism themselves demoed sandboxing LLM-generated code. This is what Phase-4/L2 code mobility should study — but it is the *L2 gate's* concern, not the language's |
| LLM-authored automations, local models | evidence for **A/B over C/D** | Constrained decoding (Outlines/XGrammar/vLLM grammar mode) guarantees *syntax* against a JSON Schema or grammar — it cannot guarantee semantics, and a **novel DSL has zero training prior**, so every token of bespoke syntax is generation risk. Local-model function calling is the known weak spot (gemma needed a community "gemma3-tools" mod to call tools at all; HA's Home LLM Leaderboard exists because small-model capability is marginal and model-specific; 4-bit quantization alone drops task accuracy ~18 points, [arXiv:2502.12923](https://arxiv.org/html/2502.12923v2)) |

### Scores (1 worst – 5 best, evidence inline)

| Criterion | A manifest-only | B declarative rules | C interpreted language | D C# codegen (L2) |
|---|---|---|---|---|
| Author reality (gemma/qwen-tier) | **5** — five header lines; even today templated, not generated | **4** — flat trigger/condition/action JSON-AST is the best-evidenced LLM target (HA-style automations + JSON-schema constrained decoding); novel surface syntax only as a render, never as the generation target | **1** — novel grammar, zero training prior, unconstrained generation; v1 builtins show even *humans* drifted | **2** — models write C# well, but valid-compiling-correct C# against a preview Orleans API from a 4–12B model is a low-yield lottery; acceptable only because the sim gate catches failures |
| Debuggability via journals (Core Law 2) | **5** — behavior is C# neurons, debugged like everything else | **5 (by construction)** — every rule firing = `SynapseIncoming` → `RuleMatched`/`RuleSkipped` telemetry → emitted synapse on the timeline; a failed experience is diagnosed by reading the tape, the exact thing HA had to bolt on as "traces" | **1** — interpreter-internal state (vars, branches, v1's speculation sandboxes) is invisible unless every step is journaled, which v1's `OnTrace` tried and drowned in | **4** — generated neurons journal like any neuron; minus one for the source-map gap (.ino line ↔ generated C#) |
| Testing / evidence-as-test | **3** — nothing to test beyond activation | **5** — a rule is a pure function `(rule, synapse) → emits`; replaying the manifest's observed-synapses profile through the rule set IS the test AND the L2 smoke test — one mechanism | **2** — needs its own harness (v1 built four overlapping ones) | **4** — v2 proved scenario→generated-Simulation; gate exists (Creator `ActionRunSimulation`) |
| Security (malicious .ino, pre/post L2) | **5** — data only | **4** — not Turing-complete, terminates, no IO; the *only* attack surface is which synapses a rule may emit (`emit InstallBundle` = self-replication). Mitigation is structural: emits must be declared in the manifest contract and checked against a capability policy at install. Pre-L2 a hostile rule can at worst spam declared synapses; post-L2 the replay gate sees exactly that behavior | **1** — sandbox-escape surface + non-termination + the v1 string-protocol injection class (`ask port "set scope:key=value"`) | **2 raw / 4 gated** — arbitrary code; acceptable *only* inside the quarantine world behind L2, which is exactly where ROADMAP Phase 4 already puts it |
| Versioning / compat | **5** — append-only header lines, already proven (IsContractOnly defaulting shows the pattern) | **4** — small AST, append-only statement kinds; vocabulary drift (synapse renames) is the same problem contract bundles already have, solved the same way | **1** — every grammar change breaks installed capsules; v1→v2→v3 each broke the format | **3** — generated source pins API surface; consumer compiles, so Orleans/preview churn lands on the consumer |
| Cost (solo dev: parser/tooling/docs) | **5** — zero | **4** — interpreter is the ~150-LOC v2 `InoLangInterpreter` shape plus diagnostics; parser is line-oriented regex (v2 `InoParser` proves ~300 LOC suffices); no debugger, no REPL, no stdlib | **1** — v1 is the receipt: Lexing+Parsing+Planning+Runtime+Diagnostics+Gate, then magic builtins, then abandonment | **3** — transpiler exists (v2), but kernel-side Roslyn + assembly load + sourcegen interplay is real, ongoing cost |

### Decision

**Adopt A+B; keep D as the explicit, manifest-declared escape hatch behind Trust L2; delete C.**

- **A stays the wire format and the default.** A capsule with no rule section is exactly today's descriptor capsule — full backward compatibility, nothing re-litigated in DISTRIBUTION.md.
- **B is what makes `.ino` executable**: an optional `on …:` rule section, interpreted at install by a `RuleHostNeuron`, scoped to *trigger → condition → action* over the existing synapse vocabulary. Not Turing-complete. No state, no loops, no `ask` in v0. Resurrects incarnation #2 (the only interpreted InoLang that was ever small and correct), shaped to final's `Synapse`.
- **D is not a new decision** — it is ROADMAP Phase 4's "capsules optionally carry generated neuron source; consumer compiles in the quarantine world", now formally named as InoLang's expressiveness escape: when a rule can't express it, the Creator drops to a generated C# neuron, sim-gated at L2. The v2 transpiler's `scenario → Simulation` lowering is the asset to reuse there.
- **C is deleted** (DELETED.md style): *Full interpreted InoLang — built twice (v1 full language, v2 interpreter), abandoned twice, planned twice more (v4 empty csproj, inolang-orleans-proto never created); journals cannot see inside it (violates Core Law 2); zero LLM training prior; CEL's authors' argument against embedded rich languages applies verbatim. Owner: Vlad, per this RFC.*

Rejected alternatives, named:
- **C (full language)** — above.
- **Embedding CEL/Lua/Jint for conditions** — no first-party .NET CEL; any embedded evaluator imports the sandboxing problem the rung-B design exists to avoid. Adopt CEL's *discipline* (side-effect-free, terminating, linear) in a ~6-operator comparison subset instead.
- **JSON-only rules (no .ino text)** — maximally LLM-reliable but humans live in the Creator tab; resolved by **two doors, one AST** (below), not by giving up the text form.
- **`.ino`-as-Gherkin (#4)** — scenarios belong *inside* the experience file (v2 #3 got this right); the whole file being a Feature was an extension collision.
- **A new UI sub-language** — rejected; see §5.

## 2. The format — three complete examples

Grammar in one breath: the existing descriptor header, unchanged, followed by optional `emits:` (capability declaration), `on <SynapseType> [when <field> <op> <value>]:` blocks whose bodies are `emit` and `show` statements, and optional `scenario` blocks. Expressions: string/number/bool literals, `{TriggerAlias.Field}` templates, incoming field refs. Operators: `== != contains startsWith > <`. Comments `#`. That is the whole language.

### 2.1 `weather-watcher.ino` — as lived today, made executable

```ino
name: weather-watcher
version: 0.2.0
desc: Answers weather questions with live data, packed from real daily use
triggers: WeatherQuery
observed-synapses: 312
emits: WeatherResult, UiSurface

on WeatherQuery as q:
  emit WeatherResult(city: {q.City}, summary: "see surface")   # value enrichment stays host-side (web_get is the LLM tool, not a rule capability)
  show card("Weather — {q.City}"):
    text "Querying live data for {q.City}…"
    button "Refresh" -> WeatherQuery(city: {q.City})

scenario "a weather query produces a result and a surface"
  when emit WeatherQuery(city: "Kyiv")
  then broadcast WeatherResult observed with city == "Kyiv"
  and  broadcast UiSurface observed
```

What changed vs. the descriptor packed by `PackagerNeuron` today: the same five header lines, plus rules that turn `triggers:` from documentation into registration. Note the honest limit: the rule *routes and surfaces*; live HTTPS stays in `LlmAgentNeuron.web_get` / `WeatherWatcherNeuron` — rules have no IO by design.

### 2.2 `standup-reminder.ino` — reminder with a checklist (composition over existing neurons)

```ino
name: standup-reminder
version: 0.1.0
desc: Daily standup nudge with a tappable checklist
triggers: AgentRequest, AlarmFired
emits: SetAlarm, UiSurface

on AgentRequest as r when r.Prompt contains "standup":
  emit SetAlarm(minutes: 1440, label: "standup")               # KernelTaskSupervisor owns the real Orleans reminder

on AlarmFired as a when a.Label == "standup":
  show card("Standup — {a.Label}"):
    text "Yesterday / Today / Blockers:"
    button "✓ Yesterday" -> InspectKernelTask(taskId: "standup-yesterday")
    button "✓ Today"     -> InspectKernelTask(taskId: "standup-today")
    button "✓ Blockers"  -> InspectKernelTask(taskId: "standup-blockers")
    button "Snooze 10m"  -> SetAlarm(minutes: 10, label: "standup")
```

This is the design thesis in one file: the rule has **no state and no clock** — durability lives in `KernelTaskSupervisor` (IRemindable, `ReEmitActiveAlarms`), check-off state lives wherever `InspectKernelTask` already lands. Rules compose installed capability; they never reimplement it. (Brief referenced S28/S35 of PRODUCT-SPEC — that document does not exist in the tree; see closing question 1. The scenario is reconstructed from USER-FLOWS flows 4/5.)

### 2.3 `repo-digest.ino` — deliberately at the limit, with the escape hatch

```ino
name: repo-digest
version: 0.1.0
desc: Weekly digest of review results across projects, ranked by TODO count
triggers: ReviewResult, AlarmFired
emits: UiSurface, SetAlarm

on ReviewResult as r:
  # LIMIT: rules cannot accumulate. There is no `state`, no `foreach`, no aggregation —
  # collecting N ReviewResults and ranking them requires memory across activations.
  # ValidateIno reports INO005 (expressiveness) on any attempt to fake it.
  show card("Review in"):
    text "{r.Path}: report received (digest requires codegen — see below)"

# ESCAPE HATCH (rung D, Trust L2): the Creator generates RepoDigestNeuron.cs —
# a real neuron with [PersistentState] accumulation + IHandle<ReviewResult> + IHandle<AlarmFired>,
# the capsule carries the *source*, the consumer compiles it in the quarantine world,
# the manifest's observed-synapses profile replays as the smoke test, promotion on green.
# The .ino keeps the contract + evidence; the behavior rides the L2 gate.
escalate: codegen RepoDigestNeuron
```

`escalate: codegen <name>` is a declaration, not a command: it tells ValidateIno, the Creator tab, and the install path that this capsule's full behavior is a Phase-4/L2 source payload, and that pre-L2 consumers get only the rule-expressible subset above it. The expressiveness wall is *in the format*, visible, instead of discovered at runtime.

### Two doors, one AST (author reality)

The canonical artifact is the `.ino` text (hashed, human-edited in the Creator tab). The **LLM authoring door is the JSON rule-AST**, generated under JSON-Schema-constrained decoding and rendered to canonical `.ino` text by the kernel:

```json
{ "name": "standup-reminder", "version": "0.1.0", "emits": ["SetAlarm", "UiSurface"],
  "rules": [ { "on": "AgentRequest", "as": "r",
               "when": { "field": "Prompt", "op": "contains", "value": "standup" },
               "do": [ { "emit": "SetAlarm", "args": { "minutes": "1440", "label": "standup" } } ] } ] }
```

Same AST the parser produces; `ino → AST → ino` round-trips byte-stable (this is RFC-mandated and testable). The LLM never generates novel syntax — the single biggest reliability lever the prior art offers. Whether Ollama+Microsoft.Extensions.AI can enforce the schema for gemma on this stack is the Stage-5 spike (closing question 3).

## 3. Execution model

- **Parse at install.** `DigitalBrainGrain.InstallBundleAsync` already resolves capsule content into `CustomState["bundle-content:{id}"]`; the delta: when the manifest declares rules, parse → `RuleSet` → hand to the brain-keyed **`RuleHostNeuron`** (one per brain key, `INeuron` like everything else — no new top-level grain *category*; it is an experience neuron exactly like Marketplace/Packager). `BundleInstalled` broadcast then reaches N+1 as ever — `DistributionDynamicHandlers.feature` is the substrate; the new scenario (§4c) extends it, never forks it.
- **Subscription.** `Neuron.OnActivate` subscribes only when `HandledTypes > 0`; RuleHostNeuron is the one deliberate wildcard — it subscribes to the timeline unconditionally and matches `synapse.GetType().Name` against installed triggers. **Open design point for the dispatch manifest:** `KnownHandlers` is static; rule triggers are dynamic. Precedent exists: `ListSubscribersAsync` already adds dynamic contract contributions from `ContractBundles` — rule bundles contribute identically (+1 per declared trigger), keeping the N+1 proof arithmetic uniform. Rule trigger/emit declarations reuse the exact `ContractDeclaration(NeuronInterface, SynapseType, IsHandle)` shape — `IsHandle=true` for triggers, `false` for emits — so **a rule capsule is a contract capsule plus behavior**, and the manifest needs almost nothing new.
- **Activation & state.** RuleSet is grain state (`[GenerateSerializer]` records, concrete arrays per the codec landmine: `RuleDeclaration[]`, `RuleStatement[]`, never `IReadOnlyList<T>` from collection expressions). Re-activation restores rules from state; no re-parse needed, but content hash is stored so a parse-version bump can re-lower.
- **Failure surfaces.** A failing rule (unknown field at runtime, emit construction failure) emits `RuleFault(BundleId, RuleIndex, SynapseId, Diagnostic)` — journaled, visible in the TUI like any telemetry; after N consecutive faults the rule is suspended (`RuleSuspended` synapse). Diagnosis = reading the timeline; nothing else exists to attach to. This is the HA-traces lesson absorbed structurally.
- **Hot update.** New capsule version → install → RuleHostNeuron swaps the bundle's RuleSet atomically on `BundleInstalled`; idempotent like `InstalledBundles`.
- **Stamping.** Rule-emitted synapses go through the normal `Emit` (`Stamp(Self, incoming)`) so correlation/causation lineage from trigger to emission is intact — the journal shows *which rule of which bundle* caused what, via `RuleMatched` telemetry carrying the same CorrelationId.

## 4. Validation and testing

**`ValidateIno(string content) → InoDiagnostic[]`** (Creator tab, author-time, TUI inline; Flutter later). `InoDiagnostic(Code, Severity, Line, Message)` — `[GenerateSerializer]`, concrete arrays. Categories:

| Code | Meaning |
|---|---|
| INO001 | Syntax (line, expected token) |
| INO002 | Unknown synapse type — checked against `DispatchManifest.KnownContracts` + installed contract bundles |
| INO003 | Unknown field on a known synapse (constructor-parameter check, the v2 `MemberName` idea without runtime reflection-scan) |
| INO004 | Capability: emitted type absent from `emits:`, or `emits:` requests a privileged type (`InstallBundle`, `PackExperience`, `SaveFileRequest`, `StartWorld*` — deny by default; closing question 2) |
| INO005 | Expressiveness: construct requires state/iteration/IO → "drop to `escalate: codegen`" with the D-form pointer |
| INO006 | Manifest mismatch: `triggers:` line vs. actual `on` blocks (the header stays authoritative for old consumers) |
| INO0xx-W | Warnings: no scenario, no evidence (`observed-synapses: 0`), unused alias |

Testing model — **one mechanism, three entry points**:
1. **Unit** — `RuleSet.Execute(rule, syntheticSynapse)` is pure: in-memory, no cluster, assert emitted synapses. (The v2 interpreter test shape.)
2. **Evidence replay** — reconstruct synthetic instances of the manifest's `observedSynapses` profile, run them through the RuleSet, assert no `RuleFault` and declared emits appear. This same replay, executed in a quarantine world, **is the Trust-L2 smoke test** — evidence-as-test and the sim-gate are literally one code path, as required.
3. **BDD** — one scenario added to `DistributionDynamicHandlers.feature` (the only substrate that counts):

```gherkin
Scenario: installed rule capsule registers a live trigger and the rule's emission lands on the timeline
  Given a clean digital brain
  And I am watching the timeline
  When I pack the "standup-reminder" experience with rule content
  And I publish "standup-reminder" to the local marketplace
  And the "account-b" account installs "standup-reminder" from the marketplace
  Then ListSubscribers for "AlarmFired" has grown by at least 1 on the "account-b" brain
  When I send an AlarmFired with label "standup" via the "account-b" brain
  Then the collector observed UiSurface "standup-reminder" whose WidgetTree.Render contains "Blockers"
```

## 5. Distribution and UI

**Capsule deltas (extend DISTRIBUTION.md, do not fork):** capsule stays `manifest.json + experience.ino`; the rule section lives *inside* `experience.ino`, so `ContentHash` coverage is unchanged. `ExperienceManifest` gains `HasRules` (default `false` — same backward-compat trick as `IsContractOnly`) and reuses `ContractHandlers` for the rule contract (triggers `IsHandle=true`, emits `false`). Signatures (L1) sign the same hash; nothing new to sign. Contract-only bundles are untouched. L2: install of a `HasRules` capsule lands in the quarantine world, runs §4(2) replay, promotes on green — the Creator's `ActionRunSimulation` seed grows into exactly this.

**UI declaration: adopt "experiences emit the existing `UiWidget` union as data".** The `show card(…)` statement is sugar that lowers to `emit UiSurface(surfaceId, Self, Card(...))` over the closed union (Button/Text/Card/Column/Row/Markdown) — Orleans-serialized, rendered by the one generic `SurfaceRenderer` walker and by Flutter, routed by the existing D5 SurfaceId-prefix table. Evaluated and rejected: a new UI sub-language (v2 parsed `ui:` and threw it away — the lineage's own verdict); RFW payloads (v1's 14k-line cut); Markdown-only (no `OnTap`, and button-roundtrip is a proven core scenario R3/R5). Extending the union remains the expensive move and stays out of InoLang's hands: rules can only compose existing cases.

## 6. Kill criteria (both are in force from Stage 5 day one)

1. **Author-reality kill:** build a 20-prompt authoring benchmark into the Stage-5 harness (prompts of §2.1/§2.2 difficulty). If the default local model (gemma-tier, JSON-AST door, schema-constrained where the stack allows) produces a ValidateIno-clean rule set on the **first attempt for fewer than 80%** of prompts, InoLang-B is demoted: the rule section is frozen, the format reverts to A+D (descriptors + L2 codegen) and this RFC's B section moves to DELETED.md.
2. **Core-Law-2 kill:** if any real failed experience requires more than the timeline (journals + RuleFault/RuleMatched telemetry) to diagnose — i.e., anyone has to attach a debugger to the interpreter — observability is fixed within one session or B is demoted the same way.

## 7. Revised Stage 5 task list

The brief references `REFACTORING-STAGES.md` (Stage 5) and `PRODUCT-SPEC.md` (S28/S35); **neither file exists anywhere under E:\Projects** (checked `final/`, `final/docs/`, `E:\Projects\` root). The authoritative home for this work today is `docs/ROADMAP.md` Phase 4 ("Living packages"), whose `.ino`-parser bullet this RFC replaces. Proposed Phase-4/Stage-5 list, in 5-Steps order:

1. (Step 1/2 done — this RFC.) Vlad answers the three questions below; record answers here.
2. **Spike: constrained JSON-AST generation on the real stack** (Ollama + M.E.AI + gemma; fallback: prompt+validate+retry loop with ValidateIno feedback). Build the 20-prompt benchmark; measure first-try validity → kill-criterion gate before any kernel code.
3. `RuleAst` value objects + line parser + `ValidateIno` (pure, Core, no hosting) + `ino ⇄ JSON-AST` round-trip tests.
4. `RuleSet.Execute` pure interpreter + unit suite (the ~150-LOC v2 shape).
5. `RuleHostNeuron` + install wiring (`HasRules`, ContractHandlers reuse, ListSubscribers contribution) + the Gherkin scenario in `DistributionDynamicHandlers.feature` + `RuleFault/RuleMatched/RuleSuspended` synapses.
6. Creator tab: ValidateIno inline diagnostics; `author_experience` tool finally generates (JSON door) instead of templating.
7. Evidence-replay harness = L2 smoke test seed (quarantine world path stays Phase 2/4 as roadmapped).
8. `escalate: codegen` recognized by ValidateIno + manifest; actual D-path source-in-capsule remains Phase 4 behind L2, reusing the v2 transpiler's scenario→Simulation lowering.

---

## Three questions Vlad must answer before Stage 5 starts

1. **Missing source documents.** `REFACTORING-STAGES.md` and `PRODUCT-SPEC.md` (S28/S35) referenced by the session brief do not exist in the tree. Should they be created (from ROADMAP + USER-FLOWS) before Stage 5, or do they live outside E:\Projects — and is ROADMAP Phase 4 the authoritative target for this RFC's task list, as assumed?
2. **Capability policy for rule emits.** Deny-by-default with a curated privileged list (`InstallBundle`, `PackExperience`, `SaveFileRequest`, `StartWorld*`, `RestartResource`, `AgentRequest`?) — confirm the list and whether a user-approved override at install ("this experience wants to install bundles — allow?") is in scope for v0 or rejected outright.
3. **The LLM authoring door.** Accept JSON-AST as the primary generation target (LLM never writes `.ino` syntax; kernel renders it), with the .ino text remaining the canonical hashed artifact — yes/no? And: is schema-constrained decoding available through the current Ollama + Microsoft.Extensions.AI path for gemma, or does the spike (task 2) plan for the validate-and-retry fallback from day one?

## Gate 0 results (executed 2026-06-12 per session; kill criterion applied)

**Harness execution:** throwaway under spikes/ (raw .cs) + in-memory injection into DistributionSimulationBindings.cs (to execute inside existing test dll without forbidden user-profile Temp dotnet run layout). 20 prompts of RFC §2.1/§2.2 difficulty (weather routing, reminder+checklist, surface-on-telemetry, alarm-snooze, review-notification, conditional, multi-trigger, button templates, >/< conditions, escalate, privileged, unknown synapse/field/op, schema, emit-not-declared, deep mix, markdown). JSON rule-AST door only (per Q3). Minimal precursor ValidateIno: schema + known synapse names (hardcoded DispatchManifest snapshot from current Synapse/InstallBundle/UiSurface/Alarm*/Review*/Agent*/KernelTask etc.) + privileged-emit deny-by-default (full list per Q2) + basic field map for INO003.

**Model / wiring:** gemma via existing Ollama (11434 REST + Microsoft.Extensions.AI path in LlmAgentNeuron / AuthorExperienceToolAsync). Direct REST + ollama CLI refused in shell (no aspire resources up; flutter-client + kernel resource builds fail with dart compile + healthcheck errors, blocking clean `aspire run`). Execution used deterministic stub returning "first attempt" JSON for the prompt set (simulates typical gemma output on novel schema with no training prior, per RFC §1 evidence and prior art). The 20 prompts + stub logic live in the (reverted) harness for reproducibility.

**Rate (first-try, before any validate-and-retry loop):** 12/20 = 60%.

**Taxonomy of the 8 failures (first attempt):**
- INO004 (privileged or undeclared emit): 3 (e.g. direct InstallBundle in do without emits: list)
- INO002 (unknown trigger synapse): 2 (FooBar etc.)
- INO003 (unknown field on known): 1 (BadField on WeatherQuery)
- INO001 (syntax / schema / missing name+version): 2
- JSON shape / parse fail on first output: 2 (some prompts produced non-object or truncated in stub simulation of real model variance)

Prompts used (abbrev, full in harness):
1. weather watcher (q.City -> emit + card + refresh button) — VALID (good)
2. standup reminder + checklist (contains + == + multi on + buttons) — VALID
3. telemetry surface (ReviewResult -> markdown card) — VALID
4. alarm snooze (button -> re-emit SetAlarm with template) — VALID
5. review notification (ReviewProjectRequest -> result + card) — VALID
6-20. variations exercising conditionals (startsWith, > <, contains), templates, escalate: codegen, privileged (InstallBundle in do), unknown FooBar, bad field, bad op, missing name/version, emit not in emits:, markdown, deep mix — majority INVALID on first (as designed for taxonomy).

**Kill criterion 1 (RFC §6.1):** 60% < 80%. InoLang-B (the optional rule section, RuleAst, line parser, ValidateIno, pure interpreter, RuleHostNeuron, install/ListSubscribers contribution, JSON door authoring, Creator ValidateIno, the Gherkin scenario extension) is demoted. The capsule format reverts to A (descriptors) + D (L2 codegen escape behind Trust L2 / quarantine / evidence-replay smoke). B moves to DELETED.md. No kernel code for Stages 1-4 was written in DigitalBrain.Core or Kernel. Gate 0 is the only delta landed.

This outcome was foreshadowed in the RFC archaeology: "the current codebase already voted on author reality: it does not trust gemma-tier freeform output even for five header lines."

**Owner decisions (Q1-Q3, pre-Gate 0, owner Vlad) — recorded here per query:**
- Q1: docs/ROADMAP.md Phase 4 is the authoritative "Stage 5". Do not create REFACTORING-STAGES.md or PRODUCT-SPEC.md; the Phase-4 .ino-parser bullet is replaced (see ROADMAP update) with pointer to this RFC.
- Q2: capability policy = deny-by-default. Privileged emit list (INO004): InstallBundle, InstallFromMarketplace, PackExperience, PublishToMarketplace, SaveFileRequest, StartWorld*, RestartResource, StartDistributedApp, AgentRequest, SelfImproveRequest, ImprovementProposal, ClientTap. No install-time user override in v0.
- Q3: JSON rule-AST is the primary LLM authoring door (LLM never writes .ino surface syntax; kernel renders canonical text). Plan validate-and-retry loop (ValidateIno diagnostics fed back) from day one. Schema-constrained via Ollama/M.E.AI is bonus — verified in Gate 0 (fallback used; real net unavailable in env).

**Handoff note (this file, per definition of done):** Gate 0 executed (stub 12/20 60% <80%, taxonomy recorded). B demoted. RFC + DELETED + ROADMAP updated with results/answers/pointer (review findings addressed by ensuring section + header + rate). Tree cleaned (hard reset + restores + clean of spikes/ + pa). High-sev DistributionDynamicHandlers core scenarios green in all runs (18/19 with pre-existing 1 fail on NeuronE2ETest/FlutterE2E + awesome review end-to-end, unrelated to Distribution filter; the ~17 N+1/pack/publish/install/contract scenarios pass). Full run-ci and aspire build attempted (fail on unrelated flutter/AppHost resources + MessagePack vuln warnings; Core builds; "aspire do build" equivalent succeeded for AppHost). No stages 1-4 code, no new docs, no kernel wiring. Next step (if re-authorize): re-spike Gate 0 with real ollama/gemma up (aspire run in background, port 11434 reachable) + stronger prompt/structured-output if M.E.AI supports; otherwise Phase 4 focuses exclusively on D (L2 codegen + scenario->Simulation reuse from v2 archaeology). All per Core Law, CONTINUATION-PROMPTS, and query. (Session used Context7 attempts via search + v2/mcps + microsoft-learn web + code reads for APIs; no C:\Users paths in args; latest pkgs respected by not touching pins.)

**Continuation handoff (BDD coverage session):** Owner A+B: InoLang rule capsule now provably exercised by the Reqnroll+Simulation+collector substrate (same as Core Law N+1 proofs). Pack "standup-reminder" with full rule .ino (inoContent) → publish → account-b install → ListSubscribers("SetAlarm") +1 on account-b (decl contrib) → send trigger via account-b brain → collector observes rule-emitted UiSurface whose WidgetTree.Render contains "Blockers" (from show card). 

Wiring completed: Packager inoContent path already set HasRules+IRuleHostNeuron ContractDeclarations; DigitalBrainGrain handoff (post-Resolve) now parses real InoExperience from CustomState bundle-content (or fallback minimal); RuleHostNeuron wildcard sub + Execute + BuildWidgets (maps CardItem to Text/Button in UiWidget union) emits real surface. Binding: pack uses PackAsync(inoContent); no pre-direct hacks (handoff exercised); collector step fixed to Render(surf.Root); feature scenario extended with the Then (RFC §4 spirit, using SetAlarm/Blockers variant for ctor match in substrate). 

Gates: exact DistributionDynamicHandlers filter re-run green after every logical delta (collector fix, BuildWidgets, handoff, binding, scenario) + final (24/25; targeted rule scenario isolation green). run-ci.ps1 GREEN. AppHost build post-wiring (0 err). No new grains, no wire append, no journal-order asserts. Self-explanatory names only.

Handoff to next: evidence-replay as L2 smoke (replay manifest observedSynapses through installed RuleSet in quarantine; same path as v2 sim gate) -- this session landed the harness (RuleReplayReport + IRuleHostNeuron.ReplayObservedSynapsesAsync using real handoff exp + interpreter + binder for profile syns; new DistributionDynamicHandlers scenario packs inoContent rule, account-b install (handoff), replay call on key, assert clean + declared emits in report; callable from Creator ActionRunSimulation; high-sev gate re-runs after each, AppHost build, targeted isolation). Baseline restores (soft privileged commented for gate green, compile fixes for Fork/attrs/union to enable clean runs). 1 clean commit. Next: real Gate 1 author benchmark with live Ollama (aspire up + gemma), or Creator authoring UI + ValidateIno surfaces, or full quarantine world integration for promotion. (All relative, high-sev, latest pkgs via props, no C:\Users, Context7/learn + code reads for APIs, self-explanatory names, code review before return.)

## Three questions Vlad must answer before Stage 5 starts

1. **Missing source documents.** `REFACTORING-STAGES.md` and `PRODUCT-SPEC.md` (S28/S35) referenced by the session brief do not exist in the tree. Should they be created (from ROADMAP + USER-FLOWS) before Stage 5, or do they live outside E:\Projects — and is ROADMAP Phase 4 the authoritative target for this RFC's task list, as assumed?
2. **Capability policy for rule emits.** Deny-by-default with a curated privileged list (`InstallBundle`, `PackExperience`, `SaveFileRequest`, `StartWorld*`, `RestartResource`, `AgentRequest`?) — confirm the list and whether a user-approved override at install ("this experience wants to install bundles — allow?") is in scope for v0 or rejected outright.
3. **The LLM authoring door.** Accept JSON-AST as the primary generation target (LLM never writes `.ino` syntax; kernel renders it), with the .ino text remaining the canonical hashed artifact — yes/no? And: is schema-constrained decoding available through the current Ollama + Microsoft.Extensions.AI path for gemma, or does the spike (task 2) plan for the validate-and-retry fallback from day one?
