# DigitalBrain — Vision v5 (The Cut)

> **Status:** canonical product vision, synthesized 2026-05-25. Supersedes v4
> on conflict. Inherits every v4 decision v5 is silent on. **v5 does not
> change the *what*. It cuts the *how* by ~70%.**

> **What v5 adds.** v3 froze the language. v4 froze the product shape. v5
> freezes the **minimum**. Every line of code, every project, every concept
> that does not directly serve "user says it, the brain does it" is on the
> cut list.

---

## 0. One paragraph

A user owns N **brains** (one per project / persona). Inside a brain they
talk to **Ino** in plain English. Ino converts intent into an **`.ino`
file** — one file, declarative, human-readable. That file declares a
**neuron** (the behavior), its **synapses** (the typed messages it sends
and receives), and its **RFW surface** (the UI it paints). The runtime
compiles the `.ino` into a Roslyn-compiled C# class behind the scenes,
runs its scenario red → green, and only then activates it as an Orleans
grain. Each brain has its own **installed domains** — folders of `.ino`
files pulled from public GitHub repos. The kernel is generic. Everything
useful is a domain.

---

## 1. The five v5 invariants (don't relitigate)

These extend the v4 invariants. Where v5 narrows a v4 invariant, v5 wins.

1. **V5-1 One file per behavior.** A neuron is **one `.ino` file**.
   No more `.cs` + `.feature` + `.Steps.cs` triplet. The `.cs` is generated
   by the runtime from the `.ino`; it lives in `obj/` and is never checked
   in. The only hand-written `.cs` files are SDK connectors (platform
   access — file IO, OAuth, gRPC, Windows API) and the kernel itself.

2. **V5-2 One message type.** A **synapse** is a typed record. **Signals
   are deleted as a separate concept** — a signal was always just a
   synapse with no addressee. The wire format is one envelope; the routing
   is "send to FQN" (point-to-point) or "send to type" (broadcast).
   Same record, same code path.

3. **V5-3 No global catalog.** `MapCatalog.With(...)` is deleted. Ports
   resolve at **activation time**, not boot time. If a neuron's `using
   x = neuron(Foo)` references a missing FQN, the neuron fails to activate
   and emits a `Neuron.ActivationFailed` synapse — same as any other
   runtime error. The catalog is the union of every domain installed in
   the brain, computed lazily, never persisted.

4. **V5-4 UI is data.** Every neuron's RFW surface is declared **in its
   `.ino` file** as a `ui:` block utilizing the declarative `UiKit` namespace, replacing raw markup or raw RFW. The Flutter shell is a generic RFW interpreter. There is no per-neuron Flutter widget code. A neuron without a `ui:` block gets the kernel default surface (V4-4 unchanged).

5. **V5-5 Domains are repos.** A domain is a public GitHub repo
   containing `.ino` files (and optional `.cs` for platform access).
   `digitalbrain install <owner>/<repo>` clones it into
   `%LocalAppData%\DigitalBrain\brains\{brainId}\domains\<owner>\<repo>`.
   The brain's "installed domains" is `ls` of that folder. No central
   registry. Discovery via GitHub topic `digitalbrain-domain`.

---

## 2. The cut list (what dies in v5)

### Concepts deleted

| Concept | Replaced by | Why |
|---|---|---|
| `MapCatalog` hand-build | Lazy resolution at activation | Validating at boot prevented runtime install; the validation is the wrong gate |
| `Signal` as a distinct concept | Synapse with broadcast routing | Same wire, same record, same code — two names for one thing |
| `.feature` + `.Steps.cs` triplet | `scenario` block in `.ino` | Three files per neuron is two files too many |
| Reqnroll / Gherkin dependency for `.ino` flows | Ino owns its scenario syntax | One language for spec + impl + tests + UI |
| `*.Contracts` projects | Records declared inside their owning `.ino` | Cross-project record sharing was a YAGNI; the FQN is the contract |
| `DigitalBrain.Domains.<Name>` per-domain silo projects | Folder convention under `domains/<name>/` | A silo per domain was DDD theater; one Orleans cluster, many `.ino` files |
| Per-neuron Flutter widget code | `rfw:` block in `.ino` | UI is data; the shell is generic |
| Hand-built `IDigitalBrain` seam in `digitalbrain.cs` | One `DigitalBrain.Launch(args)` call | The seam was a layered abstraction over a one-liner |
| Multiple InoLang test projects (`TestKit`, `TestRunner`, `Test`) | One `DigitalBrain.InoLang.Tests` | One language, one test project |

### Projects deleted or merged

```
Before (v4)                                  After (v5)
─────────────────────────────────────────    ─────────────────────────────────────
kernel/DigitalBrain.Core                       ┐
kernel/DigitalBrain.Core.Hosting               ├──> kernel/DigitalBrain.Runtime
kernel/DigitalBrain.Core.SourceGen             ┘    (one project, one assembly)
kernel/DigitalBrain.Hosting                    ──>  folded into DigitalBrain.Runtime
kernel/DigitalBrain.Kernel                     ──>  kernel/DigitalBrain.Kernel (kept)
kernel/DigitalBrain.Kernel.Contracts           ──>  deleted (records inline)
kernel/DigitalBrain.Boot                       ──>  folded into digitalbrain.cs
kernel/DigitalBrain.NeuronTesting              ──>  folded into DigitalBrain.Runtime
kernel/DigitalBrain.AppHost                    ──>  folded into digitalbrain.cs
kernel/DigitalBrain.Domains.Dynamic            ──>  folder convention, not project
kernel/DigitalBrain.ServiceDefaults            ──>  folded into DigitalBrain.Runtime
sdk/DigitalBrain.SDK                      ──>  sdk/DigitalBrain.SDK (kept)
sdk/DigitalBrain.SDK.Contracts            ──>  deleted (records inline per connector)
sdk/DigitalBrain.SDK.Mcp                  ──>  folded into DigitalBrain.SDK
inolang/DigitalBrain.InoLang              ──>  kept
inolang/DigitalBrain.InoLang.TestKit      ┐
inolang/DigitalBrain.InoLang.TestRunner   ├──> inolang/DigitalBrain.InoLang.Tests
inolang/DigitalBrain.InoLang.Test         ┘
UI/flutter                                ──>  UI/flutter (kept, but RFW-only)
UIKit                                     ──>  UI/flutter/lib/rfw_kit (Ino-callable widgets)
```

**Project count: 19 → 5.**

```
kernel/DigitalBrain.Runtime              # Orleans + Roslyn + Ino interpreter + neuron testing + service defaults
kernel/DigitalBrain.Kernel               # Creator, Navigator, Gateway, Ino app, BrainRegistry
sdk/DigitalBrain.SDK                # All connectors (Ai, Google, Sqlite, Stripe, Windows, etc.)
inolang/DigitalBrain.InoLang        # Parser + compiler + scenario runner
inolang/DigitalBrain.InoLang.Tests  # The only test project for the language
UI/flutter                          # Generic RFW shell (Constellation + Brain Scene)
```

---

## 3. The shape of a v5 neuron

One file. Declarative. Includes its own UI.

```ino
neuron Acme.WeeklyDigest
  "Summarises this week's client emails into one row in the status sheet."

  using mailbox = neuron(Google.Gmail)
  using sheets  = neuron(Google.Sheets)
  using llm     = neuron(Ai.Chat)

  synapse Digested(week: int, summary: string)

  on Activated:
    let threads = ask mailbox for "this week's threads with acme.com"
    let summary = ask llm to "summarise into 3 bullets" with threads
    ask sheets to "append row" with [today, summary]
    emit Digested(week: weekOf(today), summary: summary)

  ui:
    UiKit.Column(
      children: [
        UiKit.Card(title: "Weekly Acme Digest", body: summary),
        UiKit.Button(label: "Re-run", action: Activated)
      ]
    )

  scenario "happy path":
    given mailbox returns [thread("alice", "Q3 numbers")]
    given llm returns "alice sent Q3 numbers"
    when Activated
    then sheets received "append row"
    then Digested emitted with week == weekOf(today)
```

That is the **whole** neuron. No `.feature`, no `.Steps.cs`, no Flutter
widget, no `.csproj`, no manifest. The runtime generates the `.cs`
implementation under `obj/`, Roslyn-compiles it, runs the `scenario`
block as the gate, and on green registers it as an Orleans grain.

The user does not write this file. **Ino does, from an utterance.**

---

## 4. The PoC scenario (in v5 form)

You run `dotnet run digitalbrain.cs`. Aspire spins up the Kernel, the
SDK, and the Flutter shell. The Constellation opens. You click *Acme
Client*. The Brain Scene opens — a graph of the brain's three currently
installed domains: `digitalbrain/core`, `digitalbrain/google`,
`digitalbrain/canvas`. You open Ino and say:

> *"Analyse the file `C:/reports/q3.txt` and show me the key points as a
> diagram on the canvas."*

Ino sends the utterance + the brain's installed-domain catalog
(reflected from `~/AppData/Local/DigitalBrain/brains/{id}/domains/`) to
an LLM. The LLM returns one `.ino` file:

```ino
neuron Adhoc.AnalyseQ3
  using fs     = neuron(Windows.FileSystem)
  using llm    = neuron(Ai.Chat)
  using canvas = neuron(Canvas.Diagram)

  on Activated:
    let text   = ask fs to "read C:/reports/q3.txt"
    let bullets = ask llm to "extract 5 key points" with text
    ask canvas to "render bullets as mindmap" with bullets

  ui:
    UiKit.Column(
      children: [
        UiKit.Card(title: "Q3 Analysis", body: bullets)
      ]
    )
```

The Creator runs its scenario (LLM generated it too) red, generates the
C#, Roslyn compiles, scenario passes green. Orleans activates the new
grain. The grain fires `Activated` on itself. The Canvas neuron receives
"render mindmap" and emits an RFW Modal lock. The shell pops the
canvas surface on-screen. Total elapsed: ~3 seconds.

The whole flow is one file the user never sees.

---

## 5. Inherited from v4 (unchanged)

- **V4-1** One process, one Aspire composition, one launch command.
- **V4-2** Constellation is the only top-level screen.
- **V4-3** One brain = one isolated runtime context (BrainId prefix).
- **V4-4** Every neuron has a default RFW surface if none declared.
- **V4-5** Idle / Busy / Modal lock states honored by the shell.
- The two-layer brand split — **DigitalBrain** = substrate, **DigitalBrain**
  = product — stays. v5 collapses projects within each layer; it does
  not collapse across layers.

---

## 6. What v5 does **not** decide

- The marketplace's billing model (Stripe + 20% — kept from v3).
- The Global Brain sync mechanism (kept from v4 §10).
- Voice-to-text provider choice (Whisper.net — kept).
- The LLM provider abstraction (`Microsoft.Extensions.AI` — kept).

If v5 is silent, v4 wins. If v4 is silent, v3 wins.

---

## 7. Companion docs

- [`INO.md`](INO.md) — the unified language (neuron + synapse + RFW + scenario)
- [`DOMAINS.md`](DOMAINS.md) — the install model and per-brain isolation
- [`SDK.md`](SDK.md) — how the SDK is extended (the one place C# is hand-written)
- [`ROADMAP.md`](ROADMAP.md) — the v4 → v5 cut sequence
