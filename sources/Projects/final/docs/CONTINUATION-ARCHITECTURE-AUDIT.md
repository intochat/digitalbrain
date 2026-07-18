# Continuation prompt — Full architecture assessment after the OS-FROM-INO run

You are a senior C# developer, Microsoft Orleans expert, and a hostile architecture reviewer working on **DigitalBrain** at `E:\Projects\final` (.NET 11 preview 4, Orleans 10.1.1-preview.1, Aspire 13.4.x/13.5-preview CLI, Hex1b 0.164.1, Reqnroll + xunit v3/MTP, Flutter client). Tooling: `Filesystem:search_files`/`directory_tree` crash this repo — `list_directory` + targeted `read_text_file`/`read_multiple_files` only; context7 / microsoft-learn / hex1b skill for any API question; no `C:\Users` paths.

## Mission

**Audit, not implement.** Assess the entire architecture at HEAD after the OS-FROM-INO execution run (`docs/OS-FROM-INO-PLAN.md`, stages OS0–OS7, continuation `docs/CONTINUATION-OS-FROM-INO.md`), and measure how far the system actually is from the owner's full vision: **all UI via neurons, the whole OS described and driven by `.ino`, apps installed/updated/uninstalled live, ino as a truthfully-oriented OS assistant.** `src/` is **read-only** for this session — the only writes allowed are the report and doc corrections. Fixes happen in a separate session from the report's brief.

**Trust nothing written — only code and command output.** Commit messages, plan checkmarks, and handoff notes are claims, not evidence. Precedent #1: commit `4a2dbe4` claimed a full TUI redesign; the kernel matched, the client hid four defects (B1–B4). Precedent #2: the SIM-era "hygiene" commits made some Thens *tolerant* to keep the FQN gate at 0f — so a green gate can be green because the assertion was weakened. Hunt for both failure modes specifically.

## Phase 1 — Ground truth

`git log --oneline` since `2243410`; map every commit to OS0–OS7 (or to nothing — flag orphan commits); read each stage's diff summary (`git show --stat`). Determine: which stages landed, which are partial, what the handoff note in the plan claims vs what the diffs show. Read the current `brain.ino`, `os/` listing, `pa-files/marketplace` listing, and the plan's filled decision row.

## Phase 2 — Re-run every gate yourself

1. `git status` (tree must be clean), `.\run-ci.ps1` full.
2. `dotnet run simulation.cs -- "Distribution" --ci` and read the CTRF — count scenarios, not just exit code.
3. `dotnet run simulation.cs -- "tag:Shell"` and `-- "ino:gmail-last-senders"`.
4. `aspire run` smoke: time boot-to-workspace, capture the resource list, verify `BootManifestApplied` lands on the timeline with a hash matching `brain.ino` on disk.
5. Run (or write, if the run never produced it) the traceability audit: activated experiences ⊆ seeded capsule ids ∪ named substrate list.
6. Tamper test for fake-green: pick two high-sev scenarios (one N+1, one N−1 if it exists), temporarily break the behavior they claim to prove (in-memory experiment, revert immediately), and confirm the scenario actually fails. A gate that cannot fail is not a gate — any tolerant-Then that swallows a real regression is a finding of the highest severity.

## Phase 3 — Vision-conformance matrix (score 0–10 each, every score justified by file:line or command output)

- **P1 Boot-from-ino:** zero topology *data* remaining in `AddDefaultDigitalBrainTopology`/Sdk (grep-by-read for hardcoded kernel/tier/seed literals); BOOT001–BOOT010 diagnostics exist, are unit-tested, and are fatal; `world: from` works; no secret material anywhere in `brain.ino` or `os/*.ino`; `ino.cs` is parse→lower→run and nothing else.
- **P2 UI-via-neurons (the "full on" pillar):** enumerate EVERY user-visible element in the TUI and the Flutter client and classify each one: (a) neuron-emitted content (`UiSurface`), (b) shell-state-driven layout (`WorkspaceChanged`), (c) **remaining client chrome** — and for each (c) item give a verdict: legitimate client physics (caret, scroll, cell painting, error boundary) vs *should be OS state* (anything deciding what exists/where it lives/what's focused). The (c)-should-be-OS list, with effort estimates, is the core deliverable of this pillar. Verify the D5 prefix routing is actually deleted (not commented, not fallback-resurrected), `UiNeuron` is gone, wildcard subscribers number exactly two, `SurfacePlacement` precedence (user > capsule default > main) is resolved in ShellNeuron only, and TUI/Flutter render the same workspace from the same two streams (run both if the machine allows; record env gaps honestly).
- **P3 OS-as-`os/`:** every kernel experience has a capsule; CI packs `os/` on green; the substrate list in the plan matches reality (nothing activated that lacks identity, nothing in `os/` that never activates); the new header lines (`region/pinned/order/requires/system`) flow parser → manifest → packager → install with append-only `[Id]`s.
- **P4 Lifecycle:** install / `UpdateBundle` / `UninstallBundle` all zero-restart; N+1 and N−1 assertions are real (Phase-2 tamper test); journal provably untouched by uninstall; `requires:` surfaces correctly with no solver; grants — `GrantRequested/Decision/Revoked` journaled, enforcement points match D-OS6 exactly (install-time declared-emits + RuleHost emission-time; compiled-neuron trust boundary *stated*, not silently exceeded); `system: true` refuses uninstall with a surface.
- **P5 ino orientation:** read `BuildPersonaAsync` — every persona fact traceable to a grain read, zero static inventory narrative; run the scripted orientation exchange; verify each OS tool emits journaled synapses and the destructive set cannot bypass ApproveAction (read the tool registration, then try to find a path around the guard); `pin_widget` is genuinely emits-as-tools.
- **P6 InoLang freeze integrity:** diff the grammar surface since `2243410` — no new statement kinds inside `on …:` blocks, no `show card` extension, header lines only; InoValidator INO001–006 untouched in meaning; the Q2 amendment is recorded in INOLANG-RFC, not just implemented.
- **P7 Serialization & state discipline:** plan Appendix B inventory vs actually-present roundtrip probes (list any type missing one); no collection-expression-into-`IReadOnlyList` regressions; `UiSurface` extension append-only (old `google-auth.brain` fixture deserializes); Region handled as string with unknown→main fallback; no assertion anywhere assumes per-grain journal durability (E3 is not done — find any test or demo claim that pretends otherwise).

## Phase 4 — Defect hunt (client-side first, per the B1–B4 precedent)

Read `TaskManagerClient.cs` and the Flutter workspace scaffold end-to-end: stale routing remnants, duplicate widget instantiation, IDs flowing wrong across pack→publish→install→pin, editor/content routed to wrong fields, hardcoded references that should come from state. Then: dual seeding paths, dead code the Step-2 deletion list promised to remove but didn't (check `docs/DELETED.md` completeness against the diffs), persona fragments that restate live state, orphaned synapses (declared, never handled/emitted), `os/` capsules whose `triggers:` don't match their neuron's actual handlers.

## Phase 5 — "Go full on" gap analysis (5 Steps order, Step 1 first)

Rank everything still between HEAD and the full vision, each item with: what it is, which pillar it serves, whether any Step-1 verdict in the plan should be re-opened, and a dumbest-honest next move. Expected candidates to evaluate (reject any that are "just in case"): remaining client chrome from P2(c); `FocusSurface` as OS state (the predicted ~10% re-add); GoogleAuth → first real L3 `.AsSilo` (D4); per-grain journal durability E3 (the deepest "OS remembers" honesty gap); hot-reload of `brain.ino` vs restart (was rejected — does any finding force it?); ino *composing* new surfaces/experiences conversationally (Creator → marketplace loop as the app-authoring story); Flutter parity debt; dock launch semantics; grant revocation UX. Output a recommended next-stages table (OS8+…) with gates, sized.

## Deliverable

Write **`docs/ARCHITECTURE-ASSESSMENT-OS.md`**: HEAD + date; stage-landing table from Phase 1; gate-rerun results incl. tamper-test outcomes; the P1–P7 matrix with scores + evidence; B-numbered defect list (severity, file:line, one-line fix direction — fixes NOT applied); fake-green findings called out separately; the Phase-5 ranked gap table; a verdict paragraph: how true is "this OS boots from a text file, draws itself from neurons, and can explain itself" today, in plain words. Every claim cites code or command output. If the run was partial, assess what exists — do not grade unbuilt stages, list them as unbuilt.
