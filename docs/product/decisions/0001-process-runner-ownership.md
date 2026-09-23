# 0001 — One shared process runner

- Status: accepted
- Date: 2026-09-23
- Deciders: P0.1 (autonomous, on highlevel v0.2 recommended working assumptions)
- Supersedes: none
- Related: plan P0.1 ("Consolidate duplicate `ProcessRunner`"); current-state §7 "Behaviors, many ways"

## Context

`src/Modules/Coding/Coding/` and `src/Modules/Microsoft/DotNet/DotNet/Process/` each held a
byte-identical `IProcessRunner`, `ProcessRunner` and `ProcessResult` (111 identical implementation
lines, differing only by namespace). Both registered the runner with
`TryAddSingleton<IProcessRunner, ProcessRunner>()`. Principle 2 ("keep one of each") requires one
shared implementation; the plan left the owner open and required this ADR before deleting either copy.

## Decision

Keep the `DigitalBrain.Microsoft.DotNet` implementation as the single shared process runner. Delete
`src/Modules/Coding/Coding/{ProcessRunner,IProcessRunner,ProcessResult}.cs`. The
`DigitalBrain.Coding` module now consumes `DigitalBrain.Microsoft.DotNet.IProcessRunner` and
references the DotNet module.

## Rationale

- Dependency direction stays clean: DotNet is a dependency-light provider module (Contracts +
  Kernel); Coding is a consumer that already references Roslyn. Coding → DotNet adds no new heavy
  graph, whereas the reverse pulled Roslyn's `Microsoft.Build.Locator` target into DotNet and tripped
  `MSBL001` (a `Microsoft.Build.Framework` reference without `ExcludeAssets=runtime;PrivateAssets=all`).
- The runner's MSBuild environment defaults (`MSBUILDNODEREUSE`, `MSBUILDTERMINALLOGGER`,
  `DOTNET_CLI_UI_LANGUAGE`) belong to the module whose declared job is running the dotnet CLI, and are
  harmless for the `git` calls Coding makes.
- DotNet already owns `DotnetRunner`, the runner's primary consumer.

## Consequences

- `CodingModule` registers the DotNet runner; `GitRunner`, `ContainedProcessRunner` and
  `CodeValidationService` use the DotNet types. Coding's `ContainedProcessRunner` (the sandboxed
  validation runner) remains the second implementation of the shared interface.
- No new project is introduced and no other module changes. Both modules are developer-profile only
  (plan P0.3), so the product profile is unaffected.
