# Coding split: Roslyn, DotNet, and the coding agent

Date: 2026-09-22. Status: approved, not implemented.

## Decision

`src/Modules/Microsoft` keeps the existing GitHub module untouched. Two new modules sit beside it. MSBuild is not a module and not a grain. The coding agent is not a module either.

`Microsoft.CodeAnalysis.Workspace` cannot cross a grain call. `MSBuildWorkspace.OpenSolutionAsync` and every later query or edit must run inside the same activation. That is why Roslyn owns the loader and why `IMSBuild` is not a public interface.

The `dotnet` CLI never returns a workspace. It stays a separate module with no reference to `Microsoft.CodeAnalysis`.

## Modules

```
src/Modules/Microsoft/
  Microsoft/                         existing GitHub + Aspire connection, unchanged
  Contracts/  Aspire.Hosting/  Tests/

  Roslyn/
    Contracts/                       DigitalBrain.Modules.Microsoft.Roslyn.Contracts
      IRoslyn.cs                     Open, Reload, Read, symbols, references, diagnostics,
                                     map, skeleton, callers, implementations, derived, edit
      Workspace/                     DTOs moved from Coding/Contracts/Workspace
      Signals/
    Roslyn/                          DigitalBrain.Modules.Microsoft.Roslyn
      RoslynModule.cs
      RoslynNeuron.cs                grain type microsoft.roslyn, key = workspace key
      Workspace/                     SolutionWorkspace, SolutionQueries
      Editing/                       ChangeSetEditor, CodeFixCatalog, draft parser
      MSBuild/                       MSBuildSolutionLoader only
    Tests/

  DotNet/
    Contracts/                       DigitalBrain.Modules.Microsoft.DotNet.Contracts
      IDotnet.cs                     Build, Test
      BuildOutcome.cs
      TestOutcome.cs
    DotNet/                          DigitalBrain.Modules.Microsoft.DotNet
      DotNetModule.cs
      DotnetNeuron.cs                grain type microsoft.dotnet, key = workspace key
      DotnetRunner.cs
      Process/                       IProcessRunner, this module only
    Tests/

src/Modules/Coding/
  Contracts/
    Drafts/                          ICodeDraft
    ChangeSet/                       IChangeSet
    Artifacts/
  Coding/
    CodingModule.cs                  drafts, change-set orchestration, validation, Git
    Drafts/
    ChangeSet/                       calls IRoslyn to edit and IDotnet to build or test
    Validation/                      contract catalog, draft check, artifact store
    Git/                             keeps its own process runner until a Git neuron exists
  Tests/
```

Namespaces: `DigitalBrain.Microsoft.Roslyn`, `DigitalBrain.Microsoft.DotNet`. Assemblies follow `DigitalBrain.Modules.Microsoft.<Product>`.

`ICodeWorkspace` leaves Coding and becomes `IRoslyn`. Coding drops its `Microsoft.CodeAnalysis` reference. `Microsoft.CodeAnalysis.Workspaces.MSBuild` is referenced only by the Roslyn implementation project.

AppHost loads `RoslynModule`, `DotNetModule`, and `CodingModule` separately. `MicrosoftModule` is not required for coding. Coding's `WithSolution` still owns the solution path and passes it into `IRoslyn.Open`. DotNet options are the `dotnet` executable and the timeouts. Roslyn options are the MSBuild locator settings. Neither module stores the solution path in configuration.

## What moves

| From Coding | To |
|---|---|
| `Contracts/Workspace` and `WorkspaceNeuron` queries | `IRoslyn` |
| `SolutionWorkspace`, `SolutionQueries` | `Roslyn/Workspace` |
| `ChangeSetEditor`, `CodeFixCatalog`, draft C# parse | `Roslyn/Editing` |
| `MSBuildSolutionLoader`, `ISolutionLoader` | `Roslyn/MSBuild` |
| `DotnetRunner`, `BuildOutcome`, `TestOutcome`, `TestFailure` | `DotNet` |

Stays in Coding: `ICodeDraft`, `IChangeSet` orchestration, contract catalog, draft check, artifact store, Git.

## Coding agent

The agent is an ordinary `IAgent`. Roslyn, DotNet, and Behavior do not register `AIFunction`s and do not reference the AI module. `BehaviorAgentTools` in IntoChat remains the only factory. The agent definition names the tools. `AgentToolContext.ScopeId` is the workspace key used as the grain key.

| Tool | Grain |
|---|---|
| `workspace_open`, `workspace_map`, `symbols`, `references`, `diagnostics`, `skeleton`, `edit` | `IRoslyn` |
| `build`, `test` | `IDotnet` |
| `code_contracts`, `code_draft_read`, `code_draft_save`, `code_draft_check` | Coding |
| `behavior_deploy`, `behavior_start`, `behavior_stop`, `behavior_rollback`, `behavior_logs` | `IBehaviorProgram` |

A behavior is created by that sequence: read contracts, read the workspace, save a draft, check the draft, deploy the artifact id, start, read logs. `code_draft_check` is the compile gate. `build` runs the real solution, not an uncommitted draft. A deployed behavior does not register new agent tools.

## Out of scope

Moving the GitHub tree under `Microsoft/GitHub`. A public `IMSBuild` or `IGit` grain. A shared Process module. Letting a draft add tools during a turn.
