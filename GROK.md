# GROK.md — standing orders for grok sessions in the DigitalBrain refit

You are ONE headless session in an orchestrated multi-session refit. An external orchestrator
dispatches you with a brief, verifies your work with gates, and owns git. Your brief is in
`plans/stage1/briefs/` — your launch prompt names the exact file. Scope authority:
`plans/RATIFIED-PRODUCT-DEFINITION.md` (what the product IS / IS NOT) and
`plans/GROK-ORCHESTRATION-STAGE1.md` (the stage plan). Never exceed your brief.

## Absolute rules

1. **NO git.** Never run `git commit`, `git checkout`, `git branch`, `git reset`, `git stash`.
   The orchestrator owns version control. You only edit files.
2. **NO AppHost / aspire.** Never run `dotnet run --project src/Kernel/DigitalBrain.AppHost`
   or docker compose. Running silos hold file locks that break builds.
3. **Tests run WITHOUT the aspire stack** (timing-window tests flake on a saturated machine).
4. **No new NuGet packages** and no version changes in `Directory.Packages.props` unless your
   brief explicitly grants it. Never weaken `TreatWarningsAsErrors`.
5. **Wire aliases are permanent** once real data exists: `db.*` kernel, `ui.*` vocabulary,
   `chat.*`/`probe.*` domains. Renaming an alias needs the orchestrator's explicit brief.
6. **Your LAST action** is writing your report to the path named in your brief. If a rule or
   reality conflicts with your brief, STOP, write the conflict into the report, and end — a
   written refusal beats silent improvisation.

## Commands

- Build:   `dotnet build DigitalBrain.slnx`            → must end with 0 warnings, 0 errors
- Test:    `& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe` (after build)
  — do NOT use `dotnet test`: its testing-platform handshake fails against this xunit.v3 exe
- Flutter: `flutter analyze` / `flutter test` per package under `src/Modules/UI/Flutter/{core,kit,shell}`
- Full gate: `pwsh scripts/gate.ps1` (add `-Flutter` to include Flutter checks)

## The 9 kernel traps (violating any = automatic rejection)

1. A turn cannot await the effect of its own Send — the outbox drain timer fires between turns;
   use `Neuron.FlushOutboxAsync(ct)` to drain inline.
2. Zero-receiver emissions create NO outbox entry — journaled, never delivered, never retried.
   Confirm routes exist before making a clickable/observable thing visible.
3. Grain-call reification: every non-framework grain call between neurons becomes durable
   journal facts. Kernel infrastructure interfaces must be listed in
   `CapabilityInvocation.FrameworkInterfaces`.
4. Deterministic validation/refusal must throw `NeuronAuthorizationException` (settles as
   "refused"). Any other handler exception is retried every 50ms up to 1000 attempts/30min.
5. Only `RequestSynapse<TResponse>` synapses materialize as model tools. Plain synapses are
   silently skipped.
6. Manifests are reflected, never written (`ModuleReflection.ManifestOf`). Contracts carry no
   `[Description]` — names ARE the documentation.
7. Models pass names where schemas want GUIDs (deterministic GUID derivation); missing
   value-type JSON fields bind silently to defaults — validate in handlers.
8. Declaring `IHandle<T>` on ANY class puts T in the broadcast catalog and spawns per-correlation
   ghost receivers on every Emit of T. In tests, use `OnUnboundSynapseAsync` overrides for
   routed-only sinks.
9. Keyword god-switches in handlers are banned.

## Banned forever (ratified)

Provider/action-specific synapses (`SalesforceReadSynapse`-style); moving outbox traffic onto
Orleans Streams; second job frameworks beside Execution; MAF types leaking outside AI/Behavior
internals; MAF workflow edges represented as Connections; hot-loading generated assemblies into
the silo; client-trusted identity; keyword god-switches.

## Method

- **TDD is mandatory**: failing test first, minimal green, then refactor. Two test kinds only:
  NeuronTest-style (one neuron's contract) and DigitalBrainTest-style (cross-neuron through the
  cluster) — both in `src/Tests/DigitalBrain.Tests` (fixture: `InProcessTestClusterBuilder` +
  shared `VolatileJournalStorageProvider` + `UseInMemoryReminderService`).
- Characterization pins that intentionally pin a KNOWN DEFECT carry a
  `// PIN-DEFECT(P0-x): <one line>` comment. Only the seam session that fixes P0-x may flip
  that pin, and must remove the marker when it does.
- Grain-turn awaits use `ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)`.
- No `/// <summary>` boilerplate. Comments only for constraints the code cannot express.
- C# 14 / net11.0 preview; preview analyzers are on; keep the build at 0 warnings.
- Quality bar: idiomatic modern C#, small deep interfaces, no dead code, no TODO litter —
  this refit exists to REMOVE demo residue, never to add more.

## Report format (write to the exact path your brief names)

```
# <brief id> — <role> report
## What changed        (files + one line each)
## Tests               (added / changed / flipped pins, with names)
## Gate                (paste the tail of your final build+test run)
## Conflicts & risks   (anything that blocked you or smells wrong — be specific)
## Out of scope        (things you noticed but did NOT touch)
```
