# Unified First-Party Apps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every first-party app is a package (manifest + spec + tests + behaviors) through the one
publish gate; the compiled `IApplication`/`UiComposer` runtime and host built-ins are deleted once
the package Assistant reaches full parity.

**Architecture:** Grow module contracts first (assistant capability, agent form tools) so nothing
app-shaped stays compiled; then rebuild Settings, Assistant and CustomerResearcher as shipped
packages under `src/Applications/IntoChat/Apps/`; then delete `src/Apps/`, the `IApplication`
runtime and `BuiltInAppEndpoints`.

**Tech Stack:** Orleans grains, `ShippedAppPublisher` pipeline, sandbox C# file-based apps
(`ICSharpFile`), Flutter shell, xUnit facts via `UnitTest.Create().WithModule<...>()`.

**Spec:** `docs/superpowers/specs/2026-09-30-unified-first-party-apps-design.md`

## Global Constraints

- Never build/test the `.slnx`; per-project `dotnet test` only. Stop stray `IntoChat`/
  `DigitalBrain.Mcp` processes before rebuilding (they lock bin DLLs, MSB3027).
- Persisted grain state: append `[Id(n)]`, never renumber; concrete arrays only.
- No boilerplate `/// <summary>`; sentence-shaped test names; `TestContext.Current.CancellationToken`.
- Full parity gate: the compiled Assistant (and its endpoints) is deleted only when the package
  Assistant covers voice, attachments, receipts, model picker, agent tools and live results, proven
  by its `tests.cs` scenarios plus green unit suites and an `aspire run` smoke.
- Wire-shape changes in C# records must be mirrored in the Dart shell in the same change.

## Review Focus

1. Consent/install of a package app whose manifest lists `agentTools` — the host must still
   register those tools for a turn (AgentEndpoints path) once `FirstPartyApps` no longer exists.
2. A stale shell (old launcher tiles `assistant`/`customer-researcher` launch keys) against the new
   endpoints — must 404 cleanly, not 500.
3. Reaped-behavior wakeups: assistant behaviors re-run from the top; UI composition setup must be
   idempotent (Set with read revision, dedup via neuron state).
4. `OpenWindow` flows (assistant window per conversation) after `ApplicationCatalog.Start` is gone —
   the surface must exist before `IWorkspace.EnsureOpenAsync` references it.
5. Voice input over the script edge: audio bytes are large; the behavior path must bound size the
   way `AssistantTranscription.MaxAudioBytes` does today.

---

### Task 1: Move the assistant capability out of `src/Apps` into an Assistant module

The compiled Assistant's grain code (`AssistantNeuron` + partials, transcription, turn execution,
conversations, model catalog, receipts, tool selection, `WorkspaceFormTools`) is module-grade
neuron capability, not app packaging. Create `src/Modules/DigitalBrain/Assistant` with
`DigitalBrain.Modules.Assistant.Contracts` (holding `IAssistant`, `AssistantState`,
`AssistantWindow`, `AssistantTranscriptionResult`, signals) and `DigitalBrain.Modules.Assistant`
(the neuron + services). `WorkspaceFormTools` moves to the AI module's Agents area
(`IAgentToolFactory` registration moves from `AssistantApp.Configure` to `AssistantModule`).
The `.Ui()` composition moves out of `IApplication` into an idempotent `EnsureComposed()` step the
neuron runs on `Activate`/`OpenWindow` (plain grain calls on `ISurface`/`ILayout`/..., no
`UiComposer`/`ApplicationCatalog`). Keep the existing part names and keys (`{workspace}/applications/assistant`)
so the shell and stored state keep working. Move `src/Apps/Assistant/Tests` into the module test
project. Steps: move files, retarget namespaces to `DigitalBrain.Assistant`, wire
`AssistantModule` into IntoChat composition, delete `AssistantApp`/`.Ui()` usage, build, run the
moved unit tests, commit.

### Task 2: Same treatment for CustomerResearcher

`src/Modules/DigitalBrain/CustomerResearcher` (or fold into an existing research-related module) —
neuron keeps its key shape, `EnsureComposed()` replaces `.Ui()`, module registered in IntoChat,
tests moved, commit.

### Task 3: Delete the `IApplication` runtime and `UiComposer`

With no `IApplication` implementors left: delete `IApplication.cs`, `AppDefinition.cs`,
`ApplicationCatalog.cs`, `ApplicationComposition.cs`, `ApplicationFacts.cs`, and
`Composition/UiComposer.cs` (`UiNode`, `UiAppBuilderExtensions`). Replace remaining
`UiComposer.NameOf` call sites with a `UiParts.NameOf` helper in Flutter.Contracts
(same "{key}/{part}" shape). Build every affected project, run Flutter analyze/tests, commit.

### Task 4: Settings as a shipped package; delete BuiltIn

Author `src/Applications/IntoChat/Apps/Settings/{app.json,app.spec.md,tests.cs}` (+ behavior if
needed) covering today's `SettingsApp` scenarios (read/apply preferences, apply-from-draft via
button click). Route `/built-in/*` behavior through the package/app endpoints; delete
`BuiltIn/SettingsApp.cs` and `BuiltInAppEndpoints.cs` (assistant activation moves to the module's
endpoint or existing app endpoints). Update the shell if it calls `/built-in`. Tests + commit.

### Task 5: Assistant and CustomerResearcher app packages

Author `src/Applications/IntoChat/Apps/Assistant` (already exists — extend) and
`.../CustomerResearcher`: manifests carry `uiEntry`, `agentTools`, permissions, example prompts;
`app.spec.md` scenarios cover the parity checklist (send/answer, model pick, voice draft,
attachment, receipt shown, stop); `tests.cs` drives them with scripted models through real
contracts. Launcher tiles come from the catalog (drop the hardcoded `assistantLauncherEntry` /
`customerResearcherLauncherEntry` constants in the shell in favor of catalog entries with
`uiEntry`). Commit.

### Task 6: Final deletions + verification

Delete the rest of `src/Apps/` (folders now empty of logic), any dead endpoints, and stale launcher
constants. Full verification: affected unit suites, Flutter analyze + tests, `aspire run` all
Healthy, code review. Commit.
