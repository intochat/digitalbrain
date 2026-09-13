# Coding agent research (2026-09-13)

Status: research record for `coding-agent-design.md`. Every claim cites its primary source (official docs,
package registries, or a file:line in a repository on this machine, all read on 2026-09-13). Sections are
numbered so the design can cite them as R1.2, R4.3 and so on.

The question: how to build, inside DigitalBrain, a C# coding agent that has Roslyn inside, loads this
solution, visualizes it, refactors it through a swarm of per-file agents that discuss a change in a
dynamically created group chat, rebuilds the running kernel live, and learns the owner's conventions.

## R1. Roslyn as the agent's instrument

### R1.1 Packages and versions

| Package | Latest on nuget.org | Notes |
|---|---|---|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 5.9.0 | IAW pins 5.3.0. Versions 5.0.0 and later read `.slnx` (dotnet/roslyn PR #77326; discussion #80418 confirms no extra package is needed). |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 5.9.0 line | `Renamer`, `SymbolFinder`, `Formatter`, `DocumentEditor`, `SyntaxGenerator` live here. |
| `Microsoft.Build.Locator` | 1.11.2 | Register before any `Microsoft.Build` type is touched, from a method that references none of them (Microsoft Learn, "Find and use a version of MSBuild"). |
| `Microsoft.CodeAnalysis.Features` / `CSharp.Features` | 5.9.0 line | Code fixes and refactorings (`CodeFixProvider`, `CodeRefactoringProvider`). Public on nuget.org; its surface moves between minor versions. Phase 2 material. |

Sources: nuget.org flat-container indexes; https://github.com/dotnet/roslyn/discussions/80418.

### R1.2 Workspace lifecycle facts that shape the design

- `MSBuildWorkspace.OpenSolutionAsync(path)` evaluates projects **out of process** through a `BuildHost`
  executable shipped in a `BuildHost-netcore` folder next to the application. Single-file publish and trimming
  break its discovery; the folder must be in the output (dotnet/roslyn issues #80127, #75292). Every working
  reproduction still calls `MSBuildLocator.RegisterDefaults()` in the host process, and IAW's
  `Directory.Build.props` sets `DisableMSBuildAssemblyCopyCheck=true` because the SDK otherwise refuses to copy
  `Microsoft.Build.*` assemblies to the output (R2).
- `Solution`, `Project`, `Document` are **immutable snapshots**. A change produces a new `Solution`; nothing
  reaches disk until `Workspace.TryApplyChanges(newSolution)` (Roslyn wiki "Solutions, Projects, Documents";
  `MSBuildWorkspace.TryApplyChanges` in `PublicAPI.Shipped.txt`). This is what lets several agents compose
  edits into one snapshot, diagnose it, and commit or discard it atomically.
- `Solution.WithDocumentText(id, text)` folds an external edit (the owner's IDE, `dotnet format`, a git
  checkout) into the snapshot without reloading the solution.
- `Solution.GetProjectDependencyGraph()` gives the project DAG; `Project.ProjectReferences` and
  `Project.MetadataReferences` give the edges for the solution map.
- Queries are static on `SymbolFinder` and take a `Solution`: `FindReferencesAsync` (with a document filter
  and `IFindReferencesProgress` overloads), `FindCallersAsync` (returns `SymbolCallerInfo`),
  `FindImplementationsAsync`, `FindDerivedClassesAsync(type, solution, transitive, projects)`,
  `FindDeclarationsAsync(project, name, ignoreCase, filter)`, `FindSymbolAtPositionAsync`.
  `Renamer.RenameSymbolAsync(solution, symbol, options, newName)` returns the renamed solution.
- A symbol has a stable string identity across calls: `ISymbol.GetDocumentationCommentId()`
  (`T:Ns.Type`, `M:Ns.Type.Method(System.String)`), resolved back with
  `DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation)`.
- Failures during load surface only through `RegisterWorkspaceFailedHandler` (the `WorkspaceFailed` event is
  obsolete in 5.3). IAW never wires it, so a failed project load is invisible there (R2.3).
- A design-time evaluation reads `obj/project.assets.json`; a project that was never restored resolves no
  framework references and every type is an error. The tree the workspace reads must be restored.

### R1.3 What "Roslyn inside" buys over a text agent

Claude Code gained an LSP tool in v2.0.74 (go-to-definition, find references, hover, diagnostics) with a C#
plugin. The reported gain is semantic answers in milliseconds against seconds of grep and not reading whole
files into context. Serena, the LSP-based MCP many agents use, exposes `find_symbol`,
`find_referencing_symbols`, `find_implementations`, `get_symbols_overview`, `get_diagnostics_for_file`,
`insert_before_symbol`, `insert_after_symbol`, `replace_symbol_body`, `rename_symbol`.

Roslyn gives strictly more for C#: a whole-solution semantic model, callers with call-site syntax, data-flow
and control-flow analysis, the project graph, source-generator output, compile-without-build diagnostics, and
a solution-wide rename with conflict detection. The last three cannot come from outside the .NET process.

Sources: https://claudelog.com/faqs/what-is-lsp-tool-in-claude-code/, https://oraios.github.io/serena/01-about/035_tools.html.

### R1.4 Prior art: Roslyn MCP servers

| Server | Tools | Freshness | Notable |
|---|---|---|---|
| MarcelRoozekrans/roslyn-codelens-mcp | 67: navigation, type hierarchy, call graph, `analyze_change_impact`, `find_tests_for_symbol`, `find_uncovered_symbols`, diagnostics, code fixes, complexity, async and disposable audits, DI registrations, exception flow, data flow, `rename_symbol`, `change_signature`, source generators, public API surface, `resolve_stack_trace` | watches `.cs/.csproj/.props/.targets`; stale projects and their dependents recompile lazily on the next query | preview mode on rename and code actions; trust gate before running analyzers; `{items,totalCount,truncated,limit,summary}` envelope; native MCP cancellation; stdio or streamable HTTP |
| VladD2/RoslynMcpServer | 41: skeletons, method bodies, AST edits (add member, update body, implement interface, extract interface), build/test/run/format, project graph, NuGet audit, ILSpy decompile | one `FileSystemWatcher` per root records dirty `.cs` paths and syncs before symbol searches; `.csproj`/`.sln` changes only hint "stale" | lazy background load; host timeout must be at least 600 s; compact responses plus server-side logs; strips inherited MSBuild env vars before `dotnet` commands |
| AtomicBlom/RoseMCP, MadQ/RoslynMcp, JoshuaRamirez/RoslynMcpServer, pzalutski-pixel/sharplens-mcp, brendankowitz/dotnet-roslyn-mcp | 10 to 41 each | reload on demand | preview-then-apply rename appears in three of them independently |

Convergent lessons: one warm workspace per solution, never return an unbounded list, preview before writing,
treat `.csproj` changes as a reload trigger. None of them lets several agents edit one snapshot, none closes
the loop with a build and restart of the running product, and none remembers the owner.

Sources: the README of each repository.

## R2. IAW's C# agents (`E:\intochat\Projects\IAW\src\Agents.CSharp`)

The owner's prior art. 22 files, about 3,200 lines, namespace `IAW.Agents.Coding`. Packages from
`E:\intochat\Projects\IAW\Directory.Packages.props`: Roslyn 5.3.0, `Microsoft.Build.Locator` 1.11.2,
`Microsoft.Extensions.AI` 10.4.1, Orleans 10.0.1, Octokit 14.0.0. `Directory.Build.props` sets
`DisableMSBuildAssemblyCopyCheck=true`.

### R2.1 Workspace lifecycle (`Roslyn/Workspace/SolutionWorkspaceManager.cs`, `Roslyn/RoslynAgent.cs`)

- `MSBuildLocator.RegisterDefaults()` is the first statement of `Agents.Host/Program.cs:5`.
- `RoslynAgent` is a `[Reentrant]` grain (RoslynAgent.cs:19). `OnActivateAsync` fires a background load
  (lines 27-31) that walks parent directories for `*.slnx` then `*.sln`, opens the solution, materializes every
  compilation and persists a call graph, reverse call graph and inheritance tree as JSON in durable state
  (keys `call-graph`, `reverse-call-graph`, `inheritance-tree`, `type-catalog`, lines 640-678). Queries answer
  from the durable graphs while MSBuild loads. This warm-on-activation plus durable-cache shape is the
  transplantable idea.
- Every semantic entry point degrades to syntax-only or string search when the workspace is not ready
  (RoslynAgent.cs:50 vs 71, 93 vs 128).
- Refresh is a full reload on `CodeChangedMessage` (lines 624-628) and ignores the changed paths.

### R2.2 Tool surface

`IRoslyn` (IRoslyn.cs:26-39): GetTypeMap, FindReferences, AnalyzeArchitecture, DetectPatterns,
GetDependencyGraph, AnalyzeBuildErrors, GetCallersOf, GetCalleesOf, GetImplementors, GetBaseTypes,
GetOverrides, GetWorkspaceStatus, ImplementInterface. Helpers: `RoslynTools` (syntax and semantic
diagnostics, capped at 20), `CodeModificationTools` (7 syntax-only edits), `RefactoringTools` (MoveType,
InlineVariable, RenameSymbol, ExtractMethod, ChangeSignature). Only `RenameSymbolAsync` uses `Renamer`
(RefactoringTools.cs:171); the rest are `CSharpSyntaxRewriter`s. `DotNetAgent` shells out to
`dotnet build|test|format|run` with regex parsing; `NuGetAgent` polls the nuget.org flat container;
`GitHubAgent` is Octokit with three operations. The live instructions are `IRoslyn.AgentInstructions`
(IRoslyn.cs:17-24); `Prompts/CodingAgentPrompts.cs` is unreferenced.

### R2.3 Defects to avoid when porting (each verified in source)

1. Tools capture `_workspaceManager` by value while it is still null (RoslynAgent.cs:37-39 vs 650), so the
   semantic path of `AnalyzeSemanticsAsync` and `RenameSymbolAsync` is unreachable as a tool.
2. `IsToolSafeReturnType` (Core/Agents/Agent.Tools.cs:170-183) silently drops `BuildAsync`, `TestAsync` and
   `RunAsync` because they return records; the instructions still advertise them (IDotNet.cs:31).
3. Every edit is `File.WriteAllTextAsync`; the `Solution` and compilation cache go stale and nothing
   invalidates them. The rename computes a new solution and discards it (RefactoringTools.cs:171-188).
4. `ExtractMethod` is syntax-only (parameters default to `object`), `ChangeSignature` rewrites the declaration
   but no call site, `InlineVariable` and the rename fallback rename by identifier text across scopes,
   `MoveType` overwrites the target file without a check.
5. `FindReferencesAsync` runs a full-solution search per substring-matched symbol with no cap
   (RoslynAgent.cs:101-120).
6. `MSBuildWorkspace.Diagnostics` is never read; reindex leaks the previous workspace (line 650).
7. `OrchestrationCompiler.cs` compiles against "whatever assemblies the silo has loaded".
8. Tests assert only the unloaded-workspace messages; no test loads a real solution.

### R2.4 IAW's orchestration and communication (`E:\intochat\Projects\IAW\src`)

Facts that matter for the swarm design; the full sweep is in the session record.

- **No plan engine, no group chat.** Orchestration is three grain layers: a `ThreadAgent` router whose
  instructions are the policy (`Agents/Orchestration/IThread.cs:16-44`) with two tools `SendToAgent` and
  `Orchestrate` (`ThreadAgent.cs:55-63`); an `AgentSelector` whose "plan" is a JSON string from the model
  (`IAgentSelector.cs:15-35`, `SelectionResult.cs:3-17`); and a `CodeOrchestrator` that has the model
  **write a standalone C# console program** against the grain interfaces, validates it
  (`Core/Orchestration/CodeValidator.cs`), builds it, retries up to three times with the first 15 error lines
  fed back (`CodeOrchestratorAgent.cs:237-261`), and runs it out of process. `Microsoft.Agents.AI.Workflows`
  1.0.0 is referenced by DevUI only and used nowhere.
- **Communication**: direct grain calls; typed peer messages `IReceiver<TMessage>` with a
  `MessageReceipt(Accepted, ReceiptId, Timestamp, RejectionReason)` (`Core/Communication/IReceiver.cs:5-9`);
  pub/sub over Orleans streams auto-subscribed by implementing `IStreamConsumer<TEvent>`
  (`Core/Agents/Agent.Streams.cs:23-45`), stream names derived from type names (`CodeChangedEvent` becomes
  `code.changed`), thread-scoped namespaces `thread/{scope}/{event}` (`Agent.Events.cs:37-50`). Shared
  context is a `TaskLedger` grain rendered into prompts (`Core/Grains/TaskLedgerGrain.cs`).
  `ConsiliumResponseEvent(TaskId, ModelId, Response, Confidence)` is a designed, never-published multi-model
  vote (`Core/Messages/Events/ConsiliumResponseEvent.cs:4-11`).
- **Model per agent is a facet**: `[Llm<Fast>] IChatClient` on the grain constructor, `ServiceKey =
  "{provider}-{modelId}"`, tiers `Fast/Balanced/Reasoning` mapped in the AppHost
  (`Core/AI/LlmAttribute.cs`, `Aspire.Client/LlmRegistration.cs:40-80`). Fourteen per-model agents exist
  (`Agents/LLM/*.cs`) and are excluded from routing by prompt (`CodeOrchestratorAgent.cs:60`).
- **Human in the loop**: every tool call passes `ToolApprovalMiddleware`; a `[Reentrant]` `ApproverAgent`
  registers a `TaskCompletionSource` before publishing `approval.requested`, holds the grain alive with
  `DelayDeactivation`, Telegram renders buttons, and a granted non-once approval is summarized by the model
  into a durable natural-language policy re-injected by `PolicyContextProvider`
  (`Agents/Security/ApproverAgent.cs:106-133, 166-180`).
- **Self-redeploy loop exists**: `IAspire.RestartResourceAsync` calls the Aspire MCP tool
  `execute_resource_command` with `resource-stop`, a 3 s delay, `resource-start`
  (`Agents/Infrastructure/AspireAgent.cs:139-157`); `DeployAsync` builds through the DotNet agent, restarts
  the `assistant` resource and schedules a `deploy-verify` job (lines 202-222). `AspireAgent` spawns
  `aspire mcp start --non-interactive` over stdio and surfaces its tools verbatim (lines 93-123).
- **DevUI** watches agent traffic live: a `BackgroundService` forwards about 17 Orleans streams to SignalR and
  a cytoscape page with a LIVE/REPLAY slider (`DevUI/Visualization/AgentEventForwarder.cs`,
  `wwwroot/visualization/index.html`).
- **Testing**: `AgentTest<TAgent>` on an Orleans `TestCluster`, every `[Llm<T>]` key mapped to one scripted
  `MockChatClient` (`ReturnsText`, `ReturnsStream`, `ThrowsOnSend`), architecture guard tests by reflection.

## R3. digitalbraincore's brain MCP (`E:\intochat\digitalbraincore`)

### R3.1 What it is

A minimal model: "a neuron fires a signal along a synapse" (CONTEXT.md:3-20). Two nouns, two verbs (fire,
connect/disconnect), one query (read). A neuron is a durable Orleans grain with one receive slot, outgoing
synapses, two bounded journals (512 entries / 512 KB), and the **latest signal of each type it has received**,
kept durably outside the journal window (`src/Kernel/DigitalBrain/Neuron/Neuron.cs:109-111`). One MCP server
named `brain` with four tools `fire`, `connect`, `disconnect`, `read` at `/mcp` (stateless streamable HTTP,
`ModelContextProtocol.AspNetCore` 2.2.0; `src/Kernel/DigitalBrain.Silo/Program.cs:12-20`). No auth, no
owner model, no vector store, no `list`. The sibling repo (R6) is the descendant of this model with
`cancel`, `describe` and `call` added.

### R3.2 How the system "learns from the owner" there

There is no memory subsystem; memory is the graph. `docs/architecture/02-working-style-memory.md:33-46`
describes the loop: the owner says "always run tests before committing"; the model fires `Note { text }` at
a neuron it names `run-tests-before-commit` and connects the topic neuron `git` to it. At the next task the
model reads `git`'s synapses, follows them, and reads the latest `Note` on each. `Confirmed {}` increments a
tally; a correction is a newer `Note` on the same neuron (latest-per-type returns the new text; the journal
keeps the old). Signal vocabulary is invented by the client, not the kernel. Recall is taught in the `read`
tool description itself (`BrainTools.cs:56`): "read a topic's synapses, follow each target, read its state."
A Grok CLI benchmark scored 2/2/2 on store, recall after restart, and correct-with-original-correlation
(`docs/architecture/grok-benchmark.md`).

Lessons recorded there that apply here: rejections are advice (errors relayed verbatim,
`BrainTools.cs:68-83`); a tool runs on whatever thread the function-invocation loop is on, so grain state is
touched only through the grain's own scheduler (`AgentNeuron.cs:32-35`); a model timeout must not escape a
reaction or the drain retries it forever (`AgentNeuron.cs:98-101`).

### R3.3 What is not there

No coding tools (grep for Roslyn, MSBuild, ProcessStartInfo, git: nothing), no per-project scoping, no
Aspire commands. `continuation.md` describes a superseded generation and is not current state.

## R4. Multi-agent orchestration: Microsoft Agent Framework

Repo pins `Microsoft.Agents.AI` 1.20.0 and `Microsoft.Agents.AI.Workflows` 1.20.0
(`Directory.Packages.props:44-46`); nuget.org has 1.21.0 for both. The "always latest" rule means the plan
bumps to 1.21.0 in the phase that first uses workflows.

### R4.1 Group chat

`AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
{ MaximumIterationCount = n }).AddParticipants(a, b, ...).Build()`. The manager factory receives the
participant list; custom managers extend `RoundRobinGroupChatManager` and override
`ShouldTerminateAsync(IReadOnlyList<ChatMessage> history, CancellationToken)`; the manager also has
`UpdateHistory`, `Reset`, `OnCheckpoint`, `OnCheckpointRestored`. Agents do not share a session; the
orchestrator broadcasts each response to every participant (star topology). Run with
`InProcessExecution.RunStreamingAsync(workflow, messages)` then `run.TrySendMessageAsync(new TurnToken(emitEvents: true))`;
events are `AgentResponseUpdateEvent` and a final `WorkflowOutputEvent` carrying `List<ChatMessage>`.
Participants are fixed at build time; a different roster is a new workflow. This repo already drives
`RoundRobinGroupChatManager.SelectNextAgentAsync(history, ct)` through a subclass (R6.3), which pins the
protected selection signature.

Source: https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/group-chat.

### R4.2 Magentic (planner-led team) matches a refactoring swarm

`new MagenticWorkflowBuilder(managerAgent).AddParticipants([...]).RequirePlanSignoff(true).WithMaxRounds(10)
.WithMaxStalls(3).WithMaxResets(2).Build()`. The manager agent writes a plan, keeps a progress ledger
(request satisfied, in loop, progress being made, next speaker), detects stalls and replans.
`RequirePlanSignoff(true)` pauses with a `RequestInfoEvent` carrying `MagenticPlanReviewRequest`; the owner
answers `Approve()` or `Revise(feedback)` and the run resumes from a checkpoint
(`ExecutionEnvironment.InProcess_Lockstep.ToWorkflowExecutionEnvironment().WithCheckpointing(...)`,
`ResumeStreamingAsync`). Experimental (`MAAIW001`).

Source: https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/magentic.

### R4.3 Building blocks the design uses

- `ChatClientAgent(IChatClient, instructions, name, description)` builds an agent from any
  `Microsoft.Extensions.AI` client, so each participant can carry a different model.
- `agent.AsAIFunction()` exposes an agent as another agent's tool; `workflow.AsAIAgent(...)` exposes a
  workflow as one agent.
- `BindAsExecutor` plus `AddFanOutEdge` / `AddFanInBarrierEdge` gives parallel fan-out to per-file agents and
  a barrier aggregator receiving `List<ChatMessage>`.
- Human in the loop: `RequestPort`, `RequestInfoEvent`, `run.SendResponseAsync`.
- Checkpoints: `CheckpointManager.CreateInMemory()`, `CheckpointManager.CreateJson(new FileSystemJsonCheckpointStore(dir))`,
  custom stores derive from `JsonCheckpointStore`.

Sources: agent-framework pages "agents/tools", "workflows/as-agents", "workflows/human-in-the-loop",
"workflows/declarative"; `dotnet/samples/03-workflows/Concurrent/Program.cs`.

## R5. Rebuilding the running product: Aspire, Orleans, YARP, Windows

### R5.1 What Aspire 13.5.3 offers (repo and CLI are both 13.5.3)

- CLI: `aspire resource <name> start|stop|restart|rebuild`. Watch mode
  (`aspire config set features.defaultWatchEnabled true`) restarts the AppHost topology on AppHost or project
  changes. The docs separate "AppHost watch" from "resource hot reload": a C# project resource is restarted,
  not hot-patched.
- Project resources carry a hidden `ProjectRebuilderResource` and a `rebuild` command (microsoft/aspire
  `docs/plans/project-v2-csharpprogram-watch.md`, reuse map).
- Aspire MCP server (`aspire agent mcp`, stdio): `list_resources`, `list_console_logs`,
  `list_structured_logs`, `list_traces`, `list_trace_structured_logs`, `execute_resource_command`
  (start, stop, restart; its schema is `{resourceName, commandName, arguments?}`, so any command name),
  `list_apphosts`, `select_apphost`, `list_integrations`, `get_integration_docs`, `list_docs`,
  `search_docs`, `get_doc`, `doctor`, `refresh_tools`. It auto-detects running AppHosts under the working
  directory. No AppHost was running during this research, so the live command list of the kernel resource
  was not observed.
- Custom commands: `WithCommand(name, displayName, Func<ExecuteCommandContext, Task<ExecuteCommandResult>>, CommandOptions)`
  shows in the dashboard and is callable by name through the MCP tool.
- `AddProject(name, projectPath)` accepts a path string; `WithExplicitStart()` keeps a resource stopped until
  asked; `WithReplicas(n)` starts an automatic reverse proxy but every replica runs the same build.
- `Aspire.Hosting.Yarp` 13.5.3 exists; its configuration is fixed at start.

Sources: https://aspire.dev/app-host/hot-reload-and-watch/, https://aspire.dev/get-started/aspire-mcp-server/,
microsoft/aspire `ResourceBuilderExtensions.cs`, `ProjectResourceBuilderExtensions.cs`, `Aspire.Hosting.Dotnet/README.md`.

### R5.2 The repo already talks to Aspire

`src/Modules/Microsoft/Aspire.Hosting/MicrosoftHostingExtensions.cs:9-19` projects the AppHost path as
`DigitalBrain__Microsoft__Aspire__ProjectPath`; `MicrosoftModule.cs:14-30` builds an `AspireConnection` and
registers the native tool `aspire_read`; `AspireConnection.ReadAsync` spawns `aspire agent mcp
--non-interactive --log-level Error` over stdio, binds the AppHost with `list_apphosts` and
`select_apphost`, and **allows only the five read tools** (`AspireConnection.cs:19-22`), with a 30 s timeout
and a 128 KB budget. A restart capability is a deliberate widening of that allowlist, not a new client.

### R5.3 Two kernels, one storage: Orleans facts

- `ClusterOptions.ServiceId` names the logical service and scopes persistence (grain state, reminders);
  `ClusterId` names a membership group. "Multiple clusters can share a ServiceId. This enables blue/green
  deployment scenarios where you start a new deployment (cluster) before shutting down another."
  (Microsoft Learn, Orleans server configuration.)
- The AppHost already does this per run: `Orleans__ClusterId = digitalbrain-{guid}` with a stable
  `ServiceId`, so grain and reminder state survive while membership is fresh
  (`src/Aspire/DigitalBrain.AppHost/AppHost.cs:79-84` and the comment above it). Recorded trap: a dashboard
  restart within one run reuses the ClusterId and the new silo stalls on the dead membership row
  (memory `clickhouse-module-2026-09-12`). A slot start therefore needs a fresh ClusterId every time; the
  silo can mint one when the environment leaves it empty.
- Grain interface versioning (`[Version(n)]`, `GrainVersioningOptions.DefaultCompatibilityStrategy =
  BackwardCompatible`, `AllCompatibleVersions`) supports two code versions in **one** cluster, but state
  versioning is out of scope and both silos would share activations. Two clusters is the safe shape for a
  process whose code changes freely.

Sources: https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-versioning/grain-versioning,
https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/server-configuration.

### R5.4 Windows build facts

`dotnet build` cannot overwrite an assembly a running process has loaded on Windows, so two slots of the
same project must build into different output roots. The SDK's `ArtifactsPath` property
(`-p:ArtifactsPath=...`) relocates `bin` and `obj` (including `project.assets.json`) under one directory per
slot. A git worktree per slot is the heavier alternative that also isolates the source tree.

### R5.5 Traffic switch

YARP (`Yarp.ReverseProxy` 2.3.0 on nuget.org) loads configuration from code with
`AddReverseProxy().LoadFromMemory(routes, clusters)`; configuration is a snapshot, updates are atomic for
subsequent requests and in-flight requests finish on their snapshot. A proxy middleware can call
`IProxyStateLookup.TryGetCluster(name)` and `context.ReassignProxyRequest(cluster)` per request, which is the
simplest "active slot" switch (dotnet/yarp `testassets/ReverseProxy.Code/Program.cs`,
`samples/ReverseProxy.Code.Sample/README.md`). YARP proxies SSE and WebSockets, which the shell's `/agent`
stream and the MCP streamable-HTTP endpoint need.

## R6. This repository (`E:\intochat\digitalbrain`, master at 04944b2d)

### R6.1 Versions and layout

- SDK `11.0.100-rc.1`, `net11.0`, `TreatWarningsAsErrors`, `AnalysisLevel preview-all`,
  `EnforceCodeStyleInBuild` (`global.json`, `Directory.Build.props:3-9`). Orleans 10.3.1 (Journaling
  10.3.1-alpha.1), Aspire.Hosting 13.5.3, `ModelContextProtocol.*` 2.2.0, `Microsoft.Extensions.AI` 10.9.0,
  `Microsoft.Agents.AI` 1.20.0, `Microsoft.Agents.AI.Workflows` 1.20.0,
  `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.20.0-preview. **No `Microsoft.CodeAnalysis*` package.**
- Modules: AI, ClickHouse, Excel, Google, Memory, Microsoft, Salesforce, Time, UI. Each is `Contracts`,
  implementation, and an `Aspire.Hosting` projection project, listed under `/Modules/<Name>/` in
  `DigitalBrain.slnx`. The silo loads a module by `DigitalBrain__Modules__N = "FullName, Assembly"` with
  `Type.GetType`, so `src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj` must reference the module project
  (`docs/clickhouse/NOTES.md:24-26`). The test project references every module too.
- There is no `docs/ARCHITECTURE.md` on master; `CONTEXT.md` carries the vocabulary and is partly stale
  (`IDigitalBrain`, `IBehavior`, `SubscribeTo` no longer exist). The de-facto module-authoring contract is
  `docs/clickhouse/README.md` plus `docs/ui/ui-layer-design.md`.
- Tools on this machine: `aspire` 13.5.3, `codex-cli` 0.154.0, `dotnet` 11.0.100-rc.1, Flutter 3.47.2.

### R6.2 The neuron contract a new module must satisfy

- `IModule.Configure(ISiloBuilder)` is the whole module interface (`src/Kernel/DigitalBrain/IModule.cs`);
  a module needs a public parameterless constructor.
- Contract interface: `[Alias("timer")] public interface ITimer : INeuron` with `[Alias("schedule")]`
  commands returning `Task<Accepted<T>>` and `[ReadOnly] [Alias("read")]` queries
  (`src/Modules/Time/Contracts/ITimer.cs`). `DescriptorRules.Validate` (`src/Kernel/DigitalBrain/Descriptors/DescriptorRules.cs:83-122`)
  enforces at silo start: non-empty interface alias, unique method aliases, `Task`/`Task<T>` returns,
  at most one reference-type DTO plus an optional trailing `CancellationToken`, mutators take exactly one
  DTO deriving from `Command(CommandId Id, long? ExpectedVersion)`, DTO members are section-7 types,
  string enums, `IReadOnlyList<T>`, or DTOs from the interface's or the kernel Contracts assembly.
- Neuron class: `[GrainType("timer")] internal sealed class TimerNeuron(NeuronRuntime runtime,
  [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TimerState>> state)
  : Neuron<TimerState>(runtime, state), ITimer`. A command body is
  `ExecuteCommandAsync(Descriptor("schedule"), command, TimeJson.Default.ScheduleTimer,
  TimeJson.Default.AcceptedTimerGeneration, arguments => { validate; var work = Schedule(Signal.FromJson(...));
  return new Accepted<T>(receipt, work); })`; the reaction is `ReceiveAsync(SignalDelivery, CancellationToken)`
  switching on `delivery.Signal.Type`, reading `Body(delivery, json)`, calling `Announce(signal, to, correlation)`
  and `SaveAsync(state)` (`src/Modules/Time/Time/TimerNeuron.cs`, `src/Kernel/DigitalBrain/Neuron/NeuronOfState.cs`).
  `SaveAsync` and `Announce` throw when called from a command: state changes happen in reactions.
- JSON: one `JsonSerializerContext` per module with `[assembly: NeuronJsonContext(typeof(TimeJson))]`,
  camel-case, string enums, every DTO and every `Accepted<T>` listed (`src/Modules/Time/Contracts/TimeJson.cs`).
  Orleans records carry `[GenerateSerializer]`, `[Alias("time.schedule-timer")]` and `[property: Id(n)]`.
- Native tools: `builder.Services.AddNativeTool("clickhouse_query", services => ...)` returning an
  `AIFunction` (`src/Modules/AI/Contracts/NativeToolServiceCollectionExtensions.cs`); results are screened
  as untrusted content. Plain singleton services (`TableService`) are the established home for logic that
  tools and tests call without a grain hop.
- Reference kernel neurons: `Neuron` (op log), `Neuron<TState>` (snapshot), `PlainNeuron`. Reactions are
  serialized turns; the AI module's `TurnBoundFunction` hops a tool call back onto the grain scheduler
  (`src/Modules/AI/AI/Tools/TurnBoundFunction.cs`).

### R6.3 Chat, agents and models already present

- `chat` (`src/Modules/AI/AI/ChatNeuron.cs`) is a group chat: `Instruct { participants, manager, rounds }`
  wires `Turn` synapses idempotently (`WireAsync`, line 76), and each turn rebuilds a MAF manager and replays
  one selection per spoken turn (`SelectAsync`, lines 166-183) over the transcript read from its own incoming
  journal (`TranscriptAsync`, line 188). The subclass `ReplayableRoundRobin : RoundRobinGroupChatManager`
  exposes `SelectNextAgentAsync(history, ct)` (line 212). A model-driven manager is a localized change there.
- `agent` (`src/Modules/AI/AI/AgentNeuron.cs`) is a MAF `ChatClientAgent` configured by
  `Instruct { provider, model, system, tools }` (`AIVocabulary.cs:29-38`); its tools are the seven brain
  operations, typed neuron functions from the descriptor table, and named native tools, each wrapped in
  `TurnBoundFunction` (lines 67-71). Sessions persist per correlation (lines 252-273).
- Models (`src/Modules/AI/Contracts/LLM/LLMModel.cs:16-34`): OpenAI Gpt56Sol/Terra/Luna, Gpt54/Mini/Nano;
  Anthropic Fable5, Opus5, Sonnet5, Haiku45; Google Gemini31Pro, Gemini36Pro, Gemini36Flash; XAI Grok46;
  Ollama Gemma4, Qwen35. One keyed `IChatClient` per marker type (`AIClients.cs:33-38`), pipeline
  `UseFunctionInvocation` plus OpenTelemetry. `[Llm<TModel>]` is `internal` to the AI module
  (`Clients/LlmAttribute.cs:5-7`) and has no call site; agents resolve `Providers.Resolve(provider, model)`.
  The AppHost enables only `IGpt56Luna`; xAI, Anthropic, Google, Ollama lines are commented out.
- The workspace agent (`/agent`, AG-UI, `ConversationalAgentEndpoints.cs:10-37`) has a hard-coded tool
  allowlist at `src/Modules/AI/AI/ConversationalAgent.cs:89` (`salesforce_*`, `clickhouse_*`,
  `show_query_table`, `render_chart`). `render_chart` takes an optional `chatName`, returns `kind: "chart"`
  JSON, and the shell opens such results directly (`_servedByResult` in `workspace_app.dart:250-260`).
- The MCP endpoint `/mcp` exposes `fire, cancel, connect, disconnect, read, describe, call`
  (`src/Kernel/DigitalBrain.Mcp/BrainTools.cs`) **only when `DigitalBrain:Graph:Enabled` is true**
  (`Program.cs:36-44`); nothing sets it, so `/mcp` is off today. `.mcp.json` points `digitalbrain-mcp` at
  port 5000 while the kernel listens on 5080.

### R6.4 Graph and UI seams

- C#: `[Alias("ui.graph")] IGraph` with `Render(RenderGraph)` and `Read()`; `GraphState(Title, Nodes, Edges)`,
  `GraphNodeState(Id, Label, Kind = "leaf", Cluster)`, `GraphEdgeState(Id, SourceId, TargetId, Dotted)`;
  `GraphNodeKinds` has only `hub` and `leaf`. `GraphNeuron` saves the state and announces
  `GraphRendered { name, title }`; `GET /ui/graphs/{name}` reads it (`UiEndpoints.cs:18`). The native tool
  `show_graph` needs a `uichat` chat and is not in the workspace allowlist.
- Cards: `uichat`'s `ChatNeuron.ReceiveAsync` routes `ChartRendered|GraphRendered|ImageDescribed|SheetChanged|TableRendered`
  into `OfferAsync`, whose switch maps to `UiCardKinds` (chart, image, spreadsheet, graph, table); a new
  signal must be added in both places or it becomes a spreadsheet card (`docs/clickhouse/NOTES.md:19-22`).
- Dart: `graph_models.dart` has `GraphNodeKind { hub, leaf, entity, module }`, `GraphNode(id, label, kind,
  dimmed, cluster, position)`, `GraphEdge(decorated, dotted)`, `GraphPulse`; `UiGraph` is a hand-rolled
  depth-projected painter with drag-to-rotate, node and edge taps, highlight and pulse
  (`ui/lib/src/components/graph/ui_graph.dart`). No graph package is in any `pubspec.yaml`; `graphic` is
  charts, `markdraw` is the diagram editor. The shell opens `table, chart, diagram, brain, image, document`
  from a tool result (`workspace_chat_presentation.dart:141-147`) and edits `table, chart, diagram, image,
  brain` (`artifact_editors.dart:36-84`); **`graph` has no editor case and no core-client read route**, so a
  graph card is a dead end in the shell today.
- UI phase 1 (`docs/ui/ui-layer-design.md`, pending) renames `UiCardOffer` to `UiPartRef` and introduces
  `UiPart` records with a `graph` kind reserved for `IGraph` (line 216).

### R6.5 Memory, Aspire and tests

- `IMemory` (`src/Modules/Memory/Contracts/IMemory.cs`): `Remember`, `Forget`, `[ReadOnly] Recall(Recall
  query)` with namespace, tags and a limit up to 32; the owner is the neuron name (`MemoryNeuron.cs:61`);
  `digitalbrain.capabilities` is a reserved namespace; store is `InMemoryVectorMemoryStore` or
  `QdrantVectorMemoryStore`; embeddings come from the AI module's default generator, which the AppHost sets
  to `ITextEmbedding3Small`.
- AppHost (`src/Aspire/DigitalBrain.AppHost/AppHost.cs`): brain resource with Azurite, modules by
  `AddModule<T>(configure)`, kernel as a **project resource** on fixed port 5080 (`isProxied: false`) gated by
  `/health`. The only `WithCommand` in the repo is the Flutter shell's `hot-reload`
  (`ShellHostingExtensions.cs:151-173`), a ready template for kernel commands. Module hosting projections
  follow `DigitalBrainModuleProjection.Apply(builder)` writing `EnvironmentKeys.For(root, name)` variables
  (`MicrosoftHostingExtensions.cs:21-37`).
- Tests: one project `tests/DigitalBrain.Tests` (xunit.v3 4.0.0 on Microsoft.Testing.Platform plus Reqnroll
  3.3.4), two kinds: `.feature` scenarios and `*Facts.cs`. `BrainSimulation.StartAsync(new() { Modules =
  new([typeof(UIModule)]), ConfigureSilo, PersistenceDirectory, Configuration })` composes modules on an
  in-process cluster; `brain.Grains.GetGrain<ITable>(new NeuronId(type, name).ToGrainId())` reaches a neuron;
  `brain.SiloServices` reaches singletons. `ScriptedChatClient` (`Say | CallTool | Pause | TimeOut`) is keyed
  `"scripted"` and `typeof(IGrok46)` by `ScriptedAi.Configure`. Docker-gated facts use an environment flag and
  run with `dotnet test ... -- --filter-class <FullName>`. Local gate:
  `dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`.
  CI runs the same plus `dart format`, `dart analyze --fatal-infos`, `flutter analyze`, `flutter test`,
  `flutter build web` on Flutter 3.47.2.
- Git: only `master`; commit convention `<module>: ...`, one PR per phase; the working tree carries two
  modified generated Flutter plugin files unrelated to this work.

### R6.6 Flutter graph rendering options (pub.dev)

| Package | Version | Layouts | Nodes | Edges | Web | License |
|---|---|---|---|---|---|---|
| `graphview` | 1.5.1 | Buchheim tree, tidier tree, Fruchterman-Reingold, **Sugiyama (layered)**, balloon, circular, radial, mindmap; expand/collapse; pairs with `InteractiveViewer` | any widget | `Paint` per edge, arrows; no edge labels | yes | MIT |
| `flutter_force_directed_graph` | 1.0.8 | force-directed with a controller; add/remove live | any widget | `edgesBuilder` | yes | BSD-3 |
| `force_directed_graphview` | current | force-directed over `InteractiveViewer` | widgets | custom | yes | MIT |

A project DAG wants a layered layout; a reference neighborhood wants a force layout. `graphview` has both
in one dependency and its algorithms compute positions on a plain graph model, so they can feed the existing
`UiGraph` painter. Pretty is a painter question (edge bundling, halos, focus dimming) independent of layout.
