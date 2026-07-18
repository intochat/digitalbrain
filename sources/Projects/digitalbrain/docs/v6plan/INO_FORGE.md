# DigitalBrain — Ino Forge (v6 research + vision memo)

> Status: **proposal / research memo**, not yet canonical. Builds on
> `docs/v5plan/VISION.md` (v5 "The Cut"). Where this memo and v5 conflict,
> v5 wins until this is promoted. Written 2026-05-28.
>
> This memo answers one brainstorm in three parts:
> 1. **Is NVIDIA Omniverse / Cosmos the right tool to "learn and simulate
>    `.ino` files and run scheduled kernel tasks"?** (Short answer: no for
>    Omniverse/Cosmos, yes for NeMo/NIM. Section 2.)
> 2. **What is the genuinely useful architecture** for durable scheduled
>    task creation, self-authoring neurons, and a unified type system where
>    synapses *are* data types? (Sections 3–6.)
> 3. **The final unified vision** and the cut list. (Sections 7–9.)

---

## 1. TL;DR

**The honest NVIDIA verdict (read this first).**

- **NVIDIA Omniverse and Cosmos are the wrong tools for this.** They simulate
  the *physical world in 3D / video* (robots, autonomous vehicles, digital
  twins). An `.ino` file is a **program**, not a 3D scene or a physics
  rollout. "Simulating an `.ino`" means *deterministically executing its
  scenarios in the Orleans interpreter* — which the repo already does
  (`ScenarioRunner` + `Interpreter`). Routing that through a world-foundation
  model is a category error. On top of that, Cosmos-Predict-7B needs ~**80 GB
  VRAM** (H100; ~39 GB with aggressive offload) — not feasible on a normal
  workstation. So even if it fit conceptually, it doesn't fit the hardware.
- **The right NVIDIA stack is NeMo + NIM + Nemotron.** If you want to *train
  your own Ino*, NeMo is the open-source fine-tuning framework, NIM is the
  self-hostable OpenAI-compatible inference container, and Nemotron weights
  are free even in production. Critically, **NIM speaks the OpenAI wire
  protocol**, so a self-hosted Ino drops straight into the existing
  `OpenAiProviderFactory` / keyed-`IChatClient` path with *zero new
  abstraction*. (`sdk/DigitalBrain.SDK/Ai/Llm/Providers/OpenAiProviderFactory.cs`.)
- **Free-for-local?** NeMo: yes (Apache-2.0, runs locally). Nemotron weights:
  yes (free, commercial use, from Hugging Face). NIM: free for NVIDIA
  Developer Program members for dev/eval (up to 16 GPUs); production needs AI
  Enterprise ($4,500/GPU/yr) — but you can sidestep NIM entirely and serve the
  same fine-tuned weights with **Ollama or vLLM**, both already compatible
  with the repo's provider model (`OllamaProviderFactory`). Cosmos: weights
  are free (NVIDIA Open Model License) but irrelevant here. Omniverse: free
  for individuals, RTX GPU required — also irrelevant here.

**The vision in one paragraph.** *Ino* becomes a self-improving authoring
loop. A small open model, optionally fine-tuned on the corpus of `.ino` files
and their compile/scenario outcomes, authors `.ino`. The kernel runs the
**author → compile → simulate → gate → activate** cycle as a **durable
scheduled task** (the Accede `DurableTaskCompletionSource` pattern on Orleans
Journaling), so neuron-creation survives silo restarts and can run on a
schedule or in the background. Once a behavior goes green, its `.ino` is
**transpiled to strongly-typed C#** — promoting the existing
`InoToCSharpTranspiler` from a test-artifact generator into a real emitter — so
**synapses and neurons become first-class C# types** you can hold, send, and
compose from the SDK. Because **a synapse *is* the data schema**, the type
system is already unified: TDD a scenario, materialize the synapse record, and
the default C# scalar types are covered out of the box while authors can
declare their own composite types inline.

---

## 2. The NVIDIA research, in detail

### 2.1 What each product actually is

| Product | What it is | What it's *for* | Fit for "learn/simulate `.ino` + run kernel jobs" |
|---|---|---|---|
| **Omniverse** | OpenUSD-based 3D simulation / digital-twin platform + RTX renderer | Robotics, factories, 3D digital twins, synthetic 3D data | **No.** It's a 3D scene engine. A neuron graph is not a 3D scene; rendering one in USD is pure visual gloss and violates the v5 Cut. |
| **Cosmos (WFM)** | Generative *world foundation models* — produce physics-aware **video / future-world-state** | Physical AI: train robots & AVs on synthetic rollouts | **No.** It predicts the physical future as video. `.ino` execution is deterministic symbolic interpretation; there is no "world" to roll forward. Also ~80 GB VRAM. |
| **NeMo** | Open-source (Apache-2.0) framework to build/customize/**fine-tune** LLMs (SFT, LoRA, PEFT) | Training/customizing generative models | **Yes — this is the one.** Use it to fine-tune *Ino* on the `.ino` corpus + gate outcomes. |
| **NIM** | Self-hostable, GPU-accelerated **inference microservice** container; OpenAI-compatible API; TensorRT-LLM/vLLM/SGLang backends | Serving (custom or pretrained) models locally or in cloud | **Yes (optional).** Serves a fine-tuned Ino behind the *existing* OpenAI provider path. Replaceable by Ollama/vLLM if you want to skip NIM licensing. |
| **Nemotron** | NVIDIA's open foundation models (reasoning/agentic) | A free base model to fine-tune or serve | **Yes (optional base).** Free weights, commercial use, from Hugging Face. |

### 2.2 The category error, stated plainly

The word "simulate" is doing two unrelated jobs:

- **Physical simulation** (Omniverse/Cosmos): "given this 3D world and these
  forces, what does the next second of video look like?"
- **Program simulation** (what you need): "given this `.ino`'s scenario
  `when synapse ask(text: "t") then signal ready emitted`, does the
  interpreter produce the expected synapse?"

The repo already does program simulation: `.ino` is lexed → parsed → linked →
lowered to an `ExecutionPlan` → executed by `Interpreter`
(`inolang/DigitalBrain.InoLang/InoCompiler.cs`,
`inolang/DigitalBrain.InoLang/Runtime/Interpreter.cs`), and scenarios are run
red→green by the Creator's scenario runner. **No GPU world-model is involved
or helpful.** The "simulator" you want is a deterministic, fast, in-process
Orleans sandbox — and durable scheduling on top of it.

### 2.3 Free-for-local summary

- **NeMo** — Apache-2.0, free, runs locally on your own GPU. ✅
- **Nemotron weights** — free, commercial use, Hugging Face. ✅
- **NIM** — free for Developer Program members (dev/eval, ≤16 GPUs); production
  = AI Enterprise $4,500/GPU/yr. Local Docker requires NVIDIA Container Toolkit
  + recent CUDA. ⚠️ (Avoidable: serve the same weights via Ollama/vLLM, free.)
- **Cosmos weights** — free (NVIDIA Open Model License, commercial OK), Apache-2.0
  source; but **~80 GB VRAM** for the 7B and off-mission. ❌ for this project.
- **Omniverse** — free for individuals; RTX (Turing+) GPU; Docker needs NVIDIA
  Docker ≥ 23.0.1 + CUDA ≥ 12.6.1. Off-mission. ❌ for this project.

**Recommendation:** do not pull in Omniverse or Cosmos. If/when you train Ino,
use NeMo locally for fine-tuning and serve via Ollama or vLLM (free) — keeping
NIM as an optional production swap, since all three are OpenAI-compatible and
slot into the current provider abstraction unchanged.

---

## 3. Where the codebase is today (the honest baseline)

So the plan stays grounded, here is what already exists (don't rebuild it):

- **`.ino` is interpreted, not compiled to C#.** Pipeline: Lexer → Parser →
  Linker → Lowering → `ExecutionPlan` → `Interpreter`
  (`inolang/DigitalBrain.InoLang/InoCompiler.cs:42`,
  `…/Runtime/Interpreter.cs:33`). Strong typing happens at *link* time via
  `IContractCatalog`; unknown LLM-authored schemas defer through
  `DeferredContractCatalog`.
- **A transpiler exists but only emits test artifacts.**
  `sdk/DigitalBrain.SDK/INO/InoToCSharpTranspiler.cs:174` compiles the `.ino`
  to verify, then emits **Gherkin + Reqnroll step bindings** — *not*
  executable C# neurons. This is the seam we promote in §5.
- **Synapses are records; they already *are* the schema.**
  `abstract record Synapse` with `SynapseMetadata` headers
  (`kernel/DigitalBrain.Runtime/Neurons/Synapse.cs:49`). FQN registry pattern
  via `*SynapseTypes` (`sdk/DigitalBrain.SDK/Ai/AiSynapseTypes.cs:5`). There is
  **no separate "data type" concept** — a synapse record's init properties are
  its schema.
- **Neurons are durable grains.** `Neuron : DurableGrain`
  (Orleans.Journaling) with `IDurableList<Synapse>` incoming/outgoing journals
  (`kernel/DigitalBrain.Runtime/Neurons/Neuron.cs:13`), `IHandle<TSynapse>`
  (`…/IHandle.cs:3`), `INeuronMetadata` (`…/INeuronMetadata.cs:3`).
- **Scheduling today = Orleans reminders + grain timers.**
  `ScheduledReminderGrain` uses `RegisterOrUpdateReminder` + `IRemindable` +
  `RegisterGrainTimer` + `[PersistentState]`
  (`kernel/DigitalBrain.Kernel/Runtime/ScheduledReminderGrain.cs`). There is
  **no durable-task / completion-source scheduling** yet.
- **Creator already does author→compile→scenario→gate→activate.**
  `InoCreatorNeuron` + `InoAuthoringLoop` resolve a **keyed `IChatClient`** by
  model key, prompt with `CreatorInoSystemPrompt`, compile, run scenarios, and
  on green persist + hot-register + broadcast
  (`kernel/DigitalBrain.Kernel/Creator/InoAuthoring/InoAuthoringLoop.cs:48`).
- **LLM providers:** OpenAI (active), Anthropic (stub), Ollama, Grok via
  `ILlmProviderFactory`. **No training/fine-tuning** anywhere.
- **SDK C# surface:** `NeuronBuilder` / `ProgrammaticNeuron` fluent API and
  `INeuronExecutionContext` (`EmitAsync` / `AskAsync`)
  (`sdk/DigitalBrain.SDK/NeuronBuilder.cs`).
- **Orleans:** `10.1.1-preview.1`, with `Microsoft.Orleans.Journaling`
  `10.1.1-preview.1.alpha.1`, Reminders, Persistence.Memory, Clustering.Redis
  (`Directory.Packages.props`).

**Takeaway:** ~70% of the "Ino Forge" already exists. The four missing pieces
are (a) durable scheduled tasks, (b) trained Ino, (c) real `.ino`→C# emission,
(d) inline data-type declarations. Each is additive, not a rewrite.

---

## 4. Durable scheduled tasks in the kernel (the Accede pattern)

This is the load-bearing new capability: **the kernel must schedule tasks
(e.g. "author this neuron", "run this job nightly") that survive silo
restarts and resume exactly where they left off.** Accede shows the pattern on
the latest Orleans.

### 4.1 What Accede does

- `Agent : DurableGrain` (`src/System.Distributed.AI.Agents/Agent.cs`) — a
  journaled grain; all state persists through Orleans.Journaling.
- `async DurableTask<T>` methods are **auto-scheduled durably**; the runtime
  writes task state to the journal **before** executing
  (`src/Orleans.DurableTasks/Runtime/DurableTaskGrainRuntime.cs:146`,
  `_storage.WriteAsync()` precedes invocation).
- `IDurableTaskCompletionSource<T>` is a **durable `TaskCompletionSource`**: a
  grain awaits `completion.GetResult()` and that await **survives
  deactivation/reactivation** (`src/Accede.Service/Grains/DurableTaskCompletionSourceGrain.cs`,
  `src/Accede.Service/Agents/AdminAgent.cs:21`).
- **No reminders/timers** — pure await-based durable scheduling.
- Recovery is automatic: journal replay reconstructs state before
  `OnActivateAsync`; in-flight tasks resume; awaiters resume. The recovery
  table: crash *before* `WriteStateAsync` → clean rollback to last commit;
  crash *after* → fully durable.

### 4.2 Packaging reality (important)

Accede **vendors** `Orleans.DurableTasks` and `System.Distributed.DurableTasks`
as *local projects* — they are not (yet) on nuget.org. DigitalBrain is on
Orleans `10.1.1-preview.1` + Journaling alpha but does **not** reference the
DurableTasks libraries. Two adoption paths:

- **Path A (minimal, recommended first):** build a thin
  `IDurableTaskCompletionSourceGrain<T>` on the *existing* `DurableGrain` base
  the repo already uses. You get crash-survivable "await this result" without
  the full `[DurableTask]` scheduler. This is enough for the Creator loop and
  scheduled jobs.
- **Path B (full):** vendor Accede's `Orleans.DurableTasks` +
  `System.Distributed.DurableTasks` projects (or track the official preview
  when it ships) to get `async DurableTask<T>` auto-scheduling. Defer until
  Path A's limits actually bite.

> ⚠️ Journaling is **alpha** in both repos. Treat durable-task scheduling as a
> preview-grade dependency; pin versions and keep the reminder-based
> `ScheduledReminderGrain` as the fallback for plain wall-clock schedules.

### 4.3 Two distinct scheduling needs — keep them separate

1. **Wall-clock / cron schedules** ("run nightly", "in 10 minutes") →
   **reminders** (`ScheduledReminderGrain` already does this; keep it).
2. **Durable in-flight work** ("author this neuron; survive a restart
   mid-LLM-call"; "wait for human approval indefinitely") →
   **`DurableTaskCompletionSource`** (new, §4.1).

A scheduled job is then: a *reminder fires* → it *starts a durable task* →
the durable task drives the Creator loop and is awaitable/recoverable. The two
compose; neither replaces the other.

---

## 5. Ino: the trained assistant, and `.ino` → strongly-typed C#

### 5.1 Training Ino (only if you want a local/owned model)

The Creator already calls a keyed `IChatClient`. "Train our own Ino" =
fine-tune a small open model so it authors valid `.ino` first-try more often,
then register it as just another keyed model:

1. **Dataset = your own gate telemetry.** Every Creator run already produces
   (intent → `.ino` → compile errors → scenario pass/fail). That is a labeled
   SFT dataset for free. Capture it (the durable task journal in §4 is the
   natural place to persist these traces).
2. **Fine-tune with NeMo** (local, Apache-2.0) on a small base (Nemotron or
   any open 7–8B). LoRA/PEFT is enough; this is a narrow grammar task.
3. **Serve via Ollama or vLLM** (free, OpenAI-compatible) — or NIM if you want
   the NVIDIA-supported container. Register the endpoint as a keyed model;
   `OpenAiProviderFactory` consumes it unchanged.
4. **Close the loop:** new gate traces → periodic re-fine-tune (a *scheduled
   durable task*, §4). Ino measurably improves at authoring `.ino` over time.

> This is the *only* place NVIDIA tech earns its keep — and even here it's
> optional and swappable. Start with a hosted/`Ollama` model; fine-tune only
> when first-try gate-pass rate is the bottleneck.

### 5.2 Promote the transpiler: `.ino` → real C#

Today `InoToCSharpTranspiler` emits Gherkin + Reqnroll. The vision needs it to
emit **executable, strongly-typed C#**: a `record` per declared synapse/data
type, a `Neuron` subclass with `IHandle<T>` per handler, and the
`*SynapseTypes` FQN constants. Flow:

```
.ino  ──InoCompiler.Compile()──▶  ExecutionPlan (verified, green)
      ──InoToCSharpTranspiler──▶  Synapse records + Neuron class + SynapseTypes
      ──RoslynCompiler──────────▶  loadable assembly  ──▶  registry / activation
```

This makes the v5 invariant **"one `.ino` per behavior"** real while *also*
giving hand-written C# (the SDK) concrete types to reference. Interpretation
stays the fast path for hot-authored neurons; transpilation is the
"graduate to typed C#" path for behaviors that have stabilized.

### 5.3 Synapses *are* data types — the unified TDD type system

You already have the key insight: **a synapse is a data type.** So unify
around it instead of inventing a parallel "schema" concept:

- **Default scalars are covered:** map `.ino` primitives (`text`, `int`,
  `bool`, `datetime`, `decimal`, `list<T>`, `map<K,V>`) to their C# equivalents
  once, in the transpiler. This is the "cover the default C# types" piece.
- **Authors declare custom types inline** in the `.ino` (today they can only
  *reference* existing C# contracts via `IContractCatalog`). A `type`/`synapse`
  declaration block transpiles to a `record`. This satisfies "let others create
  their own data types."
- **TDD is native:** the `.ino` `scenario` block already defines behavior by
  example (`when synapse ask(text: "t") then signal ready emitted`). Write the
  scenario first (red), let Ino/the author fill the handler, gate to green.
  The data type is *materialized from its first use in a scenario* — define the
  example, the type follows. That is TDD for data, not just behavior.

Net: one concept (synapse-as-typed-record), authored in `.ino`, gated by
scenarios, transpiled to C#, usable from the SDK. No new type system — just
finishing the one you have.

---

## 6. Working with neurons & synapses from C# (SDK)

With real transpiled types, the existing `NeuronBuilder` /
`INeuronExecutionContext` surface (`sdk/DigitalBrain.SDK/NeuronBuilder.cs`)
becomes fully typed:

```csharp
// Reference a transpiled synapse type directly — it's a real C# record now.
await ctx.EmitAsync(new AnalyzeText(Text: "...", Topic: "Car Insurance"));
var summary = await ctx.AskAsync<Summary>("DigitalBrain.Ai.LlmNeuron", new LlmRequest(...));

// Or compose a programmatic neuron in C# (the L4 carve-out: SDK connectors).
var neuron = new NeuronBuilder()
    .WithName("Insurance.Triage")
    .WithInputSynapse<AnalyzeText>()
    .WithOutputSynapse<Summary>()
    .OnReceive<AnalyzeText>(async (ctx, msg, ct) =>
        await ctx.EmitAsync(new Summary(await Triage(msg.Text, ct))))
    .Build();
```

The contract from CLAUDE.md holds: hand-written C# is for Kernel / Boot /
Brain shell / SDK connectors; everything else is `.ino`. Transpilation is what
lets the two layers share *the same types* instead of duplicating contracts.

---

## 7. The one unified example (end-to-end)

> "Every night, look at unhandled support emails, and if a new category shows
> up, author a neuron to triage it."

1. **Schedule (reminder).** A `ScheduledReminderGrain` fires nightly →
   starts a **durable task** (§4) so the whole run survives a restart.
2. **Detect.** The task reads recent emails (existing connector neuron), asks
   the LLM neuron to cluster them, finds a new category "Car Insurance".
3. **Author (Ino).** The task sends an `AuthorInoNeuronRequest` intent; the
   Creator loop (`InoAuthoringLoop`) prompts **Ino** (your keyed model) to
   write an `.ino` that declares the `AnalyzeText`/`Summary` synapses inline
   and a handler with a scenario.
4. **Simulate (program, not physics).** `InoCompiler.Compile()` + scenario
   runner execute the scenario red→green in-process. Failures feed back to Ino
   (max-attempts loop). **No GPU world model anywhere.**
5. **Graduate.** On green, `InoToCSharpTranspiler` emits the `AnalyzeText` /
   `Summary` records + the `Triage` neuron as typed C#; Roslyn compiles;
   registry hot-activates it.
6. **Recover.** If the silo restarts at step 3, the durable task resumes from
   its journaled state; the half-finished authoring continues, not restarts.
7. **Learn.** The (intent → `.ino` → gate outcome) trace is journaled; a weekly
   durable task fine-tunes Ino (NeMo, local) so next month it nails the
   `.ino` first try.

Every arrow above is either *already in the repo* or one of the four additive
pieces in §3. None of them is Omniverse or Cosmos.

---

## 8. Final vision

> **Ino Forge: a Brain that writes, tests, types, and schedules its own
> behaviors — durably.**
>
> Intent comes in by voice or text. Ino (a small, optionally self-fine-tuned
> model served locally) authors a single `.ino` file. The kernel runs
> author → compile → **simulate (deterministic Orleans interpretation)** →
> gate → transpile-to-typed-C# → activate as a **durable scheduled task** that
> survives any restart. A synapse *is* a data type, so behavior, schema, and
> tests live in one file and graduate into strongly-typed C# the SDK can use.
> The Brain improves at authoring itself because every gate outcome is training
> data for the next Ino. NVIDIA's role is narrow and optional: NeMo to fine-tune
> Ino locally, an OpenAI-compatible server (Ollama / vLLM / NIM) to serve it.
> **No physical-world simulator. No 3D twin. No 80 GB GPU.** The simplest thing
> that makes the loop close.

---

## 9. Cut list / what NOT to build

In the spirit of v5 "The Cut":

- ❌ **No Omniverse.** A 3D/USD digital twin of the neuron graph is gloss.
- ❌ **No Cosmos / world-foundation models.** Wrong category, prohibitive VRAM.
- ❌ **No bespoke type system.** Synapse-as-record already *is* the schema;
  finish it, don't parallel it.
- ❌ **No hard NIM dependency.** Keep the provider abstraction; Ollama/vLLM are
  free and already compatible.
- ❌ **No full `[DurableTask]` machinery on day one.** Start with the
  `IDurableTaskCompletionSource<T>` pattern on the existing `DurableGrain`.
- ⚠️ **Don't replace reminders with durable tasks** — they solve different
  problems (wall-clock vs. in-flight recovery); compose them.

## 10. Suggested roadmap slices (additive, each shippable)

1. **F1 — Durable completion source.** Add `IDurableTaskCompletionSourceGrain<T>`
   on the existing `DurableGrain` base; make the Creator loop awaitable &
   restart-survivable. (Path A, §4.2.)
2. **F2 — Scheduled authoring.** Wire `ScheduledReminderGrain` → start an F1
   durable task that runs the Creator loop. (The §7 example, steps 1–6.)
3. **F3 — Inline data types in `.ino`.** Parser/linker support for declaring
   synapses/types inline; default-scalar → C# mapping. (§5.3.)
4. **F4 — Real `.ino` → C# emission.** Promote `InoToCSharpTranspiler` from
   test-artifacts to typed records + neuron classes + Roslyn load. (§5.2.)
5. **F5 — Ino training (optional).** Capture gate traces; NeMo LoRA fine-tune;
   serve via Ollama/vLLM; register as a keyed model; periodic re-train as a
   durable task. (§5.1.)

---

## Sources

- [NVIDIA Cosmos — World Foundation Models](https://www.nvidia.com/en-us/ai/cosmos/)
- [NVIDIA makes Cosmos openly available (NVIDIA Blog)](https://blogs.nvidia.com/blog/cosmos-world-foundation-models/)
- [Cosmos License (docs.nvidia.com)](https://docs.nvidia.com/cosmos/latest/license.html)
- [cosmos-predict1-7b model card (build.nvidia.com)](https://build.nvidia.com/nvidia/cosmos-predict1-7b/modelcard)
- [Cosmos Prerequisites (VRAM / GPU)](https://docs.nvidia.com/cosmos/latest/prerequisites.html)
- [Deploy Cosmos on GPU Cloud (Spheron, 2026 guide)](https://www.spheron.network/blog/deploy-nvidia-cosmos-gpu-cloud-synthetic-data/)
- [Omniverse Technical Requirements](https://docs.omniverse.nvidia.com/dev-guide/latest/common/technical-requirements.html)
- [Omniverse License Agreement](https://docs.omniverse.nvidia.com/guide_rtx-best-practices/latest/common/NVIDIA_Omniverse_License_Agreement.html)
- [NVIDIA NeMo (GitHub)](https://github.com/NVIDIA-NeMo/NeMo)
- [NIM for Developers](https://developer.nvidia.com/nim)
- [NIM free for Developer Program members (NVIDIA Blog)](https://developer.nvidia.com/blog/access-to-nvidia-nim-now-available-free-to-developer-program-members/)
- [Self-host NIM deployment guide (Spheron, 2026)](https://www.spheron.network/blog/nvidia-nim-self-host-deployment-guide/)
- [Customizing NIM with NeMo (NVIDIA Technical Blog)](https://developer.nvidia.com/blog/customizing-nvidia-nims-for-domain-specific-needs-with-nvidia-nemo/)
- [NVIDIA Nemotron foundation models](https://www.nvidia.com/en-us/ai-data-science/foundation-models/nemotron/)
