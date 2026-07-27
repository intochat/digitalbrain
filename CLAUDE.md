# CLAUDE.md — how to work in this repository

This file is the harness: how an agent operates here, what it may claim, and what it must prove
first. It is not product documentation — `README.md` carries what DigitalBrain is and what is Built
versus Designed. These two files are the only prose in the repository, and that is deliberate.

Written to be followable by any harness — Claude Code, Codex, Grok — with nothing but a shell and
this repository.

---

## 1. Starting a session

Order matters. Two of these bite silently if skipped.

1. **Build before you open an agent session.** `aspire run` or `dotnet build DigitalBrain.slnx`
   refreshes the CodeGraph index. The index lives in gitignored `.codegraph/`, so a fresh clone has
   none, and a session opened first will be told the repository "isn't indexed". That is a cold
   index, not a broken tool — build, or run `codegraph init`, and retry.
2. **Record the ground.** `git rev-parse HEAD` and `git status --porcelain`. Check both again before
   staging. This repository has been modified mid-session by other tools. If something changed that
   you did not change, **surface it and stop** — do not revert it, do not sweep it into your commit.
3. **Open at the repository root** with a plain start (`claude` / `codex` / `grok`). A session rooted
   above the repo cannot reach the index or the project MCP catalog.

First run from nothing must work with exactly two commands, and this is a gate, not an aspiration:

```powershell
git clean -fdx
aspire run
```

`aspire.config.json` points the CLI at `hosts/DigitalBrain.AppHost`, so no `--project` is needed from
the root.

---

## 2. The loop

Apply in order. Order matters — jumping to optimise or automate locks in waste.

1. **Question the requirement.** Trace it to a person or a consumer that exists *today*. "The plan
   says so" is not a reason. If nothing consumes it, it is a guess — say so out loud.
2. **Delete.** Prefer deleting a thing to simplifying it. Target a net reduction. If you are not
   adding things back occasionally, you are not deleting enough.
3. **Simplify what remains.** Then check you have not just moved the complexity somewhere else.
4. **Accelerate the feedback loop.**
5. **Automate.** Last. Never automate a process you have not first deleted and simplified.

---

## 3. Grilling

**Before building.** State a recommendation, state the strongest argument against it, and defend or
fold. Present evidence, not opinion. When a decision belongs to a person, put it to them with your
recommendation attached — never a neutral menu.

**Before the step, write the proof that fails.** Assert the behaviour the system *should* have and
watch it fail before writing the code that satisfies it. When the behaviour is not coming yet, keep
the proof and exclude it rather than deleting it — `[Fact(Explicit = true, DisplayName = "…")]`, run
on demand with `./tests/<proj>/bin/Debug/net11.0/<proj>.exe -explicit only`. **Never a red root gate.**

**Before the commit, grill the diff.** Answer these three in the commit message:

- What did I add that has no consumer today?
- What did I claim without running a command to check?
- What changed that I did not change?

**Before the claim, run it and quote it.** Evidence precedes assertion, always. If a step was
skipped, say so. If something failed, say so with the failure.

**Per phase**, run a real adversarial review and **verify its findings yourself**. A review is a claim
like any other; check its method, not only its conclusions.

---

## 4. Verification — behaviour first, never build-and-test alone

**A green test suite proves the code compiles and its logic holds. It does not prove the feature
works.** Most tests here drive `ScriptedChatClient`, a deterministic double: they prove plumbing,
journaling and wiring, and nothing whatsoever about a live model, a real silo, or a running edge.

If you reach for `dotnet test` first to validate a *behaviour* change, stop and drive the live system
instead. Tests are the regression net, not the proof.

### The ladder — climb all of it before claiming a behaviour works

1. **Compile.** `dotnet build DigitalBrain.slnx -c Release`.
2. **Bring it up.** `aspire run` (foreground) or `aspire start` (background, `aspire stop` to end).
   Then `mcp__aspire__list_resources` — every resource Running and Healthy. Not "probably up".
3. **Drive the real scenario** through the real edge — an HTTP call to `digitalbrain-ui`, or the
   Flutter shell. Not a unit test standing in for it.
4. **Read the journal.** `digitalbrain-mcp` is the audit source: `read_neuron_journal` for the causal
   facts, `read_chat_transcript` for the conversation, `list_active_neurons` for what activated.
   Confirm the synapses you expected fired, under one correlation id, in the order you expected.
5. **Cross-check Aspire telemetry** — see the table below for what that can currently tell you.
6. **Run the root gate** for regression safety.
7. **Only now** state the result, quoting the output you actually saw.

### Journals are the audit source; telemetry is a projection

That ordering is ratified and load-bearing: the kernel's durable journals are the truth, and
telemetry never becomes the audit source. Journals deliberately record causal facts only — synapse
type, caller, correlation, timestamp — never arguments, payloads, prompts or secrets. Telemetry tags
follow the same discipline.

### What telemetry actually works today

Measured against a running AppHost after a real chat turn. Do not claim more than this.

| Channel | Tool | State |
|---|---|---|
| Resource health | `mcp__aspire__list_resources` | **Works** |
| Console logs | `mcp__aspire__list_console_logs` | **Works** — noisy; Azurite request spam dominates |
| Durable journals | `digitalbrain-mcp` | **Works** — and is the audit source |
| Structured logs | `mcp__aspire__list_structured_logs` | **Empty.** Zero entries |
| Distributed traces | `mcp__aspire__list_traces` | **Empty of application spans** — only `dotnet-cli` |
| GenAI spans, metrics | `aspire otel` | **Do not exist** |

**This gap is the repository's top open defect, not a fact of life.** No host calls
`ConfigureOpenTelemetry`; no `IChatClient` pipeline calls `UseOpenTelemetry`; the kernel's
`ActivitySource("DigitalBrain")` is never registered with an exporter. Until that spine is built,
step 5 of the ladder can only confirm health and console output — say exactly that rather than
implying traces were checked. When you build it, this table is the thing to update.

---

## 5. Gates

**The root gate, every phase, no exceptions:**

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
```

**Never `--filter` for the completion gate.** Run it with a long timeout and poll. A project-scoped
run has already missed a failing contract that the root run caught. During TDD you may run the
smallest owning project in the foreground, but the root gate is what permits a completion claim.

**The client gates**, when you touch `clients/`:

```powershell
cd clients/digitalbrain_wire       ; dart test
cd clients/digitalbrain_flutter    ; dart analyze ; dart test
cd clients/digitalbrain_flutter/shell ; flutter analyze ; flutter test ; flutter build windows
```

Dart never sole-owns shell or scene semantics — the root `dotnet test` gate remains domain truth.

One guard fails the build by design, and that is correct: adding an `[Alias]` means updating the
pinned-alias contract. Public API is not baseline-locked while the framework is a pre-release alpha;
re-introduce `PublicApiAnalyzers` baselines when a real release approaches.

---

## 6. Oracles and tools

**The mandatory path uses only the compiler, the test suite, and git.** These exist in every harness.

| Question | Oracle |
|---|---|
| Does this API exist? Is this signature right? | **The compiler.** Reference it and build. No `CS0246` proves the type exists |
| Does the system behave this way? | **The live system**, then the test suite. Not prose — prose has been wrong |
| What was here before? Is this recoverable? | **git.** Retired trees live at `git show <sha>^:<path>` |

**Context7 before any library-touching code.** Resolve the id, query the docs, then write. Applies to
Orleans, Aspire, Microsoft.Extensions.AI, Flutter, MCP — every time, including APIs you think you
know. Fall back to `microsoft-learn` when `CONTEXT7_API_KEY` is unset. Note that Microsoft Learn
returns the older `Orleans.EventSourcing.JournaledGrain` for journaling queries; that is a different
API from `Microsoft.Orleans.Journaling`. Do not conflate them.

**CodeGraph before any non-trivial edit.** One `codegraph_explore` call returns the relevant source
plus call paths plus a blast radius, replacing a grep-and-read loop. It indexes what git tracks — C#,
Dart, and the Flutter Windows runner alike, 443 files out of 11,342 on disk. Two consequences worth
knowing: it honours `.gitignore` including negations and nested files, and because `obj/` is ignored
**source-generated code is invisible to it** — 96 `*.g.cs` on disk, none indexed. For generated
activation code, use the compiler.

The index is refreshed by the `RefreshCodeGraph` target in
`hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`, so `aspire run` and the root gate both
refresh it. **That target belongs to the AppHost project and nowhere else** — see the traps below.

**Project MCP lives in root `.mcp.json` and nowhere else:**

| Server | Role |
|---|---|
| `aspire` | AppHost resource control, logs, traces |
| `codegraph` | repository index — source and call paths |
| `context7` | package and framework docs |
| `microsoft-learn` | official Microsoft docs |
| `dart` | Dart/Flutter analysis |
| `digitalbrain-mcp` | the running brain at `http://localhost:5000/mcp` — needs silo + MCP host up |

**If an accelerator is unavailable, say so and fall back to the oracles. Do not skip silently.**

**Fan-out needs a scoring rule.** When dispatching parallel agents, give them the rule by which a
finding counts — "changes a decision that is currently open", not "find valuable content". Without a
rule they return summaries; with one they return findings.

---

## 7. Harness configuration

MCP servers live in `.mcp.json`. Harness adapters hold only harness-native settings, except Codex's
required MCP mirror: `.claude/settings.json`, `.codex/config.toml`, `.grok/config.toml`. Keep the
server list in lockstep across all four; do not fork it. Do not enable plugins that inject MCP.

Skills and plugins are enabled declaratively in `.claude/settings.json`. Alongside the general-purpose
plugins, the `dotnet-agent-skills` marketplace (`dotnet/skills`) supplies .NET-specific skills; the
curated set enabled here is `dotnet11`, `dotnet-msbuild`, `dotnet-nuget`, `dotnet-test`, `dotnet-diag`,
`dotnet-ai`, `dotnet-aspnetcore`. The rest of that marketplace is deliberately off — `dotnet-data`
(no EF), `dotnet-maui` and `dotnet-blazor` (the client is Flutter), `dotnet-template-engine`,
`dotnet-test-migration` (already on xunit.v3), and `dotnet` itself, whose LSP overlaps the existing
`csharp-lsp` plugin. Enable one on demand rather than enabling all of them by default; every enabled
skill costs context on every session.

---

## 8. Repository conventions

- **No comments as narrative, boilerplate, or commented-out code.** No `/// <summary>` restating a
  signature. Carry meaning in names, types, and tests — `[Fact(DisplayName = "...")]` is the
  supported way to make a test self-describing. The rule stops narration and rot; it does not forbid
  the rare case where a name genuinely cannot carry the information.
- **Code is the source of truth. Do not spend effort on documentation.** Ratified by the owner. Do
  not write design prose, decision records, or architecture narrative as a deliverable. Durable
  operational rules go here; product status goes in `README.md`. There is no `docs/` tree and none
  should be created — the public site is a separate repository, `intochat/digitalbrain.docs`.
- **One top-level type per file**, unless the types are one closed co-evolving vocabulary read as a
  set. Then name the file for the family, never for one member.
- **Folders organize; namespaces carry public meaning.** A folder does not create a namespace.
  Package names may say `Modules` and `Contracts`; public namespaces never do. Samples and tests must
  not squat a product namespace.
- **Relative paths only.** Never reference anything under a user profile directory.
- **Latest deliberate package versions**, centrally in `Directory.Packages.props`.
- **Small slices, green at each boundary.** Commit at green boundaries with the grill answers in the
  message. One logical change per commit. Never `--no-verify`; never `reset --hard`, `push --force`
  or `checkout --` without confirming first.

---

## 9. Known traps — verified here, do not re-hit

Every entry below was hit in this repository and cost real time. Add to it only what you have
actually reproduced.

- **Reentrancy deadlock.** `DrainAsync` awaits `Deliver` *inside the emitting neuron's turn*, and
  `NeuronConcurrency.RequireSerializedTurns` forbids reentrancy. A handler that calls back
  synchronously into the neuron that emitted its trigger **hangs indefinitely**. Facts flow one way:
  `UserMessaged` carries the transcript, and the answer returns as a directed `AssistantAnswered`.
- **No repository-root `Directory.Build.targets`.** Flutter's Windows native build walks up into it
  and has to be fenced off with empty stub `Directory.Build.props`/`.targets` under `clients/`. That
  is why the previous root targets file and all four stubs were deleted. Per-project targets only.
- **CodeGraph follows `.gitignore`**, so generated sources under `obj/` are absent from the index.
  Do not conclude a generated symbol does not exist because CodeGraph cannot see it.
- **`DOTNET_ROOT` breaks `aspire run` while `dotnet build`/`test` still pass.** The CLI resolves the
  SDK itself, but the AppHost executable resolves the runtime through `DOTNET_ROOT`. If it points at
  a .NET 10 location the AppHost fails to start with a missing `Microsoft.NETCore.App` 11 message.
- **llama3.2 selects tools badly.** A 3B-class model with one registered tool calls that tool on
  prompts that ask for nothing — reproduced on every live greeting so far, including a bare "Hi".
  The mis-selection is journaled as `CapabilityToolSelected`, so it is visible rather than silent.
  Mitigate by registering tools only under real intent, or switch model; the seam is model-agnostic.
- **One conversation is single-threaded end to end.** While a turn is being answered, the chat neuron
  is occupied for the whole model-plus-capability chain. Fine for one owner in dev; a real constraint
  at scale.
- **Windows file locks masquerade as build breaks.** A running `digitalbrain_flutter.exe` fails the
  Windows build with `LNK1168`; a stale VitePress or esbuild process blocks directory deletion. Check
  for a holding process before believing the error.

---

Update this file through the same rail as everything else, and only when the loop actually improves.
