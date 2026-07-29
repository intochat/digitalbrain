# CLAUDE.md — how to work in this repository

The harness for any coding agent here: what to run, what to prove, what not to touch. Product and
Built-versus-Designed status live in `README.md`; these two files are the only prose in the repo.

## Start a session

1. **Build before opening the session.** `aspire run` or `dotnet build DigitalBrain.slnx` refreshes
   the CodeGraph index. It lives in gitignored `.codegraph/`, so a fresh clone has none and the
   server will say "isn't indexed" — build, or run `codegraph init`, and retry.
2. **Record the ground.** `git rev-parse HEAD` and `git status --porcelain`, and check both again
   before staging. If something changed that you did not change, **surface it and stop** — do not
   revert it, do not sweep it into your commit.
3. **Open at the repository root**, plain start. A session rooted above the repo reaches neither the
   index nor the project MCP catalog.

From a clean tree, two commands must produce a working system. This is a gate:

```powershell
git clean -fdx
aspire run
```

## The loop

In order — jumping ahead locks in waste.

1. **Question the requirement.** Trace it to a consumer that exists *today*. "The plan says so" is
   not a reason. If nothing consumes it, say so out loud.
2. **Delete.** Prefer deleting to simplifying. If you never add things back, you are not deleting
   enough.
3. **Simplify what remains** — then check you have not just moved the complexity.
4. **Accelerate the feedback loop.**
5. **Automate last.** Never automate what you have not first deleted and simplified.

## Grilling

**Before building:** state a recommendation, state the strongest argument against it, defend or fold.
Evidence, not opinion. When the decision is the owner's, put it to them with your recommendation
attached — never a neutral menu.

**Before the step:** write the proof that fails and watch it fail. If the behaviour is not coming
yet, exclude the proof rather than delete it — `[Fact(Explicit = true, DisplayName = "…")]`, run with
`./<proj-dir>/bin/Debug/net11.0/<proj>.exe -explicit only` — suites live beside what they test, so
`<proj-dir>` is under `src/`, `os/tests/` or `tests/fixtures/`. **Never a red root gate.**

**Before the commit:** answer these in the message.

- What did I add that has no consumer today?
- What did I claim without running a command to check?
- What changed that I did not change?

**Before the claim:** run it and quote it. If a step was skipped, say so. If something failed, say so
with the failure. Verify a review's findings yourself — a review is a claim like any other.

## Verifying — behaviour first

**A green suite proves the code holds. It does not prove the feature works.** Most tests drive
`ScriptedChatClient`, a deterministic double: they prove wiring and journaling, nothing about a live
model, silo or edge. If you reach for `dotnet test` to validate a *behaviour* change, stop and drive
the live system. Tests are the regression net, not the proof.

1. `dotnet build DigitalBrain.slnx -c Release`
2. `aspire run` (or `aspire start` / `aspire stop`), then `list_resources` — every resource Healthy.
3. Drive the real scenario through the real edge — HTTP to `digitalbrain-ui`, or the Flutter shell.
4. **Read the journal** via `digitalbrain-mcp`: `read_neuron_journal`, `read_chat_transcript`,
   `list_active_neurons`. Confirm the expected synapses fired, one correlation id, right order.
5. Cross-check Aspire against structured logs and spans.
6. Root gate for regression.
7. Only now claim, quoting what you saw.

Journals are the audit source; telemetry is a projection and never replaces them. Journals hold
causal facts only — never arguments, prompts or secrets. Production telemetry follows the same rule;
the product AppHost opts Development into prompt and response capture for local diagnosis.

**What Aspire can actually tell you** (measured against a live AppHost after a real chat turn):

| Channel | State |
|---|---|
| `list_resources`, `list_console_logs` | works — console output is noisy with Azurite spam |
| `digitalbrain-mcp` journals | works, and is authoritative |
| Structured logs | works — MCP invocation completion includes tool name and error state |
| Application traces | works — ASP.NET, Orleans and kernel spans carry causal identifiers |
| GenAI spans, metrics | works — provider, model, duration, token usage and finish reason; prompt and response content only when the AI module explicitly enables it |

`dotnet test os/tests/DigitalBrain.OS.Product.Tests -c Release -- -explicit only` is the live oracle.
It starts and stops the product AppHost, drives a real Gemma4 turn and retry, confirms the durable
transcript and correlation, checks owner-scoped active-neuron discovery, and verifies GenAI usage
and Development message content in the exported span.

## Gates

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release
```

**Never `--filter` for the completion gate** — a project-scoped run has already missed a failing
contract the root run caught. Run it with a long timeout and poll. During TDD run the smallest owning
project; the root gate is what permits a completion claim.

Touching `clients/` adds:

```powershell
cd clients/digitalbrain_wire          ; dart test
cd clients/digitalbrain_flutter       ; dart analyze ; dart test
cd clients/digitalbrain_flutter/shell ; flutter analyze ; flutter test ; flutter build windows
```

Dart never sole-owns shell or scene semantics; the root gate is domain truth. Adding an `[Alias]`
fails the build until the pinned-alias contract is updated — that guard is correct.

## Oracles

The mandatory path is the compiler, the test suite, and git — they exist in every harness.

| Question | Oracle |
|---|---|
| Does this API exist? | **The compiler.** Reference it and build; no `CS0246` proves the type |
| Does it behave this way? | **The live system**, then the tests. Never prose — prose has been wrong |
| What was here before? | **git.** `git show <sha>^:<path>` |

**Context7 before any library-touching code** — Orleans, Aspire, Microsoft.Extensions.AI, Flutter,
MCP — including APIs you think you know. Fall back to `microsoft-learn` when `CONTEXT7_API_KEY` is
unset, but note it returns `Orleans.EventSourcing.JournaledGrain` for journaling queries, which is a
different API from `Microsoft.Orleans.Journaling`. Do not conflate them.

**CodeGraph before any non-trivial edit.** One `codegraph_explore` returns source, call paths and
blast radius, replacing a grep-and-read loop. It indexes what git tracks, across C# and Dart alike;
because it honours `.gitignore`, **source-generated code under `obj/` is invisible to it** — use the
compiler for generated symbols. The index is refreshed by `RefreshCodeGraph` in
`os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`, which belongs to that project alone.

MCP lives in root `.mcp.json`: `aspire` (resources, logs, traces), `codegraph`, `context7`,
`microsoft-learn`, `dart`, and `digitalbrain-mcp` (the running brain; needs silo + MCP host up).
**If an accelerator is unavailable, say so and fall back. Do not skip silently.** When dispatching
parallel agents, give them the rule by which a finding counts, or they return summaries.

Harness adapters — `.claude/settings.json`, `.codex/config.toml`, `.grok/config.toml` — hold only
harness-native settings plus Codex's required MCP mirror. Keep the server list in lockstep; never
fork it; never enable plugins that inject MCP. .NET skills come from the `dotnet-agent-skills`
marketplace, curated in `.claude/settings.json`. Every enabled plugin costs context every session —
add one only when it earns that, and drop it when the tree stops matching it.

## Conventions

- **No comments as narrative, boilerplate, or commented-out code.** No `/// <summary>` restating a
  signature. Carry meaning in names, types and tests — `[Fact(DisplayName = "...")]` makes a test
  self-describing. The rule stops narration and rot, not the rare name that cannot carry the load.
- **Code is the source of truth; do not spend effort on documentation.** No design prose, decision
  records or architecture narrative as a deliverable. Durable rules go here, product status in
  `README.md`. **There is no `docs/` tree and none should be created** — the site is a separate
  repository, `intochat/digitalbrain.docs`.
- **One top-level type per file**, unless they are one closed vocabulary read as a set; then name the
  file for the family, never for one member.
- **Folders organize; namespaces carry public meaning.** A folder does not create a namespace.
  Packages may say `Modules`/`Contracts`; public namespaces never do. Samples and tests must not
  squat a product namespace.
- **Relative paths only** — never reference a user profile directory.
- **Latest deliberate package versions**, centrally in `Directory.Packages.props`.
- **Commit at green boundaries**, one logical change, grill answers in the message. Never
  `--no-verify`; never `reset --hard`, `push --force` or `checkout --` without confirming.
- **Check `README.md` before claiming any capability ships** — much of the product is Designed, not
  Built.

## Traps — hit here, do not re-hit

Add only what you have reproduced.

- **Reentrancy deadlock.** `DrainAsync` awaits `Deliver` inside the emitting neuron's turn and
  `NeuronConcurrency.RequireSerializedTurns` forbids reentrancy, so a handler calling back into the
  neuron that emitted its trigger **hangs**. Facts flow one way: `UserMessaged` carries the
  transcript, the answer returns as a directed `AssistantResponded`.
- **No repository-root `Directory.Build.targets`.** Flutter's Windows native build walks into it and
  then needs empty stub props/targets under `clients/` to fence it off. Per-project targets only.
- **`DOTNET_ROOT` breaks `aspire run` alone.** `dotnet build`/`test` resolve through the CLI, but the
  AppHost executable resolves the runtime through `DOTNET_ROOT`; pointed at a .NET 10 location it
  fails with a missing `Microsoft.NETCore.App` 11.
- **llama3.2 mis-selects tools.** A 3B-class model with one registered tool calls it on prompts that
  ask for nothing, including a bare "Hi". Journaled as `CapabilityToolSelected`, so it is visible.
  Register tools only under real intent, or switch model — the seam is model-agnostic.
- **A conversation is single-threaded end to end.** The chat neuron is occupied for the whole
  model-plus-capability chain.
- **Windows file locks look like build breaks.** A running `digitalbrain_flutter.exe` fails the build
  with `LNK1168`; stale node processes block directory deletion. Find the holding process first.
