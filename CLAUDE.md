# CLAUDE.md — how to work in this repository

Canonical for every agent and contributor. `AGENTS.md` points here. Written to be followable by
any harness — Claude Code, Codex, Grok — with nothing but a shell and this repository.

---

## 1. What is being built

DigitalBrain is an AI-native operating system for durable agents on Orleans and Aspire. Its
ready-to-use primitives are **neurons and synapses**; users compose them in C# today and ultimately
describe behaviors in natural language. The thing that makes it worth building is:

> **A brain you program by writing ordinary C#, and that can program itself.**

The architecture in six lines:

- **The typed interface is the surface, the synapse is the substrate, the generator is the bridge.**
- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, replies. Both are journaled; neither is privileged.
- **Modules own vocabulary** — synapse records and neuron interfaces. Compile-time, needs a rebuild.
- **Behaviors own OS logic** — an approved single-file C# program runs on behalf of an owner-scoped
  `BehaviorNeuron`; the installation rail remains designed and unbuilt.
- **The Behavior SDK is the program-boundary foundation.** The shipped package supplies authoring
  interfaces, a constrained context, manifests, and artifact identities. Compilation, BDD
  verification, approval, and constrained execution remain part of the designed rail.
- **Every future install is a human-approved proposal**, journaled and reversible.

There is no architecture document. Section 7 of this file records what is Built versus Designed, and
that is the whole of the written plan of record — everything else is read from the code and the
tests. Do not silently reverse a decision recorded there. If evidence invalidates one, change section
7 in the same commit as the code.

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

Grilling is the discipline that makes step 1 real. It applies before building **and during it**.

### Before building

State a recommendation, state the strongest argument against it, and defend or fold. Present
evidence, not opinion. When a decision belongs to a person, put it to them with your recommendation
attached — never a neutral menu.

### During implementation — three moves

**Before the step — write the proof that fails.** Assert the behaviour the system *should* have and
watch it fail before writing the code that satisfies it. When the behaviour is not coming yet, keep
the proof and exclude it rather than deleting it. **Never a red root gate.**

Hold unfinished proofs with `[Fact(Explicit = true, DisplayName = "…")]`. Prove one red on demand with
`./tests/<proj>/bin/Debug/net10.0/<proj>.exe -explicit only` — the test runs and fails.

**Before the commit — grill the diff.** Three questions, answered in the commit message:

- What did I add that has no consumer today?
- What did I claim without running a command to check?
- What changed that I did not change?

**Before the claim — run it and quote it.** Evidence precedes assertion, always. "Tests pass" is not
a claim you may make without the output in front of you. If a step was skipped, say so. If something
failed, say so with the failure.

### Per phase

A real adversarial review at every phase boundary, and **verify its findings yourself**. Reviews are
worth their cost — a prior phase raised six findings and all six were real — but a review is a claim
like any other. Check its method, not only its conclusions.

---

## 4. Oracles and tools

**The mandatory path uses only the compiler, the test suite, and git.** These exist in every harness.

| Question | Oracle |
|---|---|
| Does this API exist? Is this signature right? | **The compiler.** Write a throwaway file referencing it and build. No `CS0246` proves the type exists |
| Does the system behave this way? | **The test suite.** Not the docs — several docs have been wrong |
| What was here before? Is this recoverable? | **git.** Retired trees live at `git show <sha>^:<path>` |

Optional accelerators ship as **project MCP** in root `.mcp.json` only:

| Server | Role |
|---|---|
| `aspire` | AppHost resource control |
| `codegraph` | repo architecture index |
| `context7` | package/docs lookup (`CONTEXT7_API_KEY`) |
| `microsoft-learn` | official Microsoft docs (HTTP) |
| `dart` | Dart/Flutter analysis MCP |
| `digitalbrain-mcp` | product MCP HTTP at `http://localhost:5000/mcp` — Aspire client over `IDigitalBrain.Get<ILlama32>`; tool `ask_llama32` returns `ChatResponse` (requires silo + MCP host) |

That file is the sole catalog. Shape matches sibling DigitalBrain projects: direct `aspire` / `dart`,
Windows `cmd /c npx -y …` for Node MCPs, native `"type":"http"` for remote HTTP. No `gcf-proxy`, no
user-profile paths, no global npm MCP installs as a requirement.

Claude project trust is in `.claude/settings.json`: `enableAllProjectMcpServers`,
`enabledMcpjsonServers` (same names as `.mcp.json`), and `permissions.allow` for `mcp__*` tools.
Open agents at the repository root with a **plain** start (`claude` / `grok` / `codex`) — do not use
`--strict-mcp-config` or isolated homes as proof of health. Codex cannot read `.mcp.json`; its only
adapter is `.codex/config.toml` with the same servers. Project `.grok/config.toml` holds matching
`[mcp_servers.*]` so name collisions with `~/.claude.json` still resolve to the project lines. Keep
`.mcp.json`, `.claude/settings.json` allow-lists, `.codex/config.toml`, and `.grok` MCP blocks in
lockstep. Do not enable plugins that inject MCP.

**If an accelerator is unavailable, say so and fall back to the oracles. Do not skip silently.**
`codegraph` keeps its index through the `RefreshCodeGraph` target in
`hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`, which runs `init` then `sync` over the
repository root — so `aspire run` and the root gate both refresh it, and the index spans C#, Dart and
the Flutter Windows runner alike. **That target belongs to the AppHost project and nowhere else.**
There is no `Directory.Build.targets`: a repository-root one is walked by Flutter's Windows native
build and has to be fenced off with empty stub files under `clients/`, which is why the last attempt
was deleted. Do not reintroduce either. The index lives in gitignored `.codegraph/`, so a fresh clone
has none until that build; if the server answers "isn't indexed", build the AppHost or run
`codegraph init` and retry rather than giving up on the tool. A session rooted above the repo cannot
reach the index at all; that absence is the environment, not a broken tool. `context7` needs
`CONTEXT7_API_KEY` in the process environment — fall back to Microsoft Learn when it is unset.
Note: Microsoft Learn returns the older `Orleans.EventSourcing.JournaledGrain` for journaling
queries. That is a different API from `Microsoft.Orleans.Journaling`. Do not conflate them.

**Check whether the ground moved.** Record `git rev-parse HEAD` and `git status --porcelain` at the
start of a session, and check both again before staging. This repository has been modified mid-session
by other tools. If something changed that you did not change, **surface it and stop** — do not revert
it and do not sweep it into your commit.

**Fan-out needs a scoring rule.** When dispatching parallel agents, give them the rule by which a
finding counts — for example "changes a decision that is currently open", not "find valuable
content". Without a rule they return summaries; with one they return findings.

**Agent harness (Claude / Grok / Codex).** MCP lives in `.mcp.json`. Harness adapters
(`.claude/settings.json`, `.grok/config.toml`, `.codex/config.toml`) hold only harness-native
non-MCP settings, except Codex’s required MCP TOML mirror. Do not fork the server list in three
places. If a tool only appears from user-level config or a marketplace plugin, treat it as outside
this repository’s harness and do not depend on it.

---

## 5. Gates

**The root gate, every phase, no exceptions:**

```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
```

**Never `--filter` for the completion gate.** Run it with a long timeout and poll. A project-scoped run has already missed a
failing contract that the root run caught. During TDD you may run the smallest owning project in the
foreground, but the root gate is what permits a completion claim.

That gate is the whole gate. The public site is a separate repository — `intochat/digitalbrain.docs`,
published at https://digitalbrain.tech — with its own `npm test` and `npm run build`. Nothing in this
repository builds, tests, or serves it, and no change here can break it.

One guard fails the build by design, and that is correct:

- Adding an `[Alias]` means updating the pinned-alias contract.

Public API is not baseline-locked while the framework is a pre-release alpha in flux; the
`PublicApiAnalyzers` baseline files were removed. Re-introduce them when a real release approaches
and the public surface should stop changing without review.

---

## 6. Rules

- **No comments as narrative, boilerplate, or commented-out code.** No `/// <summary>` restating a
  signature. Carry meaning in names, types, and tests instead — `[Fact(DisplayName = "...")]` is the
  supported way to make a test self-describing. The rule exists to stop narration and rot, not to
  forbid the rare case where a name genuinely cannot carry the information. Markdown prose is
  documentation, not a comment.
- **Code is the source of truth. Do not spend effort on documentation.** Ratified by the owner. Do
  not write design prose, decision records, or architecture narrative as a deliverable — express the
  design in types, names, and tests, and put durable operational rules here in `CLAUDE.md` instead.
  This repository holds no `docs/` tree at all — `CLAUDE.md` and `README.md` are the only prose it
  carries. This supersedes the earlier "keep decision records" rule and any task instruction to
  record a design in an architecture document.
- **One top-level type per file, unless the types are one closed co-evolving vocabulary.** Split when
  a type has an independent lifetime or an independent consumer. Keep together only for a closed set
  read as a set — an abstract base with its sealed cases, a type with a satellite enum that is
  meaningless alone — and then name the file for the family, never for one member.
- **Folders organize; namespaces carry public meaning.** A folder exists because a directory grew
  too large to scan, and it does **not** create a namespace. Package names may say `Modules` and
  `Contracts`; public namespaces never do. Samples and tests must not squat a product namespace.
- **Relative paths only.** Never reference anything under a user profile directory.
- **Latest deliberate package versions**, centrally in `Directory.Packages.props`.
- **Small slices, green at each boundary.** Build, run the owning project, run the root gate before
  claiming the slice is done.
- **Commit at green boundaries** with the diff-grill answers in the message.
- **Self-evolution is the product.** The only path to a live behaviour is a human-approved proposal
  through the journaled rail. That rail is not built yet — until it is, changes arrive the ordinary
  way.

---

## 7. Where things stand

The durable neuron and synapse foundation, owner-scoped client facade, generated module activation,
one-call durable AppHost composition, public testing path, and typed AI, Tasks, Google, Salesforce,
Flutter, and Quickstart families are built and proven. Time is built only through the durable one-shot
`ICountdown` capability and its deterministic recovery tests. Reminder, recurring interval/calendar
scheduling, DST records, and recurrence-library selection remain designed or open and unbuilt.

Flutter first vertical is Built: shell/scene vocabulary, Ui HTTP/SSE edge, module-owned
`WithUiEdge`/`WithFlutterHost`, headless Dart host, and Windows Material shell chrome
(`clients/digitalbrain_flutter` key/title list from `SceneOpened`). Full product chrome polish,
multi-principal IdP edge, and product journal observation on `IDigitalBrain` remain designed — do not
re-open Built Windows chrome as Designed.

Behavior proposal, approval, installation, execution, and rollback also remain designed and unbuilt.
`DigitalBrain.Behaviors` is a packable SDK foundation for public authoring interfaces, constrained
context, manifests, and revision/artifact identities; the nonpackable `DigitalBrain.Behaviors.Runtime`
contains only the canonical artifact codec. Neither project is a compiler, builder, worker, broker,
or execution rail. The post-rail model holds: one owner-scoped
`BehaviorNeuron` implementation owns journals/state/revisions; its single-file program is not a Neuron
and unknown code executes outside the silo through a capability broker. Pre-rail OS activation
(`DigitalBrainActivated` in Abstractions; pull compositions such as `ActivateDigitalBrain` /
`BootOnActivation`) may be Built samples/L1 — still not installed Behaviors and not the install rail.

This section is the single architecture authority: module status, ratified rules, known limitations,
and rejected shapes. Everything else was deleted — the eight `docs/architecture/*` topic authorities
and the `docs/superpowers/` and `docs/research/` trees folded into one page, and that page then left
for `intochat/digitalbrain.docs`, because 94% of documentation described a rail with no code. Do not
resurrect stage plans, scorecards, grills, session checklists, or a `docs/` tree. The site carries
vision for readers; code carries detail for everyone.

One assumption is load-bearing and unmeasured: **that a model can reliably emit behaviour scripts.**
That benchmark and the behavior proposal/install rail remain deliberately outside the built
foundation. Do not describe designed behavior execution or recurring/calendar Time as shipped.

Update this file through the same rail as everything else, and only when the loop actually improves.
