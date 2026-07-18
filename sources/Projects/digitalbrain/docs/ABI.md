# DigitalBrain.Core — the frozen public ABI

> Status: **frozen** (E-ABI #25, 2026-05-19). Grounding:
> `docs/superpowers/specs/2026-05-19-self-hosting-boot-design.md` §2 (layer
> table), §2.1 (decision **D-A**); `docs/v3/VISION.md` §4–§7, §11 ("blocks
> everything; the language must be frozen before any bundle ships").

## What this is

Per the two-layer split (D-A), **DigitalBrain** is the proprietary platform/runtime
substrate and **DigitalBrain** is the one installed domain (the product). The
layer authors bind to — *"the language + synapse/signal wire schema +
bundle/manifest schema"* — is **DigitalBrain.Core**, realized in-tree as the
assembly **`DigitalBrain.InoLang`** (`inolang/DigitalBrain.InoLang`).

In an InoLang-first system this surface **is** the public ABI: every `.ino`
file and every Marketplace bundle binds to it forever. It is therefore frozen.
An incompatible change must be a deliberate, reviewed baseline update — never an
accident.

### Naming sub-question (closed here, per #25)

Keep the assembly name **`DigitalBrain.InoLang`**; do **not** rename to a
product-neutral `InoLang.*`. The §Naming amendment (D-A) already standardizes
the *layer* name as **DigitalBrain.Core**; `DigitalBrain.InoLang` is the
in-tree realization of that layer. Renaming now would itself be an ABI break
ahead of the freeze and neither blocks nor helps the v3 spine.

## The frozen surface (two parts, two guards)

The ABI has two distinct parts; neither alone is sufficient, so two
complementary guards in `DigitalBrain.InoLang.Tests/PublicAbiFreezeTests.cs`
enforce it (the repo's single enforcement gate is `dotnet test` — there is no
separate .NET CI workflow):

1. **The C# binding surface** every Runtime host (E-RUN) compiles against —
   `InoCompiler`/`CompiledNeuron`/`InoGate`/`GateDecision`, the full `Ast.*`,
   `Linking.ContractSchema`/`ContractKind`/`IContractCatalog`/
   `LayeredContractCatalog` (the synapse/signal contract shape, plus the
   primary→fallback composition primitive that lets a reserved-vocabulary
   catalog stack over the reflected one — E-RUN #42), `Planning.ExecutionPlan`,
   `Runtime.INeuronHost`/`Interpreter`/`ActivationResult`,
   `Testing.ScenarioRunner`/`ScenarioReport`, `Lexing.TokenKind`,
   `Text.SourceSpan`, etc.
   - **Guard:** `Public_csharp_abi_surface_matches_frozen_baseline` —
     PublicApiGenerator renders the assembly's full public surface and asserts
     it equals the reviewed baseline
     `DigitalBrain.InoLang.PublicApi.approved.txt` (the enumerated frozen
     surface, committed alongside the test). Any add/remove/signature change
     fails the test.

2. **The InoLang language surface** every `.ino` document binds to — the
   **keyword spellings** (`neuron`, `synapse`, `scenario`, `on`, `emit`,
   `ask`, …; `signal` lexes as the same `TokenKind.Synapse` for v4-era
   bundle compatibility, see §v5 C2 baseline update below) and the
   **sigils** (`!`, `$`, `~`). These live in a
   *private* `Lexer` table, so they are **not** in the C# public API and the
   PublicApiGenerator guard cannot see them.
   - **Guards:** `Lexer_keyword_table_is_exactly_the_frozen_set` reflects the
     Lexer's own (private) keyword table — the single source of truth — and
     asserts it *is* exactly the 30 frozen spelling→`TokenKind` entries, so a
     silently **added**, removed, or remapped keyword fails the test at the
     real table (not merely a count of the test's own literal).
     `Frozen_keyword_spellings_still_lex_to_their_frozen_token_kind` /
     `Frozen_sigils_still_lex_as_sigil_tokens` /
     `Non_keyword_words_still_lex_as_identifiers` additionally drive the public
     `Lexer` as a black box, proving those spellings/sigils still lex correctly
     end-to-end through the public API.

The two guards interlock: guard 1 pins the `TokenKind` enum members; guard 2
pins the Lexer's actual keyword table as exactly the frozen set — so no silent
add, remove, or remap of a keyword can slip past both. (`TokenKind.NeuronKw`
has no spelling and is intentionally not in the keyword map; it remains part of
the frozen enum surface.)

## The bundle/manifest schema (forward note)

The layer table also names the **bundle/manifest schema** as part of the ABI.
No `Bundle`/`Manifest` type exists in `DigitalBrain.InoLang` yet — it lands
with **E1 (Marketplace)**. When it does, it joins this frozen surface: adding
those public types is a deliberate baseline update reviewed in the same PR, and
this doc gets a third bullet. That is the freeze working as designed, not a gap.

## v5 C1 baseline update (2026-05-25) — additive

`docs/v5plan/ROADMAP.md` C1 added a deferred-resolution path to the
compiler so the boot floor and Creator's LLM-authored `.ino` flow no
longer need a hand-built catalog. The additions are **backwards
compatible** — every prior caller still binds — and were applied to the
baseline under the §sanctioned-path rules below:

- `DigitalBrain.InoLang.Compile(string source)` — new no-catalog
  overload; delegates to `Compile(source, DeferredContractCatalog.Instance)`.
- `DigitalBrain.InoLang.Linking.DeferredContractCatalog` — new sealed
  class implementing `IContractCatalog`; returns a deferred stub schema
  for every FQN.
- `DigitalBrain.InoLang.Linking.ContractSchema.IsDeferred` — new
  optional record parameter (`bool IsDeferred = false`); marks a schema
  synthesised by `DeferredContractCatalog`. The Linker skips `INO301`/
  `INO302` field-shape diagnostics when this flag is set, since the
  shape is unknown until the runtime resolves it at activation.

No existing record positions, method signatures, or enum members
changed. The baseline diff is a single insertion per item.

## v5 C2 baseline update (2026-05-25) — Signal → Synapse collapse

`docs/v5plan/ROADMAP.md` C2 collapses the legacy `signal` concept into
the unified `synapse` — one wire envelope, two routing modes (`emit` =
broadcast, `ask` = point-to-point). The baseline update is **deliberate
and breaking** for the InoLang public surface; v4-era authored `.ino`
files continue to lex because `signal` stays a parse-time keyword alias
of `synapse` (the in-tree migration rewrote every shipped `.ino` and the
two embed sources).

Removed (post-C2, no longer in the public surface):

- `DigitalBrain.InoLang.Lexing.TokenKind.Signal`
- `DigitalBrain.InoLang.Linking.ContractKind.Signal`
- `DigitalBrain.InoLang.Ast.PortKind.Signal`
- `DigitalBrain.InoLang.Ast.PortSigil.Signal`, `PortSigil.Inbound`
- `DigitalBrain.InoLang.Ast.SignalFqnTrigger`
- `DigitalBrain.InoLang.Ast.ThenSignalEmitted`
- `DigitalBrain.InoLang.Runtime.EmittedSignal` (replaced by `EmittedSynapse`)
- `DigitalBrain.InoLang.Runtime.ActivationResult.EmittedSignals` (replaced by `EmittedSynapses`)
- `DigitalBrain.InoLang.Planning.TriggerCategory.SignalFqn` / `TriggerKey.SignalFqn(...)`
- `DigitalBrain.InoLang.Planning.TriggerCategory.Synapse` / `TriggerKey.Synapse(...)`
- `DigitalBrain.InoLang.Planning.ExecutionPlan.SignalPorts`

Added (v5 vocabulary):

- `DigitalBrain.InoLang.Ast.BroadcastTrigger` — replaces `SignalFqnTrigger`.
- `DigitalBrain.InoLang.Ast.ThenSynapseEmitted` — replaces `ThenSignalEmitted`.
- `DigitalBrain.InoLang.Runtime.EmittedSynapse` — replaces `EmittedSignal`.
- `DigitalBrain.InoLang.Planning.TriggerCategory.Port` / `TriggerKey.Port(...)`
  — replaces `Synapse`-category port triggers (`on portName:`).
- `DigitalBrain.InoLang.Planning.TriggerCategory.Broadcast` /
  `TriggerKey.Broadcast(...)` — replaces `SignalFqn`.
- `DigitalBrain.InoLang.Planning.ExecutionPlan.SynapsePorts` — replaces
  `SignalPorts`; same shape (`port name → contract FQN`).

Lexer surface change: the `signal` spelling is now a deliberate alias
that lexes to `TokenKind.Synapse` (recorded in the
`Lexer_keyword_table_is_exactly_the_frozen_set` guard so the alias
itself is part of the freeze, not an accident). The Boot floor
(`DigitalBrain.Boot.BootstrapCatalog`) drops `ContractKind.Signal`
references — all seed entries are now `ContractKind.Synapse`.

`DigitalBrain.Kernel.Contracts.Introspector.CatalogContractKind.Signal`
stays as an `[Obsolete]`-marked alias at the existing ordinal (1) for
Orleans wire compatibility with previously-persisted cluster state;
new code emits `Synapse`.

## Updating the frozen ABI (the only sanctioned path)

A failing freeze guard is a signal, not an obstacle.

1. Decide if the change is **intentional**. If not, revert the source change.
2. If intentional and **ABI-compatible by policy** (additive, or a conscious
   break with a recorded rationale): in the **same PR**,
   - regenerate the baseline (delete
     `DigitalBrain.InoLang.PublicApi.approved.txt` and re-run the test once — it
     rewrites then fails with a "review and commit" message — or update it by
     hand), and/or update the frozen keyword/sigil tables in
     `PublicAbiFreezeTests.cs`,
   - update this doc, and
   - call out the ABI change explicitly in the PR description.
3. Never edit the baseline to make an unintended diff "go away".
