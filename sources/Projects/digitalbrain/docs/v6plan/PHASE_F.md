# Phase F — The Forge (NVIDIA-via-Aspire, the Live Neuron, and the Cut)

> Status: **proposal / brainstorm phase**, not yet canonical. Companion to
> `docs/v6plan/INO_FORGE.md` (the vision + the honest NVIDIA verdict). Builds on
> v5 "The Cut" (`docs/v5plan/VISION.md`). Written 2026-05-28.
>
> This phase turns the INO_FORGE vision into a concrete, deletion-first plan
> driven by **Musk's 5-step algorithm**. It answers three asks:
> 1. Wire the **NVIDIA stack (NeMo + NIM + Nemotron) as Aspire resources** —
>    local Docker / Hugging Face — and expose them to the Brain **as neurons**.
> 2. Make a **live, active neuron observable** — *watch Ino operate InoLang*
>    while a job runs.
> 3. **Minimize the codebase** — delete the trash, keep it lean.

---

## The spine: Musk's algorithm, applied to DigitalBrain

Every item in this phase is placed in one of the 5 steps, **in order**. We do
not optimize what we should delete; we do not automate what we should not run.

| # | Step | What it means here |
|---|------|--------------------|
| 1 | **Make the requirement less dumb** | Question "NeMo + NIM + Nemotron each as a neuron." (§0) |
| 2 | **Delete** | Remove ~750 files of dead weight *before* adding anything. (§3) |
| 3 | **Simplify / optimize** | One connector neuron, not three. One inference resource. (§1) |
| 4 | **Accelerate** | Live durable-task view so the author→gate loop is watchable & fast. (§2) |
| 5 | **Automate** | Scheduled re-train of Ino — *last*, only once the loop is real. (§1.4) |

> "If you're not adding back ~10% of what you delete, you didn't delete
> enough." Apply that test to §3.

---

## §0 — Question the requirement (Step 1)

**Ask:** "NeMo + NIM + Nemotron, each as a neuron."

**Reality, attached to names so we can argue with a person, not a department:**

- **NIM** is an *inference server* (a long-lived HTTP service). It is **a
  resource**, not a behavior. ✅ Aspire resource.
- **Nemotron** is *model weights* loaded **into** that server. It is **a
  config value** (`--model`), not a resource and not a neuron.
- **NeMo** is a *training framework* — a **batch job** you run, then it exits.
  It is **a scheduled durable task**, not a long-lived neuron.

So "three neurons" collapses to:

- **One Aspire resource:** the inference server (NIM **or** Ollama **or**
  vLLM — all OpenAI-compatible, all interchangeable).
- **Zero new neuron types:** the Brain already has the AI-domain LLM neuron +
  `ILlmProviderFactory` + keyed `IChatClient`
  (`sdk/DigitalBrain.SDK/Ai/Llm/Providers/OpenAiProviderFactory.cs`). A
  local model is **a new keyed model registration**, not new code.
- **One scheduled job:** NeMo fine-tune, triggered by a durable task (F5),
  not a running actor.

**The deleted requirement we add ~10% back:** we *do* want **one** thin
neuron — `LocalModelNeuron` (or just reuse the existing LLM neuron) — so the
local model is visible in the constellation like any other behavior and can
declare an RFW surface ("model: nemotron-8b, status: ready, tps: 41"). That is
the 10%. We do **not** build `NeMoNeuron` + `NimNeuron` + `NemotronNeuron`.

---

## §1 — NVIDIA as Aspire resources + one connector (Steps 3, 5)

### 1.1 The inference server as an Aspire container (the GPU detail that matters)

Aspire passes `--gpus all` to the container runtime via
**`WithContainerRuntimeArgs`** — confirmed the intended mechanism for "exposing
GPUs to the container" (Aspire.Hosting 13.1). NIM runs as a standard
`--gpus all` container exposing an **OpenAI-compatible** endpoint on
`:8000/v1` (local NIM does not validate the key).

```csharp
// AppHost — illustrative; verify exact signatures via Context7 at impl time.
var ino = builder.AddContainer("ino-nim", "nvcr.io/nim/meta/llama-3.1-8b-instruct", "latest")
    .WithContainerRuntimeArgs("--gpus", "all", "--runtime=nvidia", "--shm-size=16GB")
    .WithEnvironment("NGC_API_KEY", builder.AddParameter("ngc-key", secret: true))
    .WithBindMount(@"%LocalAppData%\DigitalBrain\nim-cache", "/opt/nim/.cache")
    .WithHttpEndpoint(targetPort: 8000, name: "openai");

builder.AddDigitalBrain(...)          // the kernel
    .WithReference(ino);              // resolves http://ino-nim:8000/v1
```

### 1.2 The free default: skip NGC, use Ollama (recommended starting point)

NIM production needs AI Enterprise licensing; **Ollama and vLLM are free and
serve the same OpenAI wire protocol**, so they slot into the *same* provider
path (`OllamaProviderFactory` already exists). Start here; swap to NIM only if
you need NVIDIA's supported container.

```csharp
// Community Toolkit Ollama integration (free, GPU optional). Illustrative.
var ollama = builder.AddOllama("ino-llm")
    .WithContainerRuntimeArgs("--gpus", "all")     // optional; CPU works too
    .WithDataVolume();
var nemotron = ollama.AddModel("nemotron-mini");   // auto-pulls from registry
```

Nemotron weights are free (NVIDIA Open Model License, commercial OK) on Hugging
Face / Ollama registry — so "Nemotron" is just the `AddModel(...)` argument.

### 1.3 The connector: zero new abstraction

The kernel reads the resolved endpoint and registers it as a **keyed
`IChatClient`** under a model key (e.g. `"ino-local"`). The Creator loop
already resolves models by key
(`kernel/DigitalBrain.Kernel/Creator/InoAuthoring/InoAuthoringLoop.cs:48`), so
**Ino-the-author becomes "use model key `ino-local`"** — no new wiring beyond
one provider-factory registration pointing at the OpenAI-compatible base URL.

### 1.4 NeMo as a scheduled durable task (Step 5 — automate last)

Fine-tuning is a *job*, modeled as a one-shot container kicked off by a durable
scheduled task (F5 in INO_FORGE):

1. A reminder fires (weekly) → starts a durable task (the Accede pattern).
2. The task exports the captured gate traces (intent → `.ino` → compile/scenario
   outcome) as an SFT dataset.
3. It runs the **NeMo framework container** (`--gpus all`) as a job; on success
   it produces new LoRA weights.
4. It tells the inference resource to load the new weights and flips the
   `ino-local` model registration. Done — Ino got smarter, hands-free.

> Do **not** build this until F1–F4 exist and the loop visibly works. Automation
> is step 5, not step 1.

---

## §2 — The live neuron: *watch Ino operate InoLang* (Step 4)

The ask: "a neuron which is active now, executing some job — see how it works."
This is **observability of an in-flight durable task**, surfaced on the
neuron's RFW surface (the v5 "UI is data" invariant). No new shell code per
neuron.

**The mechanism (reusing what exists):**

- The Creator authoring run becomes a **durable task** (F1) whose state is
  journaled at each step (the Brain already runs on `DurableGrain` +
  Orleans.Journaling). Steps: `Prompting → Compiling → Simulating(scenario k/n)
  → Gating → Transpiling → Activating`.
- Each transition writes a structured progress record to the neuron's
  **outgoing journal** (`Neuron` already keeps `IDurableList<Synapse>`
  incoming/outgoing — `kernel/DigitalBrain.Runtime/Neurons/Neuron.cs`).
- The neuron's **`rfw:` surface** renders that live state: the current `.ino`
  source being authored, the compile diagnostics, the red/green scenario
  ticks, attempt N of 5. The shell is a generic RFW renderer — it just paints
  the data.
- Because the task is **durable**, if the silo restarts mid-author the live
  view resumes from the journaled step, not from zero. *You can watch it
  recover.*

**What "see how they work" gives you concretely:** land the camera on the
active Ino neuron and watch, in real time, intent → draft `.ino` → compile
error → Ino's retry → scenario goes green → typed C# emitted → activated. The
existing "execution scratchpad" / "visual lifetime hooks" work (recent commits)
is the surface; this phase feeds it durable-task progress instead of ad-hoc logs.

**Cut check:** no new dashboard, no new protocol. Progress is *synapses on the
journal*; the RFW surface is *data in the `.ino`*. Both already exist.

---

## §3 — The Cut: delete first (Step 2)

Concrete, file-cited, ordered. **Do this before §1/§2.** Verified against the
current tree.

### 3a — Delete today (zero references, zero impact)

| Target | Path | Why | Size |
|---|---|---|---|
| v3 Orleans prototype | `examples/inolang-orleans-proto/` | Not in solution; superseded by Runtime; last touched 2025-05-25 | 5 csproj, ~200 .cs |
| Scratch tree | `scratch/` | Orphaned exploration + built `bin/obj` + stray `.js`/`.py` debug scripts | whole tree |
| Broken stress test | `challenger_tests/GeneratorStressTester/` | References **non-existent** `DigitalBrain.Core.SourceGen.csproj` — won't even load | 1 csproj, ~544 lines |
| Vestigial Signal attr | `kernel/DigitalBrain.Runtime/Runtime/SignalAttribute.cs` | v5-2 deletes Signal-as-concept; no production usages | 1 file |

> ~750 files, no impact on the running system. This is the "if it builds after,
> you didn't cut enough" freebie.

### 3b — Delete after breaking one reference

| Target | Path | Blocker | Action |
|---|---|---|---|
| Pre-v5 sample domains | `samples/DigitalBrain.Domains.Samples/` | `AppHost.csproj` references it | Remove the `ProjectReference`, then delete. Reference domains are GitHub repos now (v5-5), not bundled. Kills 2 stray `Contracts/` triplet dirs too. |

### 3c — Consolidate (Step 3, after deleting)

| Item | Where | Action | Gate |
|---|---|---|---|
| Reqnroll `.feature`+`.Steps.cs` triplets | 4 neurons in `kernel/DigitalBrain.Kernel/` (Introspector, User, TaskManager, FlutterPerf) | Move scenarios into the neurons' `.ino` `scenario:` blocks (v5 C4) | unblocks ↓ |
| `Gherkin` package dependency | `kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` | Drop the package + the 4 `.feature` files — they're its only users | after C4 |
| Transpiler triplet emission | `sdk/DigitalBrain.SDK/INO/InoToCSharpTranspiler.cs` | Delete the `.feature`/`.Steps.cs` emit; keep/promote **C# record + neuron** emit (F4 in INO_FORGE) | after C4 |
| AppHost project | `kernel/DigitalBrain.AppHost/` (~21 lines) | Fold into a `digitalbrain.cs` launcher (v5 C5) | C5 |
| Scattered `SynapseTypeNames` | Runtime / SQLite / (samples — being deleted) | Single registry module in SDK; tactical, low priority | optional |

### 3d — Do NOT touch (load-bearing, flagged so we don't "optimize" them away)

- `inolang/DigitalBrain.InoLang/Parsing/Parser.cs` (808 LOC) and
  `Runtime/Interpreter.cs` (584 LOC) — normal size for a language runtime.
  Only split if they cross ~1000 LOC.
- `kernel/DigitalBrain.Runtime/DynamicNeuronGrain.cs` (816 LOC) — the live
  compile→interpret→execute path. Mid-flight (E-RUN). Don't refactor now.
- `kernel/DigitalBrain.Runtime/Neurons/Neuron.cs` (579 LOC) — the base every
  neuron inherits. Splitting breaks everything.
- Keep `signal()` *syntax* in the lexer/parser (lowers to a synapse AST node) —
  deleting the *attribute* is fine, deleting the *grammar* breaks existing
  `.ino`/tests.

---

## §4 — The one minimal example (everything above, end to end)

> "Run Ino on a schedule; watch it forge a new neuron; let it teach itself."

1. **(§3 done first.)** Repo is ~750 files lighter; one inference resource is
   wired.
2. **Schedule.** A reminder fires → starts a **durable task** (F1).
3. **Author + watch.** The task drives the Creator loop against model
   `ino-local` (served by the Aspire Ollama/NIM resource). You **land the camera
   on the active neuron** and watch the `.ino` get drafted, fail to compile,
   retry, and go green — live, from the journal (§2).
4. **Simulate = interpret.** Scenarios run in-process (no GPU world model).
5. **Graduate.** Green `.ino` → typed C# records + neuron via the
   (de-triplet-ed) transpiler → Roslyn → hot-activate.
6. **Recover.** Kill the silo mid-author; the live view resumes from the
   journaled step.
7. **Automate (last).** Weekly durable task runs the NeMo container on captured
   gate traces; reloads `ino-local` with better weights.

No Omniverse. No Cosmos. No 80 GB GPU. One container, one connector, one durable
loop, and a lot less code.

---

## §5 — Phase exit criteria & slices

Continues the F-slices from `INO_FORGE.md` §10 (F1–F5):

- **F0 — The Cut.** Execute §3a + §3b; green build; `dotnet test` passes.
  *(Do first.)*
- **F6 — Inference resource.** Add the Ollama (free) Aspire resource with
  `--gpus all`; register `ino-local` as a keyed `IChatClient`; Creator can
  author against it. (§1.1–1.3)
- **F7 — Live neuron view.** Creator authoring run as a durable task (depends
  on F1) emitting step-progress synapses; RFW surface renders the live
  author→gate loop and survives restart. (§2)
- **F8 — De-triplet.** Move the 4 Kernel scenarios to `.ino`; drop Gherkin;
  shrink the transpiler to C# emit only. (§3c, depends on F4)
- **F9 — NeMo job (optional, last).** Scheduled durable task fine-tunes Ino and
  hot-swaps `ino-local`. (§1.4)
- **F10 — NIM swap (optional).** Replace the Ollama resource with a NIM
  container behind the same keyed model, if NVIDIA-supported serving is wanted.

**Exit when:** you can sit in the constellation, trigger a scheduled author run,
*watch* Ino forge a neuron in InoLang, see it graduate to typed C#, and the repo
is materially smaller than at phase start.

---

## Sources

- [Aspire `WithContainerRuntimeArgs` (GPU passthrough)](https://learn.microsoft.com/dotnet/api/aspire.hosting.containerresourcebuilderextensions.withcontainerruntimeargs?view=dotnet-aspire-13.0)
- [Aspire — connection strings / resource references](https://learn.microsoft.com/dotnet/aspire/troubleshooting/connection-string-missing)
- [NVIDIA NIM — deploy with Docker (`--gpus all`, port 8000, OpenAI API)](https://docs.nvidia.com/ai-enterprise/nim-llm/latest/deploy-docker.html)
- [Run NVIDIA NIM on your own GPU — same API, different endpoint (DEV)](https://dev.to/torkian/run-nvidia-nim-on-your-own-gpu-same-api-different-endpoint-484a)
- [NVIDIA NIM API — free tier for developers (2026)](https://decodethefuture.org/en/nvidia-nim-api-explained/)
- [NVIDIA NeMo (GitHub)](https://github.com/NVIDIA-NeMo/NeMo)
- [NVIDIA Nemotron foundation models](https://www.nvidia.com/en-us/ai-data-science/foundation-models/nemotron/)
