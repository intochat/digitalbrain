# Multi-Agent Panel on Local LLMs (nemotron-mini + phi4)

> Status: Phase 1 in progress — 2026-05-28
> Owner: substrate / AI domain
> Supersedes nothing; this is an additive slice on top of the existing
> `GroupChatNeuron` + brainstorm/choose-direction flow.

## Goal

Run DigitalBrain's multi-agent deliberation **fully locally** on an RTX 5080,
and make it reachable + verifiable through the **MCP `brain` server** (the same
gRPC gateway a human drives from the Flutter dock). Two phases:

- **Phase 1 (now):** keep the single `GroupChatNeuron` grain, but run its
  persona turns on **nemotron-mini** and its strict-JSON synthesis on
  **phi4**. Add an MCP `convene` tool so the panel can be driven and observed
  end-to-end without a Flutter chip-tap. Get it green + verified.
- **Phase 2 (next):** split each panelist into its own neuron grain, each with
  its own `[Llm<T>]`, talking via `ask`/`emit` synapses, with the moderator as
  a real neuron. This is the faithful "each neuron has its own LM + genuine
  navigation across the brain" shape.

## Hardware / model reality (RTX 5080, 16 GB GDDR7, ~960 GB/s)

"Each neuron has its own LM" does **not** mean each grain loads its own
weights — that's impossible at N grains. It means **one shared Ollama server,
per-neuron persona/system-prompt/model-tier**. Tiering:

| Tier | Model | ~VRAM (Q4) | Used for |
|------|-------|-----------|----------|
| turns | `nemotron-mini` (4B) | ~2.7 GB | in-character panelist turns, routing, intent |
| synth | `phi4` (14B) | ~9 GB | strict-JSON weekly-plan synthesis |

Both fit together (~12 of 16 GB). Single-stream gen on a 5080 is ballpark
~120–180 tok/s; the panel is **7 sequential LLM calls** (3 participants × 2
rounds + 1 synthesis), so a deliberation is ~8–15 s wall-clock — a simulation,
not real-time.

⚠️ Gotchas:
- `--gpus all` on the `ino-llm` Ollama container needs Docker Desktop + NVIDIA
  Container Toolkit / WSL2 GPU passthrough. Without it Ollama falls back to CPU
  and timings blow out to minutes.
- First `aspire start` after this change **pulls phi4 (~9 GB)** — slow once.
- Editing `DigitalBrain.AppHost` requires a full `aspire stop` → `aspire start`
  (not `rebuild`).

## The seams this rides on (already in-tree)

- **Model binding:** `LlmModel` (abstract) → `ServiceKey = "{provider}-{id}"`.
  `[Llm<TModel>]` resolves a **keyed `IChatClient`** by that key
  (`LlmAttributeMapper`). Keyed clients are registered per-provider in
  `DigitalBrainSiloDomainsExtensions.ConfigureAiDomain`. A separate `ino-local`
  keyed client is wired from `ConnectionStrings:ino-local` (the `ino-llm`
  Ollama endpoint), defaulting to model name `nemotron-mini`.
- **Multi-agent:** `GroupChatNeuron` (`sdk/.../Ai/GroupChat`) loops personas
  (`SpeakAsync`) then `SynthesiseAsync` → emits `WeeklyPlanProposed` +
  `RfwCard(PlanCard)`. Triggered by `ChooseDirectionRequest`.
- **Navigation / entry:** gateway `Send(SynapseEnvelope)` resolves `type_name`
  via `SynapsePayloadRegistry` (keyed by **exact `Type.FullName`**), routes to
  the receiver neuron, and async results surface on `WatchHomeFeed` as RFW
  cards. `SubmitPrompt` → `UserPromptReceived` → … → BrainstormNeuron →
  `OptionChipStackCard` (the human then taps a chip → `ChooseDirectionRequest`).
- **MCP:** `sdk/DigitalBrain.SDK/Mcp` — `BrainTools` exposes `brain`
  (SubmitPrompt + watch feed) and `list_neurons`. Tools take constructor-injected
  gateway/brainwatch gRPC clients.

### Known latent bug (found while planning)

`UI/flutter/lib/widgets/option_chip_stack_card.dart` sends `typeName =
'DigitalBrain.Domains.Ai.Contracts.ChooseDirectionRequest'`, but the record's
real `FullName` is `DigitalBrain.SDK.Ai.ChooseDirectionRequest` and the registry
keys by exact `FullName` — so the chip's `Send` currently returns `NotFound`.
Phase 1 corrects this string so the UI path and the MCP path agree.

---

## Phase 1 — local panel, MCP-driven

### Edits

1. **`sdk/DigitalBrain.SDK/Ai/Models/OllamaModels.cs`** — add
   `NemotronMini : LlmModel` (`Id = "nemotron-mini"`, `Provider = "ollama"`).
   `Phi4` already exists. Both auto-join `LlmModel.All`, so their `[Llm<T>]`
   facet mappers and mock-mode keyed clients register automatically.

2. **`kernel/.../DigitalBrainSiloDomainsExtensions.cs`** — in the live branch's
   `ino-local` block, factor the local-client builder into a small helper and
   also register keyed clients under the model service keys
   `ollama-nemotron-mini` → `nemotron-mini` and `ollama-phi4` → `phi4`, both
   against the `ConnectionStrings:ino-local` Ollama endpoint (one instance
   serves both). Registered after the `LlmModel.All` loop so these win
   deterministically. So `[Llm<NemotronMini>]` / `[Llm<Phi4>]` resolve live.

3. **`sdk/DigitalBrain.SDK/Ai/GroupChat/GroupChatNeuron.cs`** — replace the
   single `[Llm<Gpt5>] IChatClient chat` with two facets:
   `[Llm<NemotronMini>] IChatClient turnChat` (used by `SpeakAsync`) and
   `[Llm<Phi4>] IChatClient synthChat` (used by `SynthesiseAsync`). Add
   `ResponseFormat = ChatResponseFormat.Json` to the synthesis `ChatOptions`
   (maps to Ollama `json_object`; kills the markdown-fence failure mode).
   Keep `ParseSynthesis`'s brace-extraction + fallback.

4. **`kernel/DigitalBrain.AppHost/DigitalBrainHostingExtensions.cs`** — add a
   second model to the `ino-llm` Ollama resource: `ollama.AddModel("ino-synth",
   "phi4")`, and `WithReference` + `WaitFor` it on the kernel so phi4 is pulled
   and startup gates on it.

5. **`sdk/DigitalBrain.SDK/Mcp/Tools/BrainTools.cs`** — add a `convene` MCP tool:
   builds a `ChooseDirectionRequest` envelope (`type_name =
   "DigitalBrain.SDK.Ai.ChooseDirectionRequest"`, `ReceiverNeuronType =
   "GroupChatNeuron"`), opens `WatchHomeFeed`, fires `Send` fire-and-forget
   (it deadlines by design — the panel fans out async), and collects feed cards
   until a `PlanCard` arrives (or timeout), returning the plan + the panel
   transcript. This is "a neuron handling your request via MCP."

6. **`UI/flutter/lib/widgets/option_chip_stack_card.dart`** — fix the stale
   `typeName` to `DigitalBrain.SDK.Ai.ChooseDirectionRequest` (constant only; no
   logic change). Needs UI re-verification later, out of Phase 1's green gate.

### MCP test path (the deliverable)

```
convene(prompt:"Bali surf-and-budget week",
        title:"Surf + tight budget",
        participants:["TimeManager","FinancialAdvisor","DietSpecialist"])
   → gateway.Send(ChooseDirectionRequest)            // routed across the cortex
   → GroupChatNeuron: 3 personas × 2 rounds on nemotron-mini  // agents talk
   → SynthesiseAsync on phi4 → WeeklyPlanProposed + RfwCard(PlanCard)
   → WatchHomeFeed delivers PlanCard                 // navigation surfaces it
   → tool returns { rationale, items[], participants, transcript[] }
```

### Acceptance (Phase 1)

- `dotnet build` clean; `dotnet test` green (mock mode unaffected — every
  `LlmModel.All` key incl. NemotronMini gets a `BddMockChatClient`).
- `aspire start` healthy; `ino-llm` pulls nemotron-mini + phi4.
- MCP `convene` returns a `PlanCard` with a non-empty transcript showing the
  three personas deliberating, synthesised into a weekly plan.

## Phase 2 — per-neuron agents (faithful shape)

Replace the persona loop with real grains:

- New neuron per role: `TimeManagerNeuron`, `FinancialAdvisorNeuron`,
  `DietSpecialistNeuron` (and the connector-backed `GoogleCalendar`, `Gmail`,
  `Navigator`), each `[Llm<NemotronMini>]` (or its own tier), each a triplet
  per project rules (`.feature` + steps + impl, or `.ino` once E-INO covers it).
- New synapse contracts: `PanelTurnRequest`/`PanelTurnReply` (moderator → agent,
  point-to-point `ask`), reusing `WeeklyPlanProposed` for the synthesised result.
- `ModeratorNeuron` replaces the loop: for each round, `ask` each participant
  neuron a `PanelTurnRequest` (carrying the running transcript), append replies,
  then `[Llm<Phi4>]` synthesis → `WeeklyPlanProposed` + `PlanCard`.
- Navigation becomes a genuine neuron-to-neuron graph; the Constellation /
  Brain Scene lights up each participant as it speaks.

### Acceptance (Phase 2)

- Each participant is independently activatable and testable (own triplet,
  own model tier).
- The same MCP `convene` call now fans out across N grains; transcript shows
  each *neuron* (not persona string) contributing; PlanCard unchanged in shape.

## Risks

- GPU passthrough / model-pull latency (see gotchas above).
- 4B JSON reliability — mitigated by routing synthesis to phi4 +
  `ChatResponseFormat.Json` + `ParseSynthesis` fallback.
- `Send` deadlines on `ChooseDirectionRequest` (async fan-out) — the `convene`
  tool must rely on the home feed, not `Send`'s return (it ignores the deadline).
