# GROK.md — standing orders for the DigitalBrain refit

Codex is the implementer on `stage1-stabilize-strangle`; no grok worker sessions are part of the
current workflow. Scope authority is `plans/RATIFIED-PRODUCT-DEFINITION.md` (what the product IS /
IS NOT) and `plans/GROK-ORCHESTRATION-STAGE1.md` (the stage plan).

> **Owner amendment — 2026-08-11:** the central automated-test project was deleted intentionally.
> Do not recreate tests or run any .NET or Flutter test command during this refit. Production source
> is the current behavioral truth. A proper test framework will be designed during final hardening,
> with tests owned by each module rather than one repository-wide test project. Salesforce Contracts
> remains a permanent product boundary for neuron and synapse interfaces.

## Absolute rules

1. **Commit only on `stage1-stabilize-strangle`.** Vlad remains merge authority for `master`.
2. **AppHost is smoke-only.** Stop it before every build; running silos hold file locks.
3. **No automated tests for now.** Never invoke `dotnet test`, a test executable, or
   `flutter test`. Do not recreate deleted test projects or move Flutter tests in this stage.
4. **No new NuGet packages** and no version changes in `Directory.Packages.props` unless your
   brief explicitly grants it. Never weaken `TreatWarningsAsErrors`.
5. **Wire aliases are permanent** once real data exists: `db.*` kernel, `ui.*` vocabulary,
   `chat.*`/`probe.*` domains. Renaming an alias needs the orchestrator's explicit brief.
6. If a rule or reality conflicts with the ratified scope, STOP and write the conflict into a
   report before continuing — a written refusal beats silent improvisation.

## Commands

- Build: `dotnet build DigitalBrain.slnx -warnaserror --nologo` → 0 warnings, 0 errors
- Flutter production source: `flutter analyze lib` in each package under
  `src/Modules/UI/Flutter/{core,kit,shell}`
- Full source gate: `pwsh scripts/gate.ps1` (add `-Flutter` for production Flutter analysis)

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
   ghost receivers on every Emit of T. Routed-only sinks must not accidentally become broadcast
   handlers.
9. Keyword god-switches in handlers are banned.

## Banned forever (ratified)

Provider/action-specific synapses (`SalesforceReadSynapse`-style); moving outbox traffic onto
Orleans Streams; second job frameworks beside Execution; MAF types leaking outside AI/Behavior
internals; MAF workflow edges represented as Connections; hot-loading generated assemblies into
the silo; client-trusted identity; keyword god-switches.

## Method

- **SOURCE → GREEN → GRILL → GATE → COMMIT**: characterize the current production source and
  routes; make the smallest coherent change; adversarially review the diff against the kernel
  traps and ratified scope; run the build/static gate; then commit on the Stage-1 branch.
- Do not treat deleted historical tests or their reports as current behavioral authority.
- Grain-turn awaits use `ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)`.
- No `/// <summary>` boilerplate. Comments only for constraints the code cannot express.
- C# 14 / net11.0 preview; preview analyzers are on; keep the build at 0 warnings.
- Quality bar: idiomatic modern C#, small deep interfaces, no dead code, no TODO litter —
  this refit exists to REMOVE demo residue, never to add more.

## Report format

```
# <brief id> — <role> report
## What changed        (files + one line each)
## Source evidence     (symbols, routes, searches, and behavior inspected)
## Gate                (paste the tail of the final build/static-analysis run)
## Conflicts & risks   (anything that blocked you or smells wrong — be specific)
## Out of scope        (things you noticed but did NOT touch)
```
