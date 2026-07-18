# INO — the unified language (v5)

> One `.ino` file is a complete neuron: behavior, contracts, UI, and tests.
> The runtime generates the C# behind it. The user (or an LLM) only ever
> writes `.ino`.

---

## 1. The full grammar in one example

```ino
neuron Acme.WeeklyDigest
  "Summarises this week's client emails into one row in the status sheet."

  # ─── Synapses this neuron declares (record types on the wire) ───────────
  synapse Digested(week: int, summary: string)
  synapse Failed(reason: string)

  # ─── Synapses this neuron consumes (resolved at activation, not boot) ───
  using mailbox = neuron(Google.Gmail)
  using sheets  = neuron(Google.Sheets)
  using llm     = neuron(Ai.Chat)

  # ─── State (typed; persisted via Orleans grain storage) ─────────────────
  state lastRun: date = never
  state lastSummary: string = ""

  # ─── Telemetry (compiled to OpenTelemetry meters) ───────────────────────
  counter digests_emitted
  counter failures

  # ─── Handlers (the verbs) ───────────────────────────────────────────────
  on Activated:
    try:
      let threads = ask mailbox for "this week's threads with acme.com"
      let summary = ask llm to "summarise into 3 bullets" with threads
      ask sheets to "append row" with [today, summary]
      set lastRun = today
      set lastSummary = summary
      bump digests_emitted
      emit Digested(week: weekOf(today), summary: summary)
    catch err:
      bump failures
      emit Failed(reason: err.message)

  on Digested(d):
    log "digest for week {d.week} done"

  # ─── Declarative UI surface (the UI) ─────────────────────────────────────
  ui:
    UiKit.Column:
      UiKit.Card(title: "Weekly Acme Digest", body: lastSummary)
      UiKit.Row:
        UiKit.Button(label: "Run now", action: Activated)

  # ─── Scenarios (the gate — must go green before activation) ─────────────
  scenario "happy path":
    given mailbox returns [thread("alice@acme.com", "Q3 numbers")]
    given llm returns "alice sent Q3 numbers"
    when Activated
    then sheets received "append row"
    then Digested emitted with week == weekOf(today)
    then counter digests_emitted == 1

  scenario "mailbox unavailable":
    given mailbox throws "auth expired"
    when Activated
    then Failed emitted with reason matches "auth"
    then counter failures == 1
```

That is the whole neuron. There is **no other file**. The runtime:

1. Parses the `.ino` (one pass).
2. Generates a C# class under `obj/digitalbrain/Acme.WeeklyDigest.g.cs`.
3. Runs the scenarios in-process (no Reqnroll, no separate test project).
4. On green, registers the grain type with Orleans, activates it.
5. On any future edit to the `.ino`, hot-reloads via a fresh
   `AssemblyLoadContext`.

---

## 2. Top-level forms

| Form | Purpose | Required? |
|---|---|---|
| `neuron <Fqn>` | Declares the neuron. FQN is dotted PascalCase. | yes (exactly one) |
| `"<docstring>"` | One-line description. Appears in RFW default surface and graph hover. | optional |
| `synapse <Name>(...)` | Declares a wire record. The neuron *owns* this type. | zero or more |
| `using <alias> = neuron(<Fqn>)` | Resolved at activation; no boot-time validation. | zero or more |
| `state <name>: <type> = <default>` | Grain-persisted typed state. | zero or more |
| `counter <name>` / `histogram <name>` | Telemetry; auto-registered as OTel meter. | zero or more |
| `on <SignalOrSynapse>:` | Handler block. | zero or more |
| `ui:` | Declarative UI block using `UiKit` widgets. Omitted ⇒ kernel default surface. | optional |
| `scenario "<name>":` | Test block. **At least one is required**; no scenario ⇒ no activation. | yes (≥ 1) |

---

## 3. Synapse declarations replace `*.Contracts` projects

```ino
synapse Digested(week: int, summary: string)
```

Compiles to a sealed record:

```csharp
public sealed record Digested(int Week, string Summary)
  : Synapse("Acme.WeeklyDigest.Digested");
```

The record name + the owning neuron's FQN form the **wire type** —
`Acme.WeeklyDigest.Digested`. No constants file. No
`AcmeSynapseTypes.cs`. No `*.Contracts.csproj`. If another neuron wants
to listen for it, it references it by FQN:

```ino
neuron Acme.WeeklyReporter
  on Acme.WeeklyDigest.Digested(d):
    log "saw a digest for week {d.week}"
```

Resolution happens at activation. If `Acme.WeeklyDigest` is not
installed in the brain, `Acme.WeeklyReporter` activates with a
`Neuron.UnresolvedReference` synapse and parks itself in `idle`.

### Signals are gone

A v4 *signal* was a synapse without a target. v5 expresses the same
thing as a synapse with broadcast routing:

```ino
emit Digested(...)              # broadcast — anyone subscribing receives it
ask sheets to "append row" ...  # point-to-point — only `sheets` receives it
```

One concept. One record. Two routing modes.

---

## 4. Handlers

The verbs are deliberately small:

| Verb | Meaning |
|---|---|
| `ask <alias> to "<intent>" with <args>` | Point-to-point request; returns the response. |
| `ask <alias> for "<query>"` | Sugar for a read-shaped ask. |
| `emit <Synapse>(...)` | Broadcast; no return value expected. |
| `let <name> = <expr>` | Local binding. |
| `set <state> = <expr>` | Persist grain state. |
| `bump <counter>` | Increment OTel counter. |
| `log "<template>"` | Structured log; `{x}` placeholders. |
| `if <cond>: ... else: ...` | Branch. |
| `for <x> in <list>: ...` | Iterate. |
| `try: ... catch <e>: ...` | Capture cortex errors. |
| `escape c#: { ... }` | **Last resort** — verbatim C# block. Generates inline into the handler. Use only when the SDK lacks a primitive. |

The `escape c#` block exists for the same reason `unsafe` exists in C#:
to admit there are things the high-level vocabulary cannot say *yet*.
Every escape block is a backlog item to extend the SDK.

---

## 5. The `ui:` block — UI as data via `UiKit`

The Flutter shell does **not** import any neuron-specific code. It is a generic RFW renderer pointed at a payload. The `ui:` block in InoLang provides a clean, unified declarative syntax (utilizing the `UiKit` namespace) that compiles directly to this structured layout description payload.

Every `.ino` file can declare its RFW layout in plain declarative InoLang code utilizing standard widget schemas under the `UiKit` namespace.

### The `UiKit` Standard Widgets

The following widgets are formally supported and map directly to the underlying Flutter Remote Flutter Widgets (RFW) runtime representation:

| Widget Scheme | Description & Parameters |
|---|---|
| `UiKit.Card(title, body)` | A structured container. `title` sets the header text, and `body` sets the body content. |
| `UiKit.Button(label, action)` | Interactive trigger button. `label` is the displayed text, and `action` references the target handler or synapse to emit on tap. |
| `UiKit.Column(children)` | Vertical flex container. Holds an array of children widgets. |
| `UiKit.Row(children)` | Horizontal flex container. Holds an array of children widgets. |
| `UiKit.Text(content)` | Simple text typography element showing `content`. |
| `UiKit.Input(placeholder, binding)` | Two-way bound text input field. `placeholder` shows when empty, and `binding` binds directly to a state property. |

### Syntax & Indented Nesting
You can specify layouts either using traditional positional/named parameters or a colon-based indentation style for child elements:

**Indented style:**
```ino
ui:
  UiKit.Column:
    UiKit.Card(title: "Weekly Acme Digest", body: lastSummary)
    UiKit.Row:
      UiKit.Button(label: "Run now", action: Activated)
```

**Brackets/Parentheses style:**
```ino
ui:
  UiKit.Column(
    children: [
      UiKit.Card(title: "Weekly Acme Digest", body: lastSummary),
      UiKit.Button(label: "Run now", action: Activated)
    ]
  )
```

### RFW Mapping Details
The `ui:` block does not compile into executable C# layout code. Instead, the compiler treats it as a structured payload definition (a serializable JSON layout description). The neuron grain serves this compiled layout JSON payload via `INeuronMetadata.UiLayoutJson` dynamically at activation time, and the Gateway/Shell lazy-binds and paints the surface using RFW.

---

## 6. The `scenario` block — the gate

The L6 invariant from v3 stays: **no green scenario ⇒ no activation**.
v5 just moves the scenario into the `.ino`.

```ino
scenario "happy path":
  given mailbox returns [thread("alice", "subject")]
  when Activated
  then sheets received "append row" with [_, _]
  then Digested emitted with week == weekOf(today)
```

| Token | Meaning |
|---|---|
| `given <alias> returns <value>` | Stub a `using` dependency's response. |
| `given <alias> throws <message>` | Stub an exception. |
| `given <state> = <value>` | Pre-populate state. |
| `when <Synapse>(...)` | Fire the trigger. |
| `then <alias> received "<intent>" with <args>` | Assert a call shape. |
| `then <Synapse> emitted with <constraint>` | Assert a broadcast. |
| `then counter <name> == <int>` | Assert telemetry. |
| `then state <name> == <value>` | Assert state. |

The scenario runner is in `DigitalBrain.InoLang.Tests` (the only test
project for the language). It uses the existing `DigitalBrain.NeuronTesting`
harness *folded into* `DigitalBrain.Runtime` — no separate project.

---

## 7. The compile pipeline

```
foo.ino
  │
  │ 1. InoParser.Parse       → InoDocument (AST)
  │ 2. InoBinder.Bind         → BoundDocument (port aliases ⇒ Symbols)
  │ 3. InoCodegen.Emit        → C# source + scenario harness
  │ 4. Roslyn.Compile         → in-memory Assembly
  │ 5. ScenarioRunner.Run     → red | green
  │ 6. GrainRegistry.Activate → live in Orleans
  │
  ▼
foo.g.cs   (under obj/, gitignored)
foo.dll    (in-memory; persisted to obj/ for hot-reload)
```

Step 1–3 live in `inolang/DigitalBrain.InoLang/`. Steps 4–6 live in
`kernel/DigitalBrain.Runtime/`. **No catalog lookup anywhere** — `using`
aliases are resolved against `GrainRegistry` at activation time, lazily.

---

## 8. What the LLM writes (and what it doesn't)

When Ino converts an utterance, the LLM produces **only the `.ino`
file**. It does *not* write C#, does *not* write Flutter widgets, does
*not* write `.csproj` edits, does *not* write a manifest. Anything
outside the `.ino` is the runtime's job.

The LLM's prompt context is:

1. The utterance.
2. The brain's installed-domain FQN list + each neuron's docstring +
   each neuron's public synapse types.
3. The Ino grammar (this file, condensed).
4. Three or four exemplar `.ino` files.

The LLM may not invent FQNs not in (2). If it needs one, it emits an
`install <owner>/<repo>` directive instead, and Ino asks the user to
confirm before cloning.

---

## 9. Anti-patterns (don't author these even if Ino offers them)

- A `.ino` file with no `scenario:` block. The runtime will refuse to
  activate it; the LLM should have generated one.
- An `escape c#:` block longer than 5 lines. That logic belongs as a
  new SDK primitive; file a TODO and extend `DigitalBrain.SDK` instead.
- An RFW block with hand-rolled widget names not in the
  `rfw_kit` vocabulary. The shell will fall back to the default
  surface; extend the kit instead.
- `using` aliases to a non-existent neuron with no `install` directive
  next to them. Either depend on something installed or say what to
  install.
