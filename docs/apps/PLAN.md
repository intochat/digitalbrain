# Apps as scenarios — proof plan

Status: all six slices built 2026-09-27 (uncommitted on refactor/csharp-module). Developer mode only, no per-app identity, no CI.

Verified in the running product: GroupChat (Luna + Gemma, live scenario judged), Assistant and WordCount (C# sandbox)
verify green; an app created from a one-sentence request through `/packages/drafts` was authored, built on the first
attempt, verified (including its @live scenario) and published.

## Idea

An app is its `.feature` scenarios (what the user sees) plus an implementation (config, prompts, C# scripts)
that makes them pass. A revision is installable only when its scenarios are green in a throwaway workspace.
Steps are platform-owned (a step library bound to neuron contracts); agents only compose them, so they
cannot fake a pass.

## Decisions

- Keep all existing app code (LeadGenerator, built-in apps, `/built-in`, `src/Apps` manifests) until the new marketplace replaces it.
- Scenarios run with real models; a `ScriptedLLM` double is selectable per scenario (`Given ... on a scripted model`).
- New Author and Builder agents, both on Gpt56Luna.
- Shipped apps live in `src/Applications/IntoChat/Apps/<App>/`, published as `intochat/<app>` at startup after their scenarios pass.

## App folder

```
src/Applications/IntoChat/Apps/<App>/
  app.json        name, description, neuron wiring, settings
  app.feature     the spec
  prompts/*.md    system and initial prompts
  scripts/*.cs    only when logic is needed (sandbox)
```

## Slices (each: tests, aspire build, aspire run)

1. **IGroupChat (AI module)** — participants {name, ILLM key, system prompt}, rules {maxRounds, stop at agreement}, moderator.
   `Ask` runs rounds, emits `GroupChatTurn` and `GroupChatConcluded`; `Read` returns the transcript.
2. **Specs module** — `IFeature` (Gherkin parse, step binding spans for highlighting, `Run`, `FeatureVerified`),
   `IScenarioRun` (scratch workspace, doubles, trace, green/red/amber), `StepLibrary` (settings, ask app,
   signal fired, turn/round counts, judged k-of-N, text/file shown), `ScriptedLLM` double.
3. **Apps module** — `PackageContent.Files` (multi-file), install/publish gate on a green `IFeature` run,
   script-less apps configured from `app.json`.
4. **Shipped apps** — GroupChat (Luna + Gemma), Assistant (IAgent + prompt), Timer report (sandbox script).
5. **Flutter** — Marketplace, App page (highlighted scenarios, status dots, Run), GroupChat transcript surface.
6. **Create app** — `IAppAuthor`: request → Author drafts `app.feature` → user approves → Builder writes the
   implementation → verify → up to 3 retries with the failing trace → publish as `{you}/{app}`. Flutter create flow.

## First scenario

```gherkin
Scenario: Two models converge on one answer
  Given the group chat has participants "Luna" on Gpt56Luna and "Gemma" on Gemma4
  And the chat stops at agreement or after 3 rounds
  When I ask "Name one product idea for dog owners"
  Then "Luna" and "Gemma" take turns, starting with "Luna"
  And each turn after the first refers to the previous turn
  And the chat concludes with one answer within 3 rounds
```
