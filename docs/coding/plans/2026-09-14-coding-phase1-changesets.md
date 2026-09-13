# Coding phase 1: edits as transactions — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `changeset` neuron that turns proposed edits into one diagnosed Roslyn snapshot and commits only
a clean one to disk, five more workspace reads (skeleton, member, callers, implementations, derived), a file
watcher that folds external saves into the snapshot, a durable map cache, `dotnet` and `git` runners with
parsed results, and nine more `code_*` tools, so that "rename X to Y and run the tests" lands as a commit on
a `coding/<id>` branch with the suite green.

**Architecture:** `SolutionWorkspace` (phase 0) gains one generic gated runner `QueryAsync`, a `CommitAsync`
that applies a changed `Solution` through `TryApplyChanges` and writes the changed documents in order, and a
`FoldAsync` the watcher uses. A singleton `ChangeSetEditor` applies `EditRequest`s to a snapshot with
`DocumentEditor`, `SyntaxFactory`, `Renamer` and reflection-discovered `CodeFixProvider`s, diagnoses only the
changed projects and their dependents, and renders a line diff. The `changeset` neuron
(`Neuron<ChangeSetState>`) is the durable transaction: commands validate and schedule; reactions call the
editor and the workspace. `DotnetRunner` and `GitRunner` shell out through one `IProcessRunner` seam
(faked in facts, real in the kernel). Tools call services and grains directly, the way phase 0's do.

**Tech Stack:** .NET 11 rc1, Orleans 10.3.1 neurons, Roslyn 5.9.0 (`Microsoft.CodeAnalysis.CSharp.Workspaces`,
`Microsoft.CodeAnalysis.Workspaces.MSBuild`, new: `Microsoft.CodeAnalysis.CSharp.Features` 5.9.0 for code
fixes), Microsoft.Extensions.AI 10.9.0, xunit.v3, git and `dotnet` CLIs.

**Spec:** `docs/coding/coding-agent-design.md` section 9.1, with 4.2, 4.8, 4.9, section 2, D10. Phase 0
outcome and observations: `docs/coding/NOTES.md`, `docs/coding/README.md`.

## Global Constraints

- Branch `feature/coding-phase1-changesets` from `feature/coding-phase0-workspace`; commits prefixed `coding:`; one PR.
- `TreatWarningsAsErrors=true`, `AnalysisLevel=preview-all`, `EnforceCodeStyleInBuild=true`: every build
  warning-free. Services use `.ConfigureAwait(false)`; grain code uses `.ConfigureAwait(true)`.
- No `/// <summary>` comments; a short inline comment only where the reason is not visible in the code.
- Every contract DTO: `[GenerateSerializer]`, `[Alias("coding.<kebab-name>")]`, `[property: Id(n)]` contiguous
  from 0, and an entry in `CodingJson`. Every neuron method: `[Alias]`; queries `[ReadOnly]` with at most one
  DTO plus an optional trailing `CancellationToken`; mutators take exactly one DTO deriving from `Command`.
- Design section 2: a neuron never awaits another neuron's answer inside a reaction (calling a singleton
  service such as `SolutionWorkspace` or `ChangeSetEditor` inside a reaction is allowed); every list result
  is `{items, totalCount, truncated}`; symbols are addressed by documentation-comment id; a tool never throws
  at the model, it returns `{ advice }`.
- D10: snapshot transactions only. Nothing reaches disk except through `SolutionWorkspace.CommitAsync`
  (a diagnosed snapshot applied with `TryApplyChanges`, then written in order) or the watcher's fold of an
  external save.
- New files use CRLF. Stage by explicit path; never stage `docs/coding/STATUS.md`, `.superpowers/`, or the
  two pre-existing modified files `src/Modules/UI/Flutter/shell/windows/flutter/generated_plugin_registrant.cc`
  and `generated_plugins.cmake`.
- Local gate before every commit:
  `dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`
  (expect the 4 Docker-gated skips plus the coding gated skips). Run one class with
  `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.<Class>`.
- Never read or write under `C:\Users`; the local NuGet cache is not a documentation source; every package
  API in this plan was checked against current docs on 2026-09-14 (Roslyn 5.9.0 `Renamer.RenameSymbolAsync(Solution, ISymbol, SymbolRenameOptions, string, CancellationToken)`,
  `DocumentEditor.CreateAsync`, `SymbolFinder.FindCallersAsync/FindImplementationsAsync/FindDerivedClassesAsync`,
  `Solution.WithDocumentText`, `CodeFixContext(Document, Diagnostic, Action<CodeAction, ImmutableArray<Diagnostic>>, CancellationToken)`,
  `CodeAction.GetOperationsAsync`, `ApplyChangesOperation.ChangedSolution`).

## File structure

```
src/Modules/Coding/Contracts/CodingVocabulary.cs            + ChangeSetType, four changeset signal names, WorkspaceMapping
src/Modules/Coding/Contracts/ICodeWorkspace.cs              + skeleton, member, callers, implementations, derived
src/Modules/Coding/Contracts/IChangeSet.cs                  the changeset contract
src/Modules/Coding/Contracts/EditKind.cs, EditRequest.cs, ProposeEdit.cs, CheckChangeSet.cs, CommitChangeSet.cs,
    DiscardChangeSet.cs, ChangeSetReceipt.cs, ChangeSetStatus.cs, ChangeSetSnapshot.cs
src/Modules/Coding/Contracts/SkeletonQuery.cs, SkeletonMember.cs, Skeleton.cs, MemberQuery.cs, MemberSource.cs,
    CallersQuery.cs, CallerHit.cs, CallersResult.cs, ImplementationsQuery.cs, DerivedQuery.cs
src/Modules/Coding/Contracts/WorkspaceSnapshot.cs           + ReloadNeeded
src/Modules/Coding/Contracts/CodingJson.cs                  + every new type
src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj + Microsoft.CodeAnalysis.CSharp.Features
src/Modules/Coding/Coding/SolutionQueries.cs                + skeleton, member, callers, implementations, derived, ProjectDiagnosticsAsync, DocumentAt
src/Modules/Coding/Coding/SolutionWorkspace.cs              + QueryAsync, CommitAsync, FoldAsync, MarkReloadNeeded, Generation
src/Modules/Coding/Coding/WorkspaceStatus.cs                + ReloadNeeded
src/Modules/Coding/Coding/LineDiff.cs                       unified-style line diff of two texts
src/Modules/Coding/Coding/EditOutcome.cs                    result of applying edits to a snapshot
src/Modules/Coding/Coding/ChangeSetEditor.cs                edits on one snapshot, diagnostics, diff
src/Modules/Coding/Coding/CodeFixCatalog.cs                 reflection-discovered CodeFixProviders
src/Modules/Coding/Coding/ChangeSetState.cs, ChangeSetNeuron.cs
src/Modules/Coding/Coding/SolutionFileWatcher.cs            folds .cs saves, flags project-file changes
src/Modules/Coding/Coding/WorkspaceState.cs                 + LastMap
src/Modules/Coding/Coding/WorkspaceNeuron.cs                + five reads, WorkspaceMapping reaction, map from cache
src/Modules/Coding/Coding/IProcessRunner.cs, ProcessRunner.cs, ProcessResult.cs
src/Modules/Coding/Coding/DotnetRunner.cs, BuildOutcome.cs, TestOutcome.cs, TestFailure.cs
src/Modules/Coding/Coding/GitRunner.cs, GitCommitOutcome.cs
src/Modules/Coding/Coding/CodingNativeTools.cs              + nine tools
src/Modules/Coding/Coding/CodingModule.cs                   + registrations
src/Modules/AI/AI/ConversationalAgent.cs                    allowlist + one instructions paragraph
tests/DigitalBrain.Tests/Features/Coding/FixtureSolutions.cs   + IWelcome, Shouter, Unused; DiskFixture (files on disk, git repo)
tests/DigitalBrain.Tests/Features/Coding/FakeProcessRunner.cs
tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs, SolutionWorkspaceFacts.cs (extended)
tests/DigitalBrain.Tests/Features/Coding/WorkspaceReadFacts.cs
tests/DigitalBrain.Tests/Features/Coding/ChangeSetEditorFacts.cs
tests/DigitalBrain.Tests/Features/Coding/ChangeSetNeuronFacts.cs
tests/DigitalBrain.Tests/Features/Coding/SolutionFileWatcherFacts.cs
tests/DigitalBrain.Tests/Features/Coding/RunnerFacts.cs
tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs   + gated runner fact
tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs (extended)
tests/DigitalBrain.Tests/Features/Coding/CodingChatFacts.cs       scripted "rename X to Y"
docs/coding/README.md, docs/coding/NOTES.md, docs/coding/STATUS.md
```

The adhoc fixture changes in Task 2 shift two phase 0 expectations: `Opening_reports_projects_and_documents`
expects 6 documents (was 3) and `The_map_lists_projects_clusters_and_references` expects Beta with 4
documents (was 2). `Find_symbols_returns_the_type_with_a_documentation_id` (TotalCount 2 for "greet") and
`References_cross_the_project_boundary` (one reference) stay true by construction: the new types are named
`IWelcome`, `Shouter` and `Unused`, and only `Program.Run` calls `Greet`.

---

### Task 1: Contracts for the changeset and the new workspace reads

**Files:**
- Create: every new file under `src/Modules/Coding/Contracts/` listed in the file structure
- Modify: `src/Modules/Coding/Contracts/ICodeWorkspace.cs`, `CodingVocabulary.cs`, `WorkspaceSnapshot.cs`, `CodingJson.cs`,
  `src/Modules/Coding/Coding/WorkspaceNeuron.cs` and `SolutionWorkspace.cs` (five delegations and five stubs so the tree compiles)
- Test: `tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs` (the descriptor fact)

**Interfaces:**
- Produces every DTO below, exactly as named; later tasks use these names verbatim.
- Produces stubs on `SolutionWorkspace`: `SkeletonAsync(SkeletonQuery, CancellationToken)`, `MemberAsync(MemberQuery, CancellationToken)`,
  `CallersAsync(CallersQuery, CancellationToken)`, `ImplementationsAsync(ImplementationsQuery, CancellationToken)`,
  `DerivedAsync(DerivedQuery, CancellationToken)`, each throwing `NotSupportedException("phase 1 task 2")`; Task 2 replaces them.

- [ ] **Step 1: Extend the descriptor fact so it covers both contracts**

Replace `Every_contract_method_uses_section_7_types` in `CodeWorkspaceNeuronFacts.cs` with:

```csharp
    [Theory]
    [InlineData(typeof(ICodeWorkspace), 12)]
    [InlineData(typeof(IChangeSet), 5)]
    public void Every_contract_method_uses_section_7_types(Type contract, int expectedMethods)
    {
        var options = DescriptorTable.ContractOptions(CodingJson.Default);
        var methods = contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var argumentTypes = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .Select(parameter => parameter.ParameterType);
            var resultType = method.ReturnType.GetGenericArguments()[0];
            foreach (var type in argumentTypes.Append(resultType))
            {
                DescriptorRules.ValidateMemberTypes(method, options.GetTypeInfo(type));
            }
        }

        Assert.Equal(expectedMethods, methods.Length);
    }
```

- [ ] **Step 2: Run it to see the compile failure**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release`
Expected: CS0246 `IChangeSet` not found.

- [ ] **Step 3: Write the vocabulary and the two contracts**

```csharp
// src/Modules/Coding/Contracts/CodingVocabulary.cs
namespace DigitalBrain.Coding;

public static class CodingVocabulary
{
    public const string WorkspaceType = "workspace";
    public const string ChangeSetType = "changeset";

    // ---- work a command schedules for its own reaction ----
    public const string WorkspaceOpening = "WorkspaceOpening";
    public const string WorkspaceReloading = "WorkspaceReloading";
    public const string WorkspaceMapping = "WorkspaceMapping";
    public const string ChangeSetProposing = "ChangeSetProposing";
    public const string ChangeSetChecking = "ChangeSetChecking";
    public const string ChangeSetCommitting = "ChangeSetCommitting";
    public const string ChangeSetDiscarding = "ChangeSetDiscarding";
}
```

`ICodeWorkspace.cs` gains, after `Map`:

```csharp
    [ReadOnly]
    [Alias("skeleton")]
    Task<Skeleton> Skeleton(SkeletonQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("member")]
    Task<MemberSource> Member(MemberQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("callers")]
    Task<CallersResult> Callers(CallersQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("implementations")]
    Task<SymbolSearchResult> Implementations(ImplementationsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("derived")]
    Task<SymbolSearchResult> Derived(DerivedQuery query, CancellationToken cancellationToken = default);
```

```csharp
// src/Modules/Coding/Contracts/IChangeSet.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("changeset")]
public interface IChangeSet : INeuron
{
    [Alias("propose")]
    Task<Accepted<ChangeSetReceipt>> Propose(ProposeEdit command);

    [Alias("check")]
    Task<Accepted<ChangeSetReceipt>> Check(CheckChangeSet command);

    [Alias("commit")]
    Task<Accepted<ChangeSetReceipt>> Commit(CommitChangeSet command);

    [Alias("discard")]
    Task<Accepted<ChangeSetReceipt>> Discard(DiscardChangeSet command);

    [ReadOnly]
    [Alias("read")]
    Task<ChangeSetSnapshot> Read();
}
```

- [ ] **Step 4: Write the changeset DTOs (one record per file)**

```csharp
// EditKind.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.edit-kind")]
public enum EditKind
{
    ReplaceMember = 0,
    InsertMember = 1,
    AddUsing = 2,
    ReplaceRange = 3,
    Rename = 4,
    ApplyCodeFix = 5,
}
```

```csharp
// EditRequest.cs
namespace DigitalBrain.Coding;

// One edit against the current snapshot. Each kind reads the fields it needs:
// ReplaceMember: SymbolId, Source. InsertMember: SymbolId (a type, or the member to insert after), Source.
// AddUsing: Path, Namespace. ReplaceRange: Path, StartLine, EndLine, Source. Rename: SymbolId, NewName.
// ApplyCodeFix: Path, DiagnosticId, optional StartLine and FixTitle.
[GenerateSerializer]
[Alias("coding.edit-request")]
public sealed record EditRequest(
    [property: Id(0)] EditKind Kind,
    [property: Id(1)] string? SymbolId = null,
    [property: Id(2)] string? Path = null,
    [property: Id(3)] string? Source = null,
    [property: Id(4)] string? NewName = null,
    [property: Id(5)] int? StartLine = null,
    [property: Id(6)] int? EndLine = null,
    [property: Id(7)] string? Namespace = null,
    [property: Id(8)] string? DiagnosticId = null,
    [property: Id(9)] string? FixTitle = null);
```

```csharp
// ProposeEdit.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.propose-edit")]
public sealed record ProposeEdit(
    CommandId Id,
    [property: Id(0)] EditRequest Edit,
    long? ExpectedVersion = null) : Command(Id, ExpectedVersion);
```

```csharp
// CheckChangeSet.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.check-change-set")]
public sealed record CheckChangeSet(CommandId Id) : Command(Id);
```

```csharp
// CommitChangeSet.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.commit-change-set")]
public sealed record CommitChangeSet(CommandId Id, [property: Id(0)] string Message) : Command(Id);
```

```csharp
// DiscardChangeSet.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.discard-change-set")]
public sealed record DiscardChangeSet(CommandId Id) : Command(Id);
```

```csharp
// ChangeSetReceipt.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.change-set-receipt")]
public sealed record ChangeSetReceipt(
    [property: Id(0)] string ChangeId,
    [property: Id(1)] int EditCount,
    [property: Id(2)] ChangeSetStatus Status);
```

```csharp
// ChangeSetStatus.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.change-set-status")]
public enum ChangeSetStatus
{
    Draft = 0,
    Checked = 1,
    Committed = 2,
    Discarded = 3,
}
```

```csharp
// ChangeSetSnapshot.cs
namespace DigitalBrain.Coding;

// Detail names the edit a check or commit refused on ("edit 2 (ReplaceMember M:...) left 1 error: ...").
[GenerateSerializer]
[Alias("coding.change-set-snapshot")]
public sealed record ChangeSetSnapshot(
    [property: Id(0)] ChangeSetStatus Status,
    [property: Id(1)] IReadOnlyList<EditRequest> Edits,
    [property: Id(2)] IReadOnlyList<DiagnosticHit> Diagnostics,
    [property: Id(3)] string? Diff,
    [property: Id(4)] long Generation,
    [property: Id(5)] string? Detail,
    [property: Id(6)] IReadOnlyList<string> Files);
```

- [ ] **Step 5: Write the read DTOs**

```csharp
// SkeletonQuery.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.skeleton-query")]
public sealed record SkeletonQuery([property: Id(0)] string Path);
```

```csharp
// SkeletonMember.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.skeleton-member")]
public sealed record SkeletonMember(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Signature,
    [property: Id(3)] int Line,
    [property: Id(4)] int Depth);
```

```csharp
// Skeleton.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.skeleton")]
public sealed record Skeleton(
    [property: Id(0)] string Path,
    [property: Id(1)] string Project,
    [property: Id(2)] IReadOnlyList<SkeletonMember> Members);
```

```csharp
// MemberQuery.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.member-query")]
public sealed record MemberQuery([property: Id(0)] string SymbolId);
```

```csharp
// MemberSource.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.member-source")]
public sealed record MemberSource(
    [property: Id(0)] string Id,
    [property: Id(1)] string Path,
    [property: Id(2)] int StartLine,
    [property: Id(3)] int EndLine,
    [property: Id(4)] string Source);
```

```csharp
// CallersQuery.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.callers-query")]
public sealed record CallersQuery([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);
```

```csharp
// CallerHit.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.caller-hit")]
public sealed record CallerHit(
    [property: Id(0)] string Id,
    [property: Id(1)] string Display,
    [property: Id(2)] string Path,
    [property: Id(3)] int Line,
    [property: Id(4)] string Project);
```

```csharp
// CallersResult.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.callers-result")]
public sealed record CallersResult(
    [property: Id(0)] string SymbolId,
    [property: Id(1)] IReadOnlyList<CallerHit> Items,
    [property: Id(2)] int TotalCount,
    [property: Id(3)] bool Truncated);
```

```csharp
// ImplementationsQuery.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.implementations-query")]
public sealed record ImplementationsQuery([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);
```

```csharp
// DerivedQuery.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.derived-query")]
public sealed record DerivedQuery([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);
```

`WorkspaceSnapshot.cs` gains a seventh member, the last positional parameter: `[property: Id(6)] bool ReloadNeeded = false`.

`CodingJson.cs` gains one `[JsonSerializable(typeof(X))]` line for each of: `EditKind`, `EditRequest`, `ProposeEdit`,
`CheckChangeSet`, `CommitChangeSet`, `DiscardChangeSet`, `ChangeSetReceipt`, `Accepted<ChangeSetReceipt>`,
`ChangeSetStatus`, `ChangeSetSnapshot`, `SkeletonQuery`, `SkeletonMember`, `Skeleton`, `MemberQuery`, `MemberSource`,
`CallersQuery`, `CallerHit`, `CallersResult`, `ImplementationsQuery`, `DerivedQuery`.

- [ ] **Step 6: Keep the tree compiling**

`WorkspaceNeuron` must implement the five new interface members. Add the delegations (they are final; Task 2
only fills in the service side):

```csharp
    [ReadOnly]
    public Task<Skeleton> Skeleton(SkeletonQuery query, CancellationToken cancellationToken = default)
        => workspace.SkeletonAsync(query, cancellationToken);

    [ReadOnly]
    public Task<MemberSource> Member(MemberQuery query, CancellationToken cancellationToken = default)
        => workspace.MemberAsync(query, cancellationToken);

    [ReadOnly]
    public Task<CallersResult> Callers(CallersQuery query, CancellationToken cancellationToken = default)
        => workspace.CallersAsync(query, cancellationToken);

    [ReadOnly]
    public Task<SymbolSearchResult> Implementations(ImplementationsQuery query, CancellationToken cancellationToken = default)
        => workspace.ImplementationsAsync(query, cancellationToken);

    [ReadOnly]
    public Task<SymbolSearchResult> Derived(DerivedQuery query, CancellationToken cancellationToken = default)
        => workspace.DerivedAsync(query, cancellationToken);
```

and in `SolutionWorkspace` the five stubs, each `=> throw new NotSupportedException("phase 1 task 2");` with the
signatures listed under Interfaces. `WorkspaceNeuron.Read()` passes `live.ReloadNeeded` … no: `WorkspaceStatus`
gains `ReloadNeeded` only in Task 6, so `Read()` passes `ReloadNeeded: false` explicitly until then.

- [ ] **Step 7: Build and run the descriptor fact**

Run: `dotnet build DigitalBrain.slnx -c Release` then
`dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodeWorkspaceNeuronFacts`
Expected: 0 warnings; the theory passes for both contracts (12 and 5 methods); the other facts still pass.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs
git commit -m "coding: changeset contract and the phase 1 read queries"
```

---

### Task 2: Skeleton, member, callers, implementations and derived reads

**Files:**
- Modify: `tests/DigitalBrain.Tests/Features/Coding/FixtureSolutions.cs`, `SolutionWorkspaceFacts.cs` (two counts),
  `src/Modules/Coding/Coding/SolutionQueries.cs`, `SolutionWorkspace.cs`
- Create: `tests/DigitalBrain.Tests/Features/Coding/WorkspaceReadFacts.cs`

**Interfaces:**
- Consumes: the Task 1 DTOs and stubs.
- Produces: `SolutionWorkspace.QueryAsync<T>(Func<Solution, CancellationToken, Task<T>> query, CancellationToken)`
  (the one gated runner every read uses), the five reads implemented, `SolutionQueries.DocumentAt(Solution, string)`,
  `SolutionQueries.DeclarationAsync(ISymbol, CancellationToken)`; `FixtureSolutions.Documents(string root)` and
  `FixtureSolutions.Build(string root)`.

- [ ] **Step 1: Grow the fixture**

In `FixtureSolutions.cs`, replace `TwoProjects()` and add the sources as constants so Task 3 can put the same
files on disk:

```csharp
    internal const string WelcomePath = Root + "/Alpha/IWelcome.cs";
    internal const string ShouterPath = Root + "/Beta/Shouter.cs";
    internal const string UnusedPath = Root + "/Beta/Unused.cs";

    internal const string GreeterSource = """
        namespace Alpha;

        public class Greeter : IWelcome
        {
            public string Greet(string name) => $"Hello, {name}";

            public string Welcome(string name) => "Welcome, " + name;
        }
        """;

    internal const string WelcomeSource = """
        namespace Alpha;

        public interface IWelcome
        {
            string Welcome(string name);
        }
        """;

    internal const string ProgramSource = """
        using Alpha;

        namespace Beta;

        public static class Program
        {
            public static string Run() => new Greeter().Greet("world");
        }
        """;

    internal const string BrokenSource = """
        namespace Beta;

        public static class Broken
        {
            public static int Count() => "not a number";
        }
        """;

    internal const string ShouterSource = """
        using Alpha;

        namespace Beta;

        public sealed class Shouter : Greeter
        {
            public string Shout(string name) => name.ToUpperInvariant();
        }
        """;

    internal const string UnusedSource = """
        namespace Beta;

        public static class Unused
        {
            public static void Run()
            {
                int count = 1;
            }
        }
        """;

    internal static IReadOnlyList<(string Path, string Source)> Documents(string root) =>
    [
        (root + "/Alpha/Greeter.cs", GreeterSource),
        (root + "/Alpha/IWelcome.cs", WelcomeSource),
        (root + "/Beta/Program.cs", ProgramSource),
        (root + "/Beta/Broken.cs", BrokenSource),
        (root + "/Beta/Shouter.cs", ShouterSource),
        (root + "/Beta/Unused.cs", UnusedSource),
    ];

    internal static Workspace TwoProjects() => Build(Root);

    internal static Workspace Build(string root)
    {
        var workspace = new AdhocWorkspace();
        var alpha = ProjectId.CreateNewId("Alpha");
        var beta = ProjectId.CreateNewId("Beta");
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(alpha, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp,
                filePath: root + "/Alpha/Alpha.csproj", compilationOptions: options, metadataReferences: Runtime))
            .AddProject(ProjectInfo.Create(beta, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp,
                filePath: root + "/Beta/Beta.csproj", compilationOptions: options, metadataReferences: Runtime,
                projectReferences: [new ProjectReference(alpha)]));
        foreach (var (path, source) in Documents(root))
        {
            var project = path.Contains("/Alpha/", StringComparison.Ordinal) ? alpha : beta;
            solution = solution.AddDocument(DocumentId.CreateNewId(project), System.IO.Path.GetFileName(path), SourceText.From(source), filePath: path);
        }

        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("The adhoc fixture did not apply.");
        }

        return workspace;
    }
```

Keep `Root`, `GreeterPath`, `ProgramPath`, `BrokenPath`, `Runtime` and `ConsoleWithoutMain()` as they are. Update
two phase 0 expectations in `SolutionWorkspaceFacts.cs`: `Opening_reports_projects_and_documents` asserts
`6` documents; `The_map_lists_projects_clusters_and_references` asserts Beta has `4` documents.

- [ ] **Step 2: Write the failing read facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/WorkspaceReadFacts.cs
using DigitalBrain.Coding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class WorkspaceReadFacts
{
    private static async Task<SolutionWorkspace> ReadyAsync()
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return workspace;
    }

    [Fact]
    public async Task A_skeleton_lists_types_and_member_signatures_without_bodies()
    {
        using var workspace = await ReadyAsync();
        var skeleton = await workspace.SkeletonAsync(new(FixtureSolutions.GreeterPath), TestContext.Current.CancellationToken);
        Assert.Equal("Alpha", skeleton.Project);
        Assert.Equal(["T:Alpha.Greeter", "M:Alpha.Greeter.Greet(System.String)", "M:Alpha.Greeter.Welcome(System.String)"], skeleton.Members.Select(member => member.Id));
        Assert.Equal("public class Greeter : IWelcome", skeleton.Members[0].Signature);
        Assert.Equal("public string Greet(string name)", skeleton.Members[1].Signature);
        Assert.Equal([0, 1, 1], skeleton.Members.Select(member => member.Depth));
        Assert.Equal(5, skeleton.Members[1].Line);
    }

    [Fact]
    public async Task A_member_returns_its_declaration_with_the_body()
    {
        using var workspace = await ReadyAsync();
        var member = await workspace.MemberAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        Assert.Equal(FixtureSolutions.GreeterPath, member.Path);
        Assert.Equal(5, member.StartLine);
        Assert.Equal(5, member.EndLine);
        Assert.Equal("""public string Greet(string name) => $"Hello, {name}";""", member.Source);
    }

    [Fact]
    public async Task Callers_name_the_calling_symbol_and_the_call_site()
    {
        using var workspace = await ReadyAsync();
        var callers = await workspace.CallersAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(callers.Items);
        Assert.Equal("M:Beta.Program.Run", hit.Id);
        Assert.Equal(FixtureSolutions.ProgramPath, hit.Path);
        Assert.Equal(7, hit.Line);
        Assert.Equal("Beta", hit.Project);
        Assert.Equal(1, callers.TotalCount);
    }

    [Fact]
    public async Task Implementations_of_an_interface_member_are_the_implementing_members()
    {
        using var workspace = await ReadyAsync();
        var implementations = await workspace.ImplementationsAsync(new("M:Alpha.IWelcome.Welcome(System.String)"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(implementations.Items);
        Assert.Equal("M:Alpha.Greeter.Welcome(System.String)", hit.Id);
    }

    [Fact]
    public async Task Derived_types_cross_the_project_boundary()
    {
        using var workspace = await ReadyAsync();
        var derived = await workspace.DerivedAsync(new("T:Alpha.Greeter"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(derived.Items);
        Assert.Equal("T:Beta.Shouter", hit.Id);
        Assert.Equal("Beta", hit.Project);
    }

    [Fact]
    public async Task Derived_of_an_interface_are_its_implementing_types()
    {
        using var workspace = await ReadyAsync();
        var derived = await workspace.DerivedAsync(new("T:Alpha.IWelcome"), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Alpha.Greeter", "T:Beta.Shouter"], derived.Items.Select(hit => hit.Id));
    }

    [Fact]
    public async Task An_unknown_path_is_advice()
    {
        using var workspace = await ReadyAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SkeletonAsync(new("E:/fixture/Nowhere.cs"), TestContext.Current.CancellationToken));
        Assert.Contains("find-symbols", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.WorkspaceReadFacts`
Expected: every fact fails with `NotSupportedException: phase 1 task 2`.

- [ ] **Step 4: Add the generic gated runner to `SolutionWorkspace`**

Replace the four one-line query methods and the five stubs with one runner plus nine delegations:

```csharp
    public async Task<T> QueryAsync<T>(Func<Solution, CancellationToken, Task<T>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await query(lease.Solution, cancellationToken).ConfigureAwait(false);
    }

    public Task<SymbolSearchResult> FindSymbolsAsync(SymbolSearch query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.FindSymbolsAsync(solution, query, token), cancellationToken);

    public Task<ReferenceSearchResult> ReferencesAsync(ReferenceSearch query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.ReferencesAsync(solution, query, token), cancellationToken);

    public Task<DiagnosticsResult> DiagnosticsAsync(DiagnosticsQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.DiagnosticsAsync(solution, query, token), cancellationToken);

    public Task<SolutionMap> MapAsync(MapQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.MapAsync(solution, solution.FilePath ?? string.Empty, query, token), cancellationToken);

    public Task<Skeleton> SkeletonAsync(SkeletonQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.SkeletonAsync(solution, query, token), cancellationToken);

    public Task<MemberSource> MemberAsync(MemberQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.MemberAsync(solution, query, token), cancellationToken);

    public Task<CallersResult> CallersAsync(CallersQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.CallersAsync(solution, query, token), cancellationToken);

    public Task<SymbolSearchResult> ImplementationsAsync(ImplementationsQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.ImplementationsAsync(solution, query, token), cancellationToken);

    public Task<SymbolSearchResult> DerivedAsync(DerivedQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.DerivedAsync(solution, query, token), cancellationToken);
```

`MapAsync` needs the solution path the lease carried; `Solution.FilePath` is null on the adhoc fixture, so
keep the path on the lease: give `QueryAsync` a second overload `QueryAsync<T>(Func<Solution, string, CancellationToken, Task<T>>, CancellationToken)`
that passes `lease.SolutionPath`, and make `MapAsync` use it. The `The_map_lists_projects_clusters_and_references`
fact (cluster "Alpha") pins that the path still reaches the map.

- [ ] **Step 5: Write the queries**

Add to `SolutionQueries.cs` (usings `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Syntax`, `Microsoft.CodeAnalysis.Text`):

```csharp
    internal static Document DocumentAt(Solution solution, string path)
    {
        var id = solution.GetDocumentIdsWithFilePath(path).FirstOrDefault()
            ?? throw new InvalidOperationException($"No document at '{path}' is in the solution. Use a path from find-symbols.");
        return solution.GetDocument(id)!;
    }

    internal static async Task<Skeleton> SkeletonAsync(Solution solution, SkeletonQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Path);
        var document = DocumentAt(solution, query.Path);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{query.Path}' has no syntax tree.");
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{query.Path}' has no semantic model.");
        var members = new List<SkeletonMember>();
        foreach (var node in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (node is BaseNamespaceDeclarationSyntax)
            {
                continue;
            }

            foreach (var symbol in DeclaredSymbols(model, node, cancellationToken))
            {
                if (symbol.GetDocumentationCommentId() is { } id)
                {
                    var depth = node.Ancestors().Count(static ancestor => ancestor is TypeDeclarationSyntax);
                    members.Add(new SkeletonMember(id, symbol.Kind.ToString(), Signature(node), node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, depth));
                }
            }
        }

        return new Skeleton(document.FilePath ?? document.Name, document.Project.Name, members);
    }

    internal static async Task<MemberSource> MemberAsync(Solution solution, MemberQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var node = await DeclarationAsync(symbol, cancellationToken).ConfigureAwait(false);
        var span = node.GetLocation().GetLineSpan();
        return new MemberSource(query.SymbolId, span.Path, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1, node.ToString());
    }

    internal static async Task<CallersResult> CallersAsync(Solution solution, CallersQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var limit = ClampLimit(query.Limit);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var callers = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
        var hits = callers
            .SelectMany(caller => caller.Locations
                .Where(static location => location.IsInSource)
                .Select(location => new CallerHit(caller.CallingSymbol.GetDocumentationCommentId() ?? caller.CallingSymbol.Name,
                    caller.CallingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    location.SourceTree!.FilePath, location.GetLineSpan().StartLinePosition.Line + 1,
                    solution.GetDocument(location.SourceTree)?.Project.Name ?? string.Empty)))
            .OrderBy(static hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Line)
            .ToArray();
        return new CallersResult(query.SymbolId, [.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    internal static async Task<SymbolSearchResult> ImplementationsAsync(Solution solution, ImplementationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, projects: null, cancellationToken).ConfigureAwait(false);
        return Envelope(solution, implementations, query.Limit);
    }

    internal static async Task<SymbolSearchResult> DerivedAsync(Solution solution, DerivedQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol type)
        {
            throw new InvalidOperationException($"'{query.SymbolId}' is not a type. Derived types are asked of a class or an interface id.");
        }

        IEnumerable<ISymbol> derived = type.TypeKind == TypeKind.Interface
            ? (await SymbolFinder.FindImplementationsAsync(type, solution, projects: null, cancellationToken).ConfigureAwait(false))
                .Concat(await SymbolFinder.FindDerivedInterfacesAsync(type, solution, transitive: true, projects: null, cancellationToken).ConfigureAwait(false))
            : await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: true, projects: null, cancellationToken).ConfigureAwait(false);
        return Envelope(solution, derived, query.Limit);
    }

    private static SymbolSearchResult Envelope(Solution solution, IEnumerable<ISymbol> symbols, int requestedLimit)
    {
        var limit = ClampLimit(requestedLimit);
        var hits = symbols.Select(symbol => Hit(solution, symbol)).OfType<SymbolHit>()
            .OrderBy(static hit => hit.Name, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Id, StringComparer.Ordinal)
            .ToArray();
        return new SymbolSearchResult([.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    private static IEnumerable<ISymbol> DeclaredSymbols(SemanticModel model, MemberDeclarationSyntax node, CancellationToken cancellationToken)
    {
        // Field and event declarations declare their variables, not themselves.
        if (node is BaseFieldDeclarationSyntax field)
        {
            return field.Declaration.Variables.Select(variable => model.GetDeclaredSymbol(variable, cancellationToken)).OfType<ISymbol>();
        }

        return model.GetDeclaredSymbol(node, cancellationToken) is { } symbol ? [symbol] : [];
    }

    // The declaration up to its body: "public string Greet(string name)", "public class Greeter : IWelcome".
    private static string Signature(MemberDeclarationSyntax node)
    {
        var end = node switch
        {
            TypeDeclarationSyntax type when type.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken) => type.OpenBraceToken.SpanStart,
            BaseMethodDeclarationSyntax method => method.Body?.SpanStart ?? method.ExpressionBody?.SpanStart ?? method.Span.End,
            PropertyDeclarationSyntax property => property.AccessorList?.SpanStart ?? property.ExpressionBody?.SpanStart ?? property.Span.End,
            IndexerDeclarationSyntax indexer => indexer.AccessorList?.SpanStart ?? indexer.ExpressionBody?.SpanStart ?? indexer.Span.End,
            EventDeclarationSyntax @event => @event.AccessorList?.SpanStart ?? @event.Span.End,
            _ => node.Span.End,
        };
        var text = node.SyntaxTree.GetText().ToString(TextSpan.FromBounds(node.SpanStart, end));
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';').Trim();
    }

    internal static async Task<SyntaxNode> DeclarationAsync(ISymbol symbol, CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault(static reference => reference.SyntaxTree.FilePath.Length > 0)
            ?? throw new InvalidOperationException($"'{symbol.GetDocumentationCommentId()}' has no source declaration.");
        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        // A field's declaring node is the variable; the member to show is the whole field declaration.
        return node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } ? field : node;
    }
```

- [ ] **Step 6: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.WorkspaceReadFacts`, then `SolutionWorkspaceFacts` (with the
two updated counts), `CodeWorkspaceNeuronFacts`, `CodingNativeToolFacts`.
Expected: all PASS. If the class signature ends with `{`, the open-brace guard did not match: use
`type.OpenBraceToken.Span.Length > 0`. If `Callers` returns zero, the fixture's `Program.Run` must still call `Greet`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: skeleton, member, callers, implementations and derived reads"
```

---

### Task 3: One snapshot, many edits — the editor core and the commit path

**Files:**
- Create: `src/Modules/Coding/Coding/LineDiff.cs`, `EditOutcome.cs`, `ChangeSetEditor.cs`, `CommitOutcome.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/DiskFixture.cs`, `tests/DigitalBrain.Tests/Features/Coding/ChangeSetEditorFacts.cs`
- Modify: `src/Modules/Coding/Coding/SolutionWorkspace.cs` (lease carries the workspace; `CommitAsync`; `Generation`),
  `SolutionQueries.cs` (`ProjectDiagnosticsAsync` shared with `DiagnosticsAsync`), `CodingModule.cs` (register the editor),
  `SolutionWorkspaceFacts.cs` (commit facts)

**Interfaces:**
- Consumes: `SolutionQueries.ResolveAsync`, `DeclarationAsync`, `DocumentAt`, `ClampLimit`, `Hit` (Tasks 0-2), `EditRequest`/`EditKind` (Task 1).
- Produces:
  - `public sealed record EditOutcome(Solution Changed, IReadOnlyList<DiagnosticHit> Diagnostics, string Diff, IReadOnlyList<string> ChangedPaths, int? FailingEdit, string? Detail)` with `bool HasErrors`.
  - `public sealed class ChangeSetEditor` with `Task<EditOutcome> ApplyAsync(Solution original, IReadOnlyList<EditRequest> edits, CancellationToken)`; kinds `Rename` and `ApplyCodeFix` throw `NotSupportedException("phase 1 task 4")` until Task 4.
  - `public sealed record CommitOutcome(IReadOnlyList<string> WrittenPaths, long Generation)`.
  - `SolutionWorkspace.CommitAsync(Func<Solution, CancellationToken, Task<Solution>> change, CancellationToken)` and `long Generation`.
  - `internal static class LineDiff { static string Render(string path, string before, string after) }`.
  - `SolutionQueries.ProjectDiagnosticsAsync(Solution, IReadOnlyCollection<ProjectId>, int limit, CancellationToken) -> DiagnosticsResult`.
  - Test helper `DiskFixture` (`Root`, `SolutionPath`, `GreeterPath`, `ProgramPath`, `UnusedPath`, `Open()`, `Create()`, `IDisposable`).

- [ ] **Step 1: Write the failing editor facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/ChangeSetEditorFacts.cs
using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class ChangeSetEditorFacts
{
    private static readonly ChangeSetEditor Editor = new();

    private static Solution Snapshot() => FixtureSolutions.TwoProjects().CurrentSolution;

    private static async Task<string> TextAsync(Solution solution, string path)
        => (await SolutionQueries.DocumentAt(solution, path).GetTextAsync(TestContext.Current.CancellationToken)).ToString();

    [Fact]
    public async Task Replacing_a_member_yields_a_clean_snapshot_and_a_diff()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.Null(outcome.FailingEdit);
        Assert.Equal([FixtureSolutions.GreeterPath], outcome.ChangedPaths);
        Assert.Contains("""-    public string Greet(string name) => $"Hello, {name}";""", outcome.Diff, StringComparison.Ordinal);
        Assert.Contains("""+    public string Greet(string name) => $"Hi, {name}";""", outcome.Diff, StringComparison.Ordinal);
        Assert.Contains("Hi, {name}", await TextAsync(outcome.Changed, FixtureSolutions.GreeterPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_that_does_not_compile_names_the_responsible_edit()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public string Greet(string name) => 42;")],
            TestContext.Current.CancellationToken);
        Assert.True(outcome.HasErrors);
        Assert.Equal(0, outcome.FailingEdit);
        Assert.StartsWith("edit 1 (ReplaceMember M:Alpha.Greeter.Greet(System.String))", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains(outcome.Diagnostics, hit => hit.Id == "CS0029" && hit.Path == FixtureSolutions.GreeterPath);
    }

    [Fact]
    public async Task Dependent_projects_are_diagnosed_too()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public string Greet(int count) => count.ToString();")],
            TestContext.Current.CancellationToken);
        Assert.True(outcome.HasErrors);
        Assert.Contains(outcome.Diagnostics, hit => hit.Path == FixtureSolutions.ProgramPath && hit.Severity == "Error");
        Assert.Equal(0, outcome.FailingEdit);
    }

    [Fact]
    public async Task Inserting_into_a_type_appends_a_member()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.InsertMember, SymbolId: "T:Alpha.Greeter", Source: "public int Count => 1;")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        var skeleton = await SolutionQueries.SkeletonAsync(outcome.Changed, new(FixtureSolutions.GreeterPath), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Alpha.Greeter", "M:Alpha.Greeter.Greet(System.String)", "M:Alpha.Greeter.Welcome(System.String)", "P:Alpha.Greeter.Count"], skeleton.Members.Select(member => member.Id));
    }

    [Fact]
    public async Task Inserting_after_a_member_keeps_the_order()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.InsertMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public int Count => 1;")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        var skeleton = await SolutionQueries.SkeletonAsync(outcome.Changed, new(FixtureSolutions.GreeterPath), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Alpha.Greeter", "M:Alpha.Greeter.Greet(System.String)", "P:Alpha.Greeter.Count", "M:Alpha.Greeter.Welcome(System.String)"], skeleton.Members.Select(member => member.Id));
    }

    [Fact]
    public async Task Adding_a_using_is_idempotent()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.AddUsing, Path: FixtureSolutions.GreeterPath, Namespace: "System.Text"),
             new EditRequest(EditKind.AddUsing, Path: FixtureSolutions.GreeterPath, Namespace: "System.Text")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        var text = await TextAsync(outcome.Changed, FixtureSolutions.GreeterPath);
        Assert.StartsWith("using System.Text;", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("using System.Text;").Length - 1);
    }

    [Fact]
    public async Task Replacing_a_line_range_is_a_text_edit()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceRange, Path: FixtureSolutions.ProgramPath, StartLine: 7, EndLine: 7,
                Source: """    public static string Run() => new Greeter().Welcome("world");""")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.Contains(""".Welcome("world")""", await TextAsync(outcome.Changed, FixtureSolutions.ProgramPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_symbol_fails_that_edit_with_advice()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.AddUsing, Path: FixtureSolutions.GreeterPath, Namespace: "System.Text"),
             new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Nowhere.Missing", Source: "public int X => 1;")],
            TestContext.Current.CancellationToken);
        Assert.True(outcome.HasErrors);
        Assert.Equal(1, outcome.FailingEdit);
        Assert.Contains("find-symbols", outcome.Detail, StringComparison.Ordinal);
        Assert.Empty(outcome.Diagnostics);
    }

    [Fact]
    public async Task A_source_that_is_not_a_member_is_refused()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "this is not C#")],
            TestContext.Current.CancellationToken);
        Assert.Equal(0, outcome.FailingEdit);
        Assert.Contains("member declaration", outcome.Detail, StringComparison.Ordinal);
    }
}
```

And the commit facts, appended to `SolutionWorkspaceFacts.cs`:

```csharp
    [Fact]
    public async Task Commit_applies_the_snapshot_and_writes_only_the_changed_files()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var programBefore = File.GetLastWriteTimeUtc(fixture.ProgramPath);
        var editor = new ChangeSetEditor();

        var outcome = await workspace.CommitAsync(async (solution, token) =>
        {
            var applied = await editor.ApplyAsync(solution,
                [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""")], token);
            return applied.Changed;
        }, TestContext.Current.CancellationToken);

        Assert.Equal([fixture.GreeterPath], outcome.WrittenPaths);
        Assert.Equal(1, outcome.Generation);
        Assert.Equal(1, workspace.Generation);
        Assert.Contains("Hi, {name}", await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(programBefore, File.GetLastWriteTimeUtc(fixture.ProgramPath));
        var member = await workspace.MemberAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        Assert.Contains("Hi, {name}", member.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commit_refuses_a_snapshot_the_workspace_has_moved_past()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var stale = await workspace.QueryAsync((solution, _) => Task.FromResult(solution), TestContext.Current.CancellationToken);
        await workspace.CommitAsync((solution, _) => Task.FromResult(solution.WithDocumentText(
            solution.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(), Microsoft.CodeAnalysis.Text.SourceText.From(FixtureSolutions.GreeterSource + "\n// touched\n"))), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CommitAsync((_, _) => Task.FromResult(stale.WithDocumentText(
            stale.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(), Microsoft.CodeAnalysis.Text.SourceText.From("namespace Alpha;"))), TestContext.Current.CancellationToken));
        Assert.Contains("Check it again", error.Message, StringComparison.Ordinal);
    }
```

with the fixture:

```csharp
// tests/DigitalBrain.Tests/Features/Coding/DiskFixture.cs
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Tests.Coding;

// The two-project fixture written to a temporary folder, so commits and the watcher touch real files.
internal sealed class DiskFixture : IDisposable
{
    private DiskFixture(string root) => Root = root;

    public string Root { get; }

    public string SolutionPath => Root + "/Fixture.slnx";

    public string GreeterPath => Root + "/Alpha/Greeter.cs";

    public string ProgramPath => Root + "/Beta/Program.cs";

    public string UnusedPath => Root + "/Beta/Unused.cs";

    public static DiskFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "digitalbrain-coding", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        foreach (var (path, source) in FixtureSolutions.Documents(root))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
        }

        File.WriteAllText(Path.Combine(root, "Fixture.slnx"), "<Solution />");
        return new DiskFixture(root);
    }

    public Workspace Open() => FixtureSolutions.Build(Root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `-- --filter-class DigitalBrain.Tests.Coding.ChangeSetEditorFacts`
Expected: compile errors for `ChangeSetEditor`, `EditOutcome`, `DiskFixture`, `CommitAsync`.

- [ ] **Step 3: Write the diff and the outcome records**

```csharp
// src/Modules/Coding/Coding/LineDiff.cs
using System.Text;

namespace DigitalBrain.Coding;

// A unified-style diff of the lines that differ, computed from the common prefix and suffix; enough for a
// card and for the model to see what changed, without a full LCS.
internal static class LineDiff
{
    internal static string Render(string path, string before, string after)
    {
        var oldLines = before.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
        var newLines = after.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
        {
            suffix++;
        }

        var removed = oldLines.Length - prefix - suffix;
        var added = newLines.Length - prefix - suffix;
        if (removed == 0 && added == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder()
            .Append("--- a/").AppendLine(path)
            .Append("+++ b/").AppendLine(path)
            .Append("@@ -").Append(prefix + 1).Append(',').Append(removed).Append(" +").Append(prefix + 1).Append(',').Append(added).AppendLine(" @@");
        foreach (var line in oldLines.Skip(prefix).Take(removed))
        {
            text.Append('-').AppendLine(line);
        }

        foreach (var line in newLines.Skip(prefix).Take(added))
        {
            text.Append('+').AppendLine(line);
        }

        return text.ToString();
    }
}
```

```csharp
// src/Modules/Coding/Coding/EditOutcome.cs
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Coding;

public sealed record EditOutcome(
    Solution Changed,
    IReadOnlyList<DiagnosticHit> Diagnostics,
    string Diff,
    IReadOnlyList<string> ChangedPaths,
    int? FailingEdit,
    string? Detail)
{
    public bool HasErrors => FailingEdit is not null || Diagnostics.Any(static hit => hit.Severity == nameof(DiagnosticSeverity.Error));
}
```

```csharp
// src/Modules/Coding/Coding/CommitOutcome.cs
namespace DigitalBrain.Coding;

public sealed record CommitOutcome(IReadOnlyList<string> WrittenPaths, long Generation);
```

- [ ] **Step 4: Write the editor**

```csharp
// src/Modules/Coding/Coding/ChangeSetEditor.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Coding;

public sealed class ChangeSetEditor
{
    private const int DiagnosticLimit = 200;

    public async Task<EditOutcome> ApplyAsync(Solution original, IReadOnlyList<EditRequest> edits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edits);
        var solution = original;
        var touched = new List<(int Edit, string Path)>();
        for (var index = 0; index < edits.Count; index++)
        {
            try
            {
                var (next, path) = await ApplyOneAsync(solution, edits[index], cancellationToken).ConfigureAwait(false);
                solution = next;
                touched.Add((index, path));
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                return new EditOutcome(solution, [], await DiffAsync(original, solution, cancellationToken).ConfigureAwait(false),
                    ChangedPaths(original, solution), index, Describe(index, edits[index], error.Message));
            }
        }

        solution = await FormatAsync(original, solution, cancellationToken).ConfigureAwait(false);
        var diagnostics = await SolutionQueries.ProjectDiagnosticsAsync(solution, ChangedAndDependents(original, solution), DiagnosticLimit, cancellationToken).ConfigureAwait(false);
        int? failing = null;
        string? detail = null;
        if (diagnostics.Items.FirstOrDefault(static hit => hit.Severity == nameof(DiagnosticSeverity.Error)) is { } firstError)
        {
            // The last edit that touched the failing file is the likely cause; else the last edit overall.
            var responsible = touched.LastOrDefault(entry => string.Equals(entry.Path, firstError.Path, StringComparison.OrdinalIgnoreCase));
            failing = touched.Count == 0 ? 0 : responsible.Path is null ? touched[^1].Edit : responsible.Edit;
            detail = Describe(failing.Value, edits[failing.Value], $"left {diagnostics.ErrorCount} error(s); first: {firstError.Id} {firstError.Path}:{firstError.Line} {firstError.Message}");
        }

        return new EditOutcome(solution, diagnostics.Items, await DiffAsync(original, solution, cancellationToken).ConfigureAwait(false),
            ChangedPaths(original, solution), failing, detail);
    }

    private static async Task<(Solution Solution, string Path)> ApplyOneAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
        => edit.Kind switch
        {
            EditKind.ReplaceMember => await ReplaceMemberAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.InsertMember => await InsertMemberAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.AddUsing => await AddUsingAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.ReplaceRange => await ReplaceRangeAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.Rename => throw new NotSupportedException("phase 1 task 4"),
            EditKind.ApplyCodeFix => throw new NotSupportedException("phase 1 task 4"),
            _ => throw new InvalidOperationException($"Unknown edit kind '{edit.Kind}'."),
        };

    private static async Task<(Solution, string)> ReplaceMemberAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var (document, node) = await TargetAsync(solution, edit, cancellationToken).ConfigureAwait(false);
        var replacement = ParseMember(Required(edit.Source, "source"));
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(node, replacement.WithTriviaFrom(node));
        return (editor.GetChangedDocument().Project.Solution, document.FilePath!);
    }

    private static async Task<(Solution, string)> InsertMemberAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var (document, node) = await TargetAsync(solution, edit, cancellationToken).ConfigureAwait(false);
        var member = ParseMember(Required(edit.Source, "source"));
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (node is TypeDeclarationSyntax)
        {
            editor.AddMember(node, member);
        }
        else
        {
            editor.InsertAfter(node, member);
        }

        return (editor.GetChangedDocument().Project.Solution, document.FilePath!);
    }

    private static async Task<(Solution, string)> AddUsingAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var name = Required(edit.Namespace, "namespace");
        var document = SolutionQueries.DocumentAt(solution, path);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException($"'{path}' has no compilation unit.");
        if (root.Usings.Any(directive => directive.Name?.ToString() == name))
        {
            return (solution, path);
        }

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);
        return (document.WithSyntaxRoot(root.AddUsings(directive)).Project.Solution, path);
    }

    private static async Task<(Solution, string)> ReplaceRangeAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var document = SolutionQueries.DocumentAt(solution, path);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var start = edit.StartLine ?? throw new InvalidOperationException("ReplaceRange needs startLine.");
        var end = edit.EndLine ?? start;
        if (start < 1 || end < start || end > text.Lines.Count)
        {
            throw new InvalidOperationException($"Lines {start}-{end} are outside '{path}' ({text.Lines.Count} lines).");
        }

        var span = TextSpan.FromBounds(text.Lines[start - 1].Start, text.Lines[end - 1].End);
        var changed = text.WithChanges(new TextChange(span, edit.Source ?? string.Empty));
        return (document.WithText(changed).Project.Solution, path);
    }

    private static async Task<(Document Document, SyntaxNode Node)> TargetAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var symbol = await SolutionQueries.ResolveAsync(solution, Required(edit.SymbolId, "symbolId"), cancellationToken).ConfigureAwait(false);
        var node = await SolutionQueries.DeclarationAsync(symbol, cancellationToken).ConfigureAwait(false);
        var document = solution.GetDocument(node.SyntaxTree)
            ?? throw new InvalidOperationException($"'{edit.SymbolId}' is declared outside the solution.");
        return (document, node);
    }

    private static MemberDeclarationSyntax ParseMember(string source)
    {
        var member = SyntaxFactory.ParseMemberDeclaration(source);
        if (member is null || member.ContainsDiagnostics || member.FullSpan.Length < source.Trim().Length)
        {
            throw new InvalidOperationException("source is not one complete member declaration (a method, property, field, event, or type).");
        }

        return member.WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is required for this edit kind.") : value;

    private static string Describe(int index, EditRequest edit, string message)
        => $"edit {index + 1} ({edit.Kind} {edit.SymbolId ?? edit.Path ?? "?"}) {message}";

    private static async Task<Solution> FormatAsync(Solution original, Solution solution, CancellationToken cancellationToken)
    {
        foreach (var id in ChangedDocumentIds(original, solution))
        {
            var formatted = await Formatter.FormatAsync(solution.GetDocument(id)!, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            solution = formatted.Project.Solution;
        }

        return solution;
    }

    private static IReadOnlyCollection<ProjectId> ChangedAndDependents(Solution original, Solution solution)
    {
        var graph = solution.GetProjectDependencyGraph();
        var projects = new HashSet<ProjectId>();
        foreach (var change in solution.GetChanges(original).GetProjectChanges())
        {
            projects.Add(change.ProjectId);
            projects.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(change.ProjectId));
        }

        return projects;
    }

    private static IReadOnlyList<DocumentId> ChangedDocumentIds(Solution original, Solution solution)
        => [.. solution.GetChanges(original).GetProjectChanges().SelectMany(static change => change.GetChangedDocuments())];

    private static IReadOnlyList<string> ChangedPaths(Solution original, Solution solution)
        => [.. ChangedDocumentIds(original, solution).Select(id => solution.GetDocument(id)!.FilePath ?? id.ToString()).Order(StringComparer.Ordinal)];

    private static async Task<string> DiffAsync(Solution original, Solution solution, CancellationToken cancellationToken)
    {
        var diff = new System.Text.StringBuilder();
        foreach (var id in ChangedDocumentIds(original, solution))
        {
            var before = await original.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var after = await solution.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            diff.Append(LineDiff.Render(solution.GetDocument(id)!.FilePath ?? id.ToString(), before.ToString(), after.ToString()));
        }

        return diff.ToString();
    }
}
```

`SolutionQueries.DiagnosticsAsync` keeps its document branch and delegates its project branch to the new shared method:

```csharp
    internal static Task<DiagnosticsResult> ProjectDiagnosticsAsync(Solution solution, IReadOnlyCollection<ProjectId> projects, int limit, CancellationToken cancellationToken)
```

which contains the existing loop (`GetCompilationAsync`, `compilation.GetDiagnostics`, the location-less fallback to the
project path, the sort and the `DiagnosticsResult` envelope with `TotalCount`) over `projects.Select(solution.GetProject)`.
`DiagnosticsAsync`'s project branch computes the project id set (all, or the named project) and calls it.

- [ ] **Step 5: The commit path on the service**

`Lease` gains `Workspace Workspace` (captured under `_gate` in `AcquireAsync` from `_workspace`). Add to `SolutionWorkspace`:

```csharp
    private long _generation;

    public long Generation => Interlocked.Read(ref _generation);

    // One lease around the whole transaction: the change is computed on the leased snapshot, applied through
    // TryApplyChanges, and only then written to disk document by document. TryApplyChanges refuses a snapshot
    // the workspace has moved past, so a fold or a reload between check and commit cannot be overwritten.
    public async Task<CommitOutcome> CommitAsync(Func<Solution, CancellationToken, Task<Solution>> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var changed = await change(lease.Solution, cancellationToken).ConfigureAwait(false);
        var documents = changed.GetChanges(lease.Solution).GetProjectChanges()
            .SelectMany(project => project.GetChangedDocuments().Select(id => changed.GetDocument(id)!))
            .OrderBy(static document => document.FilePath, StringComparer.Ordinal)
            .ToArray();
        if (!lease.Workspace.TryApplyChanges(changed))
        {
            throw new InvalidOperationException("The workspace changed while the change set was being checked. Check it again before committing.");
        }

        var written = new List<string>();
        foreach (var document in documents)
        {
            var path = document.FilePath ?? throw new InvalidOperationException($"Document '{document.Name}' has no file path to write to.");
            var text = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (!File.Exists(path) || !string.Equals(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), text, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
            }

            written.Add(path);
        }

        return new CommitOutcome(written, Interlocked.Increment(ref _generation));
    }
```

Register the editor in `CodingModule.Configure`: `builder.Services.TryAddSingleton<ChangeSetEditor>();`.

- [ ] **Step 6: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.ChangeSetEditorFacts` then `SolutionWorkspaceFacts` and `WorkspaceReadFacts`.
Expected: all PASS. If `Formatter.FormatAsync` changes indentation of the inserted property so the skeleton order
still holds but the diff shows extra whitespace lines, keep the facts (they check order and containment, not
whitespace). If `AddUsings` places the directive after the file-scoped namespace, build the new root with
`root.WithUsings(root.Usings.Add(directive))` instead. If `TryApplyChanges` returns false for a snapshot
derived from the current one on `AdhocWorkspace`, the changed solution must derive from `lease.Solution`
(never from a solution captured in an earlier lease) — the second commit fact pins the refusal on a stale one.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: edits applied to one snapshot, diagnosed, diffed and committed in order"
```

---

### Task 4: Rename by symbol id and code fixes from the Features package

**Files:**
- Modify: `Directory.Packages.props` (`Microsoft.CodeAnalysis.CSharp.Features` 5.9.0), `src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj`,
  `ChangeSetEditor.cs`, `CodingModule.cs`, `tests/DigitalBrain.Tests/Features/Coding/ChangeSetEditorFacts.cs`
- Create: `src/Modules/Coding/Coding/CodeFixCatalog.cs`

**Interfaces:**
- Produces: `public sealed class CodeFixCatalog` with `IReadOnlyList<CodeFixProvider> Providers` and
  `IEnumerable<CodeFixProvider> For(string diagnosticId)`; `ChangeSetEditor(CodeFixCatalog codeFixes)` primary constructor
  (the facts' `new()` becomes `new(new CodeFixCatalog())`).

- [ ] **Step 1: Write the failing facts**

Append to `ChangeSetEditorFacts` (and change `Editor` to `new(new CodeFixCatalog())`):

```csharp
    [Fact]
    public async Task Rename_by_symbol_id_updates_every_reference()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.Rename, SymbolId: "M:Alpha.Greeter.Greet(System.String)", NewName: "Hello")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.Equal([FixtureSolutions.GreeterPath, FixtureSolutions.ProgramPath], outcome.ChangedPaths);
        Assert.Contains(""".Hello("world")""", await TextAsync(outcome.Changed, FixtureSolutions.ProgramPath), StringComparison.Ordinal);
        Assert.Contains("public string Hello(string name)", await TextAsync(outcome.Changed, FixtureSolutions.GreeterPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_refuses_an_invalid_identifier()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.Rename, SymbolId: "M:Alpha.Greeter.Greet(System.String)", NewName: "not an identifier")],
            TestContext.Current.CancellationToken);
        Assert.Equal(0, outcome.FailingEdit);
        Assert.Contains("identifier", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_fix_removes_the_unused_variable()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ApplyCodeFix, Path: FixtureSolutions.UnusedPath, DiagnosticId: "CS0219")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.DoesNotContain("int count", await TextAsync(outcome.Changed, FixtureSolutions.UnusedPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_fix_for_a_diagnostic_that_is_not_there_is_advice()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ApplyCodeFix, Path: FixtureSolutions.GreeterPath, DiagnosticId: "CS0219")],
            TestContext.Current.CancellationToken);
        Assert.Equal(0, outcome.FailingEdit);
        Assert.Contains("CS0219", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_catalog_finds_a_fixer_for_unused_variables()
    {
        Assert.NotEmpty(new CodeFixCatalog().For("CS0219"));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `-- --filter-class DigitalBrain.Tests.Coding.ChangeSetEditorFacts`
Expected: CS0246 `CodeFixCatalog`.

- [ ] **Step 3: Add the package**

`Directory.Packages.props`, next to the other `Microsoft.CodeAnalysis.*` versions: `<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Features" Version="5.9.0" />`.
`DigitalBrain.Modules.Coding.csproj`: `<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Features" />`. Build the Coding
project; if MSBL001 or a copy-check error fires on a project that does not carry the property yet, add
`DisableMSBuildAssemblyCopyCheck` there with the usual one-line comment and name it in the report.

- [ ] **Step 4: Write the catalog**

```csharp
// src/Modules/Coding/Coding/CodeFixCatalog.cs
using System.Reflection;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DigitalBrain.Coding;

// The C# code fixers ship as MEF exports in Microsoft.CodeAnalysis.CSharp.Features. This host has no MEF
// composition for them, so the ones with a parameterless constructor are built by reflection; fixers that
// import services are skipped, and a fixer that throws while registering is skipped by the editor.
public sealed class CodeFixCatalog
{
    private readonly Lazy<IReadOnlyList<CodeFixProvider>> _providers = new(Discover);

    public IReadOnlyList<CodeFixProvider> Providers => _providers.Value;

    public IEnumerable<CodeFixProvider> For(string diagnosticId)
        => Providers.Where(provider => provider.FixableDiagnosticIds.Contains(diagnosticId, StringComparer.Ordinal));

    private static IReadOnlyList<CodeFixProvider> Discover()
    {
        var assembly = Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            types = partial.Types;
        }

        var providers = new List<CodeFixProvider>();
        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type)
                || type.GetCustomAttribute<ExportCodeFixProviderAttribute>() is null)
            {
                continue;
            }

            var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
            if (constructor is null)
            {
                continue;
            }

            try
            {
                providers.Add((CodeFixProvider)constructor.Invoke(null));
            }
            catch (TargetInvocationException)
            {
                // A fixer whose constructor needs the IDE host is not usable here.
            }
        }

        return providers;
    }
}
```

- [ ] **Step 5: The two edit kinds**

`ChangeSetEditor` becomes `public sealed class ChangeSetEditor(CodeFixCatalog codeFixes)`; `ApplyOneAsync` becomes an
instance method with the two cases:

```csharp
            EditKind.Rename => await RenameAsync(solution, edit, cancellationToken).ConfigureAwait(false),
            EditKind.ApplyCodeFix => await ApplyCodeFixAsync(solution, edit, cancellationToken).ConfigureAwait(false),
```

```csharp
    private static async Task<(Solution, string)> RenameAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var newName = Required(edit.NewName, "newName");
        if (!SyntaxFacts.IsValidIdentifier(newName))
        {
            throw new InvalidOperationException($"'{newName}' is not a valid C# identifier.");
        }

        var (document, _) = await TargetAsync(solution, edit, cancellationToken).ConfigureAwait(false);
        var symbol = await SolutionQueries.ResolveAsync(solution, edit.SymbolId!, cancellationToken).ConfigureAwait(false);
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(false);
        return (renamed, document.FilePath!);
    }

    private async Task<(Solution, string)> ApplyCodeFixAsync(Solution solution, EditRequest edit, CancellationToken cancellationToken)
    {
        var path = Required(edit.Path, "path");
        var diagnosticId = Required(edit.DiagnosticId, "diagnosticId");
        var document = SolutionQueries.DocumentAt(solution, path);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{path}' has no semantic model.");
        var diagnostic = model.GetDiagnostics(cancellationToken: cancellationToken)
            .FirstOrDefault(candidate => candidate.Id == diagnosticId
                && (edit.StartLine is null || candidate.Location.GetLineSpan().StartLinePosition.Line + 1 == edit.StartLine))
            ?? throw new InvalidOperationException($"No {diagnosticId} diagnostic in '{path}'{(edit.StartLine is { } line ? $" at line {line}" : string.Empty)}. Ask diagnostics for the ids and lines it has.");
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);
        foreach (var provider in codeFixes.For(diagnosticId))
        {
#pragma warning disable CA1031 // a fixer built outside its MEF host may fail in any way; the next fixer gets its turn
            try
            {
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                continue;
            }
#pragma warning restore CA1031
        }

        var flat = actions.SelectMany(static action => action.NestedActions.Length > 0 ? action.NestedActions : [action]).ToArray();
        var chosen = flat.FirstOrDefault(action => edit.FixTitle is null || string.Equals(action.Title, edit.FixTitle, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(flat.Length == 0
                ? $"No code fix is available for {diagnosticId} in this host."
                : $"No fix titled '{edit.FixTitle}'; available: {string.Join(" | ", flat.Select(static action => action.Title))}.");
        var operations = await chosen.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var apply = operations.OfType<ApplyChangesOperation>().FirstOrDefault()
            ?? throw new InvalidOperationException($"The fix '{chosen.Title}' does not change the solution.");
        return (apply.ChangedSolution, path);
    }
```

with `using Microsoft.CodeAnalysis.CodeActions;`, `using Microsoft.CodeAnalysis.CodeFixes;`, `using Microsoft.CodeAnalysis.Rename;`.
`CodingModule.Configure` adds `builder.Services.TryAddSingleton<CodeFixCatalog>();` before the editor.

- [ ] **Step 6: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.ChangeSetEditorFacts`
Expected: all PASS. If `The_catalog_finds_a_fixer_for_unused_variables` is empty, print the discovered
provider count and the names of the types skipped for lacking a parameterless constructor; the fixer for
CS0168/CS0219 is `CSharpRemoveUnusedVariableCodeFixProvider`, whose constructor is `[ImportingConstructor]`
and parameterless — if it has a required-service constructor in 5.9.0, report DONE_WITH_CONCERNS with the
constructor signature so the controller can rule (the fallback is a `NotSupportedException` advice for
`ApplyCodeFix` recorded in NOTES). If `RegisterCodeFixesAsync` returns no action for CS0219, try CS0168 by
changing the fixture line to `int count;` and say so.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: rename by symbol id and code fixes from the Features package"
```

---

### Task 5: The `changeset` neuron

**Files:**
- Create: `src/Modules/Coding/Contracts/CommittingBody.cs`, `src/Modules/Coding/Coding/ChangeSetState.cs`, `ChangeSetNeuron.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/ChangeSetNeuronFacts.cs`
- Modify: `src/Modules/Coding/Contracts/CodingJson.cs` (`CommittingBody`)

**Interfaces:**
- Consumes: `IChangeSet` and its DTOs (Task 1), `ChangeSetEditor.ApplyAsync`, `SolutionWorkspace.QueryAsync`/`CommitAsync`/`Generation` (Tasks 3-4).
- Produces: `[GrainType("changeset")] ChangeSetNeuron`; the change set's version for `ProposeEdit.ExpectedVersion` is its edit count.

- [ ] **Step 1: Write the failing neuron facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/ChangeSetNeuronFacts.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class ChangeSetNeuronFacts
{
    private static readonly EditRequest FriendlyGreet = new(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""");
    private static readonly EditRequest BrokenGreet = new(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public string Greet(string name) => 42;");

    private static async Task<(BrainSimulation Brain, DiskFixture Fixture)> StartAsync()
    {
        var fixture = DiskFixture.Create();
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(fixture.Open)),
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = fixture.SolutionPath },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        return (brain, fixture);
    }

    private static IChangeSet ChangeSet(BrainSimulation brain, string id)
        => brain.Grains.GetGrain<IChangeSet>(new NeuronId(CodingVocabulary.ChangeSetType, id).ToGrainId());

    private static async Task<ChangeSetSnapshot> WaitAsync(IChangeSet changeSet, Func<ChangeSetSnapshot, bool> done)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var snapshot = await changeSet.Read();
            if (done(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task Propose_then_check_yields_a_checked_snapshot_with_a_diff()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c1");
        var accepted = await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        Assert.Equal("c1", accepted.Receipt.ChangeId);
        Assert.Equal(1, accepted.Receipt.EditCount);
        await changeSet.Check(new CheckChangeSet(CommandId.New()));
        var checkedSnapshot = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Checked);
        Assert.Contains("+    public string Greet(string name)", checkedSnapshot.Diff, StringComparison.Ordinal);
        Assert.Empty(checkedSnapshot.Diagnostics);
        Assert.Null(checkedSnapshot.Detail);
    }

    [Fact]
    public async Task A_check_with_errors_stays_a_draft_and_names_the_edit()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c2");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), BrokenGreet));
        await changeSet.Check(new CheckChangeSet(CommandId.New()));
        var snapshot = await WaitAsync(changeSet, snapshot => snapshot.Detail is not null);
        Assert.Equal(ChangeSetStatus.Draft, snapshot.Status);
        Assert.StartsWith("edit 1 (ReplaceMember", snapshot.Detail, StringComparison.Ordinal);
        Assert.Contains(snapshot.Diagnostics, hit => hit.Id == "CS0029");
    }

    [Fact]
    public async Task Commit_writes_the_file_and_records_the_generation()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c3");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        await changeSet.Commit(new CommitChangeSet(CommandId.New(), "friendlier greeting"));
        var committed = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Committed || snapshot.Detail is not null);
        Assert.Equal(ChangeSetStatus.Committed, committed.Status);
        Assert.Equal([fixture.GreeterPath], committed.Files);
        Assert.Equal(1, committed.Generation);
        Assert.Contains("Hi, {name}", await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet)));
        Assert.Contains("committed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Commit_refuses_errors_and_leaves_the_file_alone()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c4");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), BrokenGreet));
        await changeSet.Commit(new CommitChangeSet(CommandId.New(), "broken"));
        var refused = await WaitAsync(changeSet, snapshot => snapshot.Detail is not null);
        Assert.Equal(ChangeSetStatus.Draft, refused.Status);
        Assert.Equal(FixtureSolutions.GreeterSource, await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Discard_closes_the_change_set()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c5");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        await changeSet.Discard(new DiscardChangeSet(CommandId.New()));
        var discarded = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Discarded);
        Assert.Single(discarded.Edits);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changeSet.Check(new CheckChangeSet(CommandId.New())));
        Assert.Contains("discarded", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_without_edits_is_refused_with_advice()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ChangeSet(brain, "c6").Check(new CheckChangeSet(CommandId.New())));
        Assert.Contains("Propose at least one edit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_expected_version_is_refused()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c7");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        await WaitAsync(changeSet, snapshot => snapshot.Edits.Count == 1);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet, ExpectedVersion: 0)));
        Assert.Contains("expected version 0", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `-- --filter-class DigitalBrain.Tests.Coding.ChangeSetNeuronFacts`
Expected: every fact fails with Orleans reporting no implementation for `IChangeSet` (CS errors if `CommittingBody` is referenced before it exists — it is not referenced by the facts).

- [ ] **Step 3: Write the body, the state and the neuron**

```csharp
// src/Modules/Coding/Contracts/CommittingBody.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.committing-body")]
public sealed record CommittingBody([property: Id(0)] string Message);
```

Add `[JsonSerializable(typeof(CommittingBody))]` to `CodingJson`.

```csharp
// src/Modules/Coding/Coding/ChangeSetState.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.change-set-state")]
internal sealed record ChangeSetState(
    [property: Id(0)] ChangeSetStatus Status,
    [property: Id(1)] IReadOnlyList<EditRequest> Edits,
    [property: Id(2)] IReadOnlyList<DiagnosticHit> Diagnostics,
    [property: Id(3)] string? Diff,
    [property: Id(4)] long Generation,
    [property: Id(5)] string? Detail,
    [property: Id(6)] IReadOnlyList<string> Files)
{
    public static readonly ChangeSetState Empty = new(ChangeSetStatus.Draft, [], [], null, 0, null, []);
}
```

```csharp
// src/Modules/Coding/Coding/ChangeSetNeuron.cs
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType(CodingVocabulary.ChangeSetType)]
internal sealed class ChangeSetNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ChangeSetState>> state,
    SolutionWorkspace workspace,
    ChangeSetEditor editor)
    : Neuron<ChangeSetState>(runtime, state), IChangeSet
{
    private ChangeSetState Current => State ?? ChangeSetState.Empty;

    public Task<Accepted<ChangeSetReceipt>> Propose(ProposeEdit command) => ExecuteCommandAsync(
        Descriptor("propose"), command, CodingJson.Default.ProposeEdit, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            if (arguments.Edit is null)
            {
                throw new CommandRejectedException(arguments.Id, "edit is missing", "Provide one edit: kind plus the fields that kind needs.");
            }

            if (arguments.ExpectedVersion is { } expected && expected != Current.Edits.Count)
            {
                throw new CommandRejectedException(arguments.Id, $"expected version {expected} but the change set has {Current.Edits.Count} edits",
                    "Read the change set and retry with the number of edits it reports.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.ChangeSetProposing, arguments.Edit, CodingJson.Default.EditRequest));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count + 1), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Check(CheckChangeSet command) => ExecuteCommandAsync(
        Descriptor("check"), command, CodingJson.Default.CheckChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            RejectWhenEmpty(arguments.Id);
            var work = Schedule(Signal.Create(CodingVocabulary.ChangeSetChecking, "{}"));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Commit(CommitChangeSet command) => ExecuteCommandAsync(
        Descriptor("commit"), command, CodingJson.Default.CommitChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            RejectWhenEmpty(arguments.Id);
            if (string.IsNullOrWhiteSpace(arguments.Message))
            {
                throw new CommandRejectedException(arguments.Id, "message is blank", "Say what the change does in one line.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.ChangeSetCommitting, new CommittingBody(arguments.Message), CodingJson.Default.CommittingBody));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    public Task<Accepted<ChangeSetReceipt>> Discard(DiscardChangeSet command) => ExecuteCommandAsync(
        Descriptor("discard"), command, CodingJson.Default.DiscardChangeSet, CodingJson.Default.AcceptedChangeSetReceipt, arguments =>
        {
            RejectWhenClosed(arguments.Id);
            var work = Schedule(Signal.Create(CodingVocabulary.ChangeSetDiscarding, "{}"));
            return new Accepted<ChangeSetReceipt>(Receipt(Current.Edits.Count), work);
        });

    [ReadOnly]
    public Task<ChangeSetSnapshot> Read()
    {
        var current = Current;
        return Task.FromResult(new ChangeSetSnapshot(current.Status, current.Edits, current.Diagnostics, current.Diff, current.Generation, current.Detail, current.Files));
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = Current;
        switch (delivery.Signal.Type)
        {
            case CodingVocabulary.ChangeSetProposing:
                {
                    if (Body(delivery, CodingJson.Default.EditRequest) is not { } edit)
                    {
                        return;
                    }

                    await SaveAsync(current with { Status = ChangeSetStatus.Draft, Edits = [.. current.Edits, edit], Diagnostics = [], Diff = null, Detail = null }, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetChecking:
                {
                    ChangeSetState next;
                    try
                    {
                        var outcome = await workspace.QueryAsync((solution, token) => editor.ApplyAsync(solution, current.Edits, token), cancellationToken).ConfigureAwait(true);
                        next = current with
                        {
                            Status = outcome.HasErrors ? ChangeSetStatus.Draft : ChangeSetStatus.Checked,
                            Diagnostics = outcome.Diagnostics,
                            Diff = outcome.Diff,
                            Detail = outcome.Detail,
                        };
                    }
                    catch (InvalidOperationException error)
                    {
                        // The workspace is not ready or refused the snapshot; the detail is the advice.
                        next = current with { Status = ChangeSetStatus.Draft, Detail = error.Message };
                    }

                    await SaveAsync(next, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetCommitting:
                {
                    EditOutcome? applied = null;
                    ChangeSetState next;
                    try
                    {
                        var committed = await workspace.CommitAsync(async (solution, token) =>
                        {
                            applied = await editor.ApplyAsync(solution, current.Edits, token).ConfigureAwait(false);
                            return applied.HasErrors
                                ? throw new InvalidOperationException(applied.Detail ?? "the change set has errors")
                                : applied.Changed;
                        }, cancellationToken).ConfigureAwait(true);
                        next = current with
                        {
                            Status = ChangeSetStatus.Committed,
                            Diagnostics = applied!.Diagnostics,
                            Diff = applied.Diff,
                            Detail = null,
                            Files = committed.WrittenPaths,
                            Generation = committed.Generation,
                        };
                    }
                    catch (InvalidOperationException error)
                    {
                        next = current with
                        {
                            Status = ChangeSetStatus.Draft,
                            Diagnostics = applied?.Diagnostics ?? current.Diagnostics,
                            Diff = applied?.Diff ?? current.Diff,
                            Detail = error.Message,
                        };
                    }

                    await SaveAsync(next, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.ChangeSetDiscarding:
                await SaveAsync(current with { Status = ChangeSetStatus.Discarded, Detail = null }, cancellationToken).ConfigureAwait(true);
                break;
            default:
                return;
        }
    }

    private ChangeSetReceipt Receipt(int editCount) => new(Id.Name, editCount, Current.Status);

    private void RejectWhenClosed(CommandId id)
    {
        if (Current.Status is ChangeSetStatus.Committed or ChangeSetStatus.Discarded)
        {
            throw new CommandRejectedException(id, $"the change set is {Current.Status.ToString().ToLowerInvariant()}", "Start a new change set with a new id.");
        }
    }

    private void RejectWhenEmpty(CommandId id)
    {
        if (Current.Edits.Count == 0)
        {
            throw new CommandRejectedException(id, "no edits", "Propose at least one edit first.");
        }
    }
}
```

- [ ] **Step 4: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.ChangeSetNeuronFacts` then `CodeWorkspaceNeuronFacts`.
Expected: all PASS. If the silo refuses to start with a descriptor message, it names the method and the rule.
If `Propose` after commit reports a message without "committed", the rejection text is the third
`CommandRejectedException` argument — put the status word in the message as the code above does.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: changeset neuron with propose, check, commit and discard"
```

---

### Task 6: The file watcher, the reload-needed flag and the durable map cache

**Files:**
- Create: `src/Modules/Coding/Coding/SolutionFileWatcher.cs`, `tests/DigitalBrain.Tests/Features/Coding/SolutionFileWatcherFacts.cs`
- Modify: `SolutionWorkspace.cs` (`FoldAsync`, `MarkReloadNeeded`, `Opened` callback, same-path open is a no-op when Ready),
  `WorkspaceStatus.cs` (`ReloadNeeded`), `WorkspaceWarmup.cs` (starts the watcher, opens the grain), `WorkspaceState.cs` (`LastMap`),
  `WorkspaceNeuron.cs` (map from cache, mapping reaction), `CodingModule.cs`, `CodeWorkspaceNeuronFacts.cs`

**Interfaces:**
- Produces: `SolutionWorkspace.FoldAsync(string path, string text, CancellationToken) -> Task<bool>`, `MarkReloadNeeded(string path)`,
  `internal Action<string>? Opened` (called with the solution path after each successful load);
  `WorkspaceStatus.ReloadNeeded`; `SolutionFileWatcher.Start(string directory)` (idempotent per directory) and `Stop()`;
  `CodingModule.WorkspaceKeyKey = "DigitalBrain:Coding:WorkspaceKey"` (default key `digitalbrain`).

- [ ] **Step 1: Write the failing watcher facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/SolutionFileWatcherFacts.cs
using DigitalBrain.Coding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionFileWatcherFacts
{
    private static async Task<bool> UntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }

    [Fact]
    public async Task A_saved_source_file_is_folded_into_the_snapshot()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        using var watcher = new SolutionFileWatcher(workspace, NullLogger<SolutionFileWatcher>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        watcher.Start(fixture.Root);

        await File.WriteAllTextAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource.Replace("public string Welcome", "public string Hola(string name) => name;\n\n    public string Welcome", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        Assert.True(await UntilAsync(async () => (await workspace.FindSymbolsAsync(new("Hola"), TestContext.Current.CancellationToken)).TotalCount == 1, TimeSpan.FromSeconds(10)));
        Assert.True(workspace.Generation >= 1);
        Assert.False(workspace.Status.ReloadNeeded);
    }

    [Fact]
    public async Task A_project_file_change_flags_a_reload()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        using var watcher = new SolutionFileWatcher(workspace, NullLogger<SolutionFileWatcher>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        watcher.Start(fixture.Root);

        await File.WriteAllTextAsync(fixture.Root + "/Alpha/Alpha.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken);

        Assert.True(await UntilAsync(() => Task.FromResult(workspace.Status.ReloadNeeded), TimeSpan.FromSeconds(10)));
        Assert.Contains("Alpha.csproj", workspace.Status.Detail, StringComparison.Ordinal);
        await workspace.BeginReloadAsync();
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.False(workspace.Status.ReloadNeeded);
    }

    [Fact]
    public async Task Files_under_obj_are_ignored_and_a_fold_of_the_same_text_is_a_no_op()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);

        Assert.False(await workspace.FoldAsync(fixture.Root + "/Alpha/obj/Generated.cs", "namespace X;", TestContext.Current.CancellationToken));
        Assert.False(await workspace.FoldAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource, TestContext.Current.CancellationToken));
        Assert.Equal(0, workspace.Generation);
        Assert.True(SolutionFileWatcher.IsIgnored(fixture.Root + "/Alpha/obj/Debug/x.g.cs"));
        Assert.True(SolutionFileWatcher.IsIgnored(fixture.Root + "/.git/index"));
        Assert.False(SolutionFileWatcher.IsIgnored(fixture.GreeterPath));
    }
}
```

And two facts in `CodeWorkspaceNeuronFacts`:

```csharp
    [Fact]
    public async Task The_warmup_opens_the_grain_so_its_state_records_the_solution()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
            Configuration = new Dictionary<string, string?>
            {
                [CodingModule.SolutionPathKey] = "E:/fixture/Fixture.slnx",
                [CodingModule.WorkspaceKeyKey] = "fixture",
            },
        });
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var snapshot = await WaitAsync(workspace, WorkspacePhase.Ready);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (snapshot.Generation < 1)
        {
            await Task.Delay(50, timeout.Token);
            snapshot = await workspace.Read();
        }

        Assert.Equal(1, snapshot.Generation);
        Assert.Equal(Path.GetFullPath("E:/fixture/Fixture.slnx"), snapshot.SolutionPath);
    }

    [Fact]
    public async Task The_map_answers_from_the_durable_cache_while_a_reload_is_in_flight()
    {
        var gate = new TaskCompletionSource();
        var opens = 0;
        var loader = new GatedSecondOpenLoader(FixtureSolutions.TwoProjects, gate, () => opens++);
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(loader),
        });
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        await WaitAsync(workspace, WorkspacePhase.Ready);
        var first = await workspace.Map(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Projects.Count);

        await workspace.Reload(new ReloadWorkspace(CommandId.New()));
        await WaitAsync(workspace, WorkspacePhase.Opening);
        var cached = await workspace.Map(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, cached.Projects.Count);
        gate.SetResult();
        await WaitAsync(workspace, WorkspacePhase.Ready);
    }

    // The first open completes at once; the second waits for the gate so a fact can observe "Opening".
    private sealed class GatedSecondOpenLoader(Func<Microsoft.CodeAnalysis.Workspace> open, TaskCompletionSource gate, Action opened) : ISolutionLoader
    {
        private int _opens;

        public async Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _opens) > 1)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            opened();
            return new LoadedSolution(open(), []);
        }
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `-- --filter-class DigitalBrain.Tests.Coding.SolutionFileWatcherFacts` and `CodeWorkspaceNeuronFacts`.
Expected: compile errors (`SolutionFileWatcher`, `FoldAsync`, `ReloadNeeded`, `WorkspaceKeyKey`).

- [ ] **Step 3: The service side**

`WorkspaceStatus` gains a sixth positional member `bool ReloadNeeded = false` (`NotOpened` unchanged). `WorkspaceNeuron.Read()`
passes `live.ReloadNeeded`. `SolutionWorkspace` gains:

```csharp
    internal Action<string>? Opened { get; set; }

    // Folds an external save of one document into the snapshot; false when the path is not a document or
    // the text is unchanged (our own commit writes come back through the watcher and must not bump anything).
    public async Task<bool> FoldAsync(string path, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        if (Status.Phase != WorkspacePhase.Ready)
        {
            return false;
        }

        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var ids = lease.Solution.GetDocumentIdsWithFilePath(path);
        if (ids.IsDefaultOrEmpty)
        {
            return false;
        }

        var changed = lease.Solution;
        foreach (var id in ids)
        {
            var current = await changed.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current.ToString(), text, StringComparison.Ordinal))
            {
                changed = changed.WithDocumentText(id, SourceText.From(text));
            }
        }

        if (ReferenceEquals(changed, lease.Solution) || !lease.Workspace.TryApplyChanges(changed))
        {
            return false;
        }

        Interlocked.Increment(ref _generation);
        return true;
    }

    public void MarkReloadNeeded(string path)
    {
        lock (_gate)
        {
            if (_status.Phase == WorkspacePhase.Ready)
            {
                _status = _status with { ReloadNeeded = true, Detail = $"reload needed: {path}" };
            }
        }
    }
```

`BeginOpenAsync` returns `Task.CompletedTask` when the status is already `Ready` for the same path (case-insensitive),
so the grain's open after a warmup does not reload. `OpenCoreAsync` calls `Opened?.Invoke(solutionPath)` after
the status becomes `Ready` (outside the lock). The success branch's new status has `ReloadNeeded = false`.

```csharp
// src/Modules/Coding/Coding/SolutionFileWatcher.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

// Saves from the owner's editor, dotnet format or a git checkout reach the snapshot without a reload;
// project-file changes only flag that a reload is needed (design 4.2).
public sealed class SolutionFileWatcher(SolutionWorkspace workspace, ILogger<SolutionFileWatcher> logger) : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);
    private static readonly string[] IgnoredSegments = ["/bin/", "/obj/", "/.git/", "/artifacts/", "/node_modules/"];
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _loop;
    private string? _directory;

    public static bool IsIgnored(string path)
    {
        var normalized = path.Replace('\\', '/');
        return IgnoredSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    public void Start(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (_gate)
        {
            if (string.Equals(_directory, directory, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory))
            {
                return;
            }

            StopCore();
            _directory = directory;
            _watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            _watcher.Changed += (_, args) => Enqueue(args.FullPath);
            _watcher.Created += (_, args) => Enqueue(args.FullPath);
            _watcher.Renamed += (_, args) => Enqueue(args.FullPath);
            _watcher.EnableRaisingEvents = true;
            _loop = new CancellationTokenSource();
            _ = DrainAsync(_loop.Token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public void Dispose() => Stop();

    private void Enqueue(string path)
    {
        if (IsIgnored(path))
        {
            return;
        }

        var extension = Path.GetExtension(path);
        if (extension is ".cs" or ".csproj" or ".props" or ".targets" or ".slnx" or ".sln")
        {
            _pending[path] = DateTime.UtcNow;
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Settle);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var (path, seen) in _pending.ToArray())
                {
                    if (DateTime.UtcNow - seen < Settle || !_pending.TryRemove(path, out _))
                    {
                        continue;
                    }

                    await ApplyAsync(path, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            workspace.MarkReloadNeeded(path);
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            await workspace.FoldAsync(path.Replace('\\', '/'), text, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException error)
        {
            // The editor still holds the file; the next save re-enqueues it.
            logger.LogDebug(error, "Could not read {Path} after a save; waiting for the next one.", path);
        }
        catch (InvalidOperationException error)
        {
            logger.LogDebug(error, "Could not fold {Path}.", path);
        }
    }

    private void StopCore()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        _watcher?.Dispose();
        _watcher = null;
        _directory = null;
    }
}
```

`FoldAsync` receives forward-slash paths; the adhoc fixture registers documents with forward slashes and
`MSBuildWorkspace` registers them with the platform separator, so `FoldAsync` looks the path up both ways:
`solution.GetDocumentIdsWithFilePath(path)` then, when empty, `GetDocumentIdsWithFilePath(path.Replace('/', Path.DirectorySeparatorChar))`.

- [ ] **Step 4: The warmup starts the watcher and opens the grain**

`CodingModule` gains `public const string WorkspaceKeyKey = "DigitalBrain:Coding:WorkspaceKey";` and registers
`builder.Services.TryAddSingleton<SolutionFileWatcher>();`. `WorkspaceWarmup` becomes:

```csharp
internal sealed class WorkspaceWarmup(SolutionWorkspace workspace, SolutionFileWatcher watcher, IGrainFactory grains, IConfiguration configuration, ILogger<WorkspaceWarmup> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        workspace.Opened = path => watcher.Start(Path.GetDirectoryName(path)!);
        var path = configuration[CodingModule.SolutionPathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(error, "The configured solution path '{SolutionPath}' is not a valid path; the workspace stays closed until it is opened by hand.", path);
            return Task.CompletedTask;
        }

        _ = workspace.BeginOpenAsync(fullPath);
        _ = RecordOpenAsync(fullPath, configuration[CodingModule.WorkspaceKeyKey] ?? "digitalbrain");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        watcher.Stop();
        return Task.CompletedTask;
    }

    // The grain is the durable record of "which solution is open"; the load itself already runs in the service,
    // so this open is a no-op for the service and records the path, generation and map in grain state.
    private async Task RecordOpenAsync(string fullPath, string key)
    {
        try
        {
            await workspace.WhenReadyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (WorkspaceNotReadyException)
        {
            return;
        }

        var grain = grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, key).ToGrainId());
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await grain.Open(new OpenWorkspace(CommandId.New(), fullPath)).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException && attempt < 29)
            {
                // The silo is still coming up; a grain call before membership settles is retried.
                logger.LogDebug(error, "Recording the open of {SolutionPath} on {Key} failed; retrying.", fullPath, key);
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
    }
}
```

(`using DigitalBrain.Abstractions.Commands; using DigitalBrain.Abstractions.Identity;`.) The broad catch in the
retry loop is a `when` filter, which this repo's analyzers accept (phase 0 NOTES).

- [ ] **Step 5: The durable map on the neuron**

`WorkspaceState` gains `[property: Id(3)] SolutionMap? LastMap = null`. In `WorkspaceNeuron`:

- The `WorkspaceOpening` and `WorkspaceReloading` reactions, after `SaveAsync`, `Schedule(Signal.Create(CodingVocabulary.WorkspaceMapping, "{}"))`.
- A `WorkspaceMapping` reaction:

```csharp
            case CodingVocabulary.WorkspaceMapping:
                {
                    if (State is not { } current)
                    {
                        return;
                    }

                    switch (workspace.Status.Phase)
                    {
                        case WorkspacePhase.Ready:
                            var map = await workspace.MapAsync(new MapQuery(), cancellationToken).ConfigureAwait(true);
                            await SaveAsync(current with { LastMap = map }, cancellationToken).ConfigureAwait(true);
                            break;
                        case WorkspacePhase.Opening:
                            // One bounded wait per reaction keeps reads flowing; the next reaction looks again.
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                            Schedule(Signal.Create(CodingVocabulary.WorkspaceMapping, "{}"));
                            break;
                        default:
                            break;
                    }

                    break;
                }
```

- `Map()` answers from the cache when the service is not ready:

```csharp
    [ReadOnly]
    public async Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default)
    {
        if (workspace.Status.Phase == WorkspacePhase.Ready)
        {
            return await workspace.MapAsync(query, cancellationToken).ConfigureAwait(true);
        }

        return State?.LastMap ?? throw new WorkspaceNotReadyException(workspace.Status);
    }
```

- [ ] **Step 6: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.SolutionFileWatcherFacts`, `CodeWorkspaceNeuronFacts`, `ChangeSetNeuronFacts`, `SolutionWorkspaceFacts`.
Expected: all PASS. If the fold fact times out, print `workspace.Generation` and the watcher's pending keys:
the usual cause is the path form (`\` vs `/`) — `FoldAsync` must try both. If `The_warmup_opens_the_grain...`
never reaches generation 1, the `Open` retry loop is swallowing a real rejection: log it at Warning and read the log.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: file watcher folds saves, project changes flag a reload, the map is cached durably"
```

---

### Task 7: `dotnet` and `git` through one process seam

**Files:**
- Create: `src/Modules/Coding/Coding/IProcessRunner.cs`, `ProcessResult.cs`, `ProcessRunner.cs`, `DotnetRunner.cs`, `BuildOutcome.cs`,
  `TestOutcome.cs`, `TestFailure.cs`, `GitRunner.cs`, `GitCommitOutcome.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/FakeProcessRunner.cs`, `RunnerFacts.cs`
- Modify: `CodingModule.cs` (registrations), `tests/DigitalBrain.Tests/Features/Coding/DiskFixture.cs` (`InitGit`),
  `CodingSelfTestFacts.cs` (gated runner fact), `.gitignore` (`artifacts/`, if absent)

**Interfaces:**
- Produces:
  - `public interface IProcessRunner { Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken); }`
  - `public sealed record ProcessResult(int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut)`
  - `public sealed class DotnetRunner(IProcessRunner processes)` with `Task<BuildOutcome> BuildAsync(string solutionPath, string? artifactsPath, CancellationToken)`
    and `Task<TestOutcome> TestAsync(string projectOrSolutionPath, string? filterClass, string? artifactsPath, CancellationToken)`
  - `public sealed record BuildOutcome(bool Succeeded, IReadOnlyList<DiagnosticHit> Errors, int WarningCount, double DurationSeconds, string Command, string? Detail)`
  - `public sealed record TestOutcome(bool Succeeded, int Total, int Passed, int Failed, int Skipped, IReadOnlyList<TestFailure> Failures, double DurationSeconds, string Command, string? Detail)`
  - `public sealed record TestFailure(string Name, string Message)`
  - `public sealed class GitRunner(IProcessRunner processes)` with `Task<IReadOnlyList<string>> ChangedPathsAsync(string repository, CancellationToken)`,
    `Task<string> CurrentBranchAsync(string repository, CancellationToken)`, `Task<string> EnsureBranchAsync(string repository, string branch, CancellationToken)`,
    `Task<GitCommitOutcome> CommitAsync(string repository, IReadOnlyList<string> files, string message, CancellationToken)`
  - `public sealed record GitCommitOutcome(string Hash, string Branch, IReadOnlyList<string> Files)`
  - `DiskFixture.InitGit()` (a repository with one initial commit at `Root`) and `FakeProcessRunner`.

- [ ] **Step 1: Write the failing runner facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/FakeProcessRunner.cs
using DigitalBrain.Coding;

namespace DigitalBrain.Tests.Coding;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Calls { get; } = [];

    public void Enqueue(int exitCode, string output, string error = "")
        => _results.Enqueue(new ProcessResult(exitCode, output, error, TimeSpan.FromSeconds(1), TimedOut: false));

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments, workingDirectory));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new ProcessResult(0, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false));
    }
}
```

```csharp
// tests/DigitalBrain.Tests/Features/Coding/RunnerFacts.cs
using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class RunnerFacts
{
    private const string BuildOutput = """
          Determining projects to restore...
        E:\repo\src\A\Thing.cs(12,9): error CS0103: The name 'x' does not exist in the current context [E:\repo\src\A\A.csproj]
        E:\repo\src\A\Thing.cs(12,9): error CS0103: The name 'x' does not exist in the current context [E:\repo\src\A\A.csproj]
        E:\repo\src\A\Other.cs(3,1): warning CS8019: Unnecessary using directive. [E:\repo\src\A\A.csproj]
        E:\repo\src\A\A.csproj : error NU1101: Unable to find package Missing. [E:\repo\src\A\A.csproj]

        Build FAILED.
        """;

    private const string TestOutput = """
        Running tests from E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64)
        failed DigitalBrain.Tests.Coding.RunnerFacts.Nope (12ms)
          Assert.Equal() Failure: Values differ
        E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64) failed [+327/x1/?5] (1m 37s)

        Test run summary: Failed!
          total: 333
          failed: 1
          succeeded: 327
          skipped: 5
          duration: 1m 37s 446ms
        """;

    [Fact]
    public async Task Build_errors_are_parsed_once_each_with_path_line_and_id()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, BuildOutput);
        var outcome = await new DotnetRunner(processes).BuildAsync("E:/repo/Repo.slnx", "E:/repo/artifacts/slot-b", TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Errors.Count);
        Assert.Equal(("CS0103", @"E:\repo\src\A\Thing.cs", 12), (outcome.Errors[0].Id, outcome.Errors[0].Path, outcome.Errors[0].Line));
        Assert.Equal(("NU1101", @"E:\repo\src\A\A.csproj", 0), (outcome.Errors[1].Id, outcome.Errors[1].Path, outcome.Errors[1].Line));
        Assert.Equal(1, outcome.WarningCount);
        var call = Assert.Single(processes.Calls);
        Assert.Equal("dotnet", call.FileName);
        Assert.Equal(["build", "E:/repo/Repo.slnx", "-c", "Release", "--nologo", "-p:ArtifactsPath=E:/repo/artifacts/slot-b"], call.Arguments);
        Assert.Equal("E:/repo", call.WorkingDirectory.Replace('\\', '/'));
    }

    [Fact]
    public async Task Test_results_are_parsed_with_the_failing_test_named()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, TestOutput);
        var outcome = await new DotnetRunner(processes).TestAsync("E:/repo/tests/Tests.csproj", "DigitalBrain.Tests.Coding.RunnerFacts", null, TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded);
        Assert.Equal((333, 327, 1, 5), (outcome.Total, outcome.Passed, outcome.Failed, outcome.Skipped));
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal("DigitalBrain.Tests.Coding.RunnerFacts.Nope", failure.Name);
        Assert.Contains("Values differ", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["test", "E:/repo/tests/Tests.csproj", "-c", "Release", "--no-build", "--", "--filter-class", "DigitalBrain.Tests.Coding.RunnerFacts"], processes.Calls[0].Arguments);
    }

    [Fact]
    public async Task A_timed_out_process_is_a_failed_outcome_with_advice()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(-1, string.Empty);
        processes.Calls.Clear();
        var timedOut = new TimedOutProcessRunner();
        var outcome = await new DotnetRunner(timedOut).BuildAsync("E:/repo/Repo.slnx", null, TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded);
        Assert.Contains("timed out", outcome.Detail, StringComparison.Ordinal);
    }

    private sealed class TimedOutProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, timeout, TimedOut: true));
    }

    [Fact]
    public async Task Git_commits_the_change_set_files_on_a_coding_branch()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync();
        var git = new GitRunner(new ProcessRunner());
        await File.WriteAllTextAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource.Replace("Hello", "Hi", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var branch = await git.EnsureBranchAsync(fixture.Root, "coding/c1", TestContext.Current.CancellationToken);
        var outcome = await git.CommitAsync(fixture.Root, [fixture.GreeterPath], "coding: friendlier greeting", TestContext.Current.CancellationToken);

        Assert.Equal("coding/c1", branch);
        Assert.Equal("coding/c1", outcome.Branch);
        Assert.Equal(40, outcome.Hash.Length);
        Assert.Equal(["Alpha/Greeter.cs"], outcome.Files);
        Assert.Equal("coding/c1", await git.CurrentBranchAsync(fixture.Root, TestContext.Current.CancellationToken));
        Assert.Empty(await git.ChangedPathsAsync(fixture.Root, TestContext.Current.CancellationToken));
        Assert.Equal("coding/c1", await git.EnsureBranchAsync(fixture.Root, "coding/c1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Git_refuses_when_the_tree_is_dirty_outside_the_change_set()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync();
        var git = new GitRunner(new ProcessRunner());
        await File.WriteAllTextAsync(fixture.GreeterPath, "namespace Alpha;", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(fixture.ProgramPath, "namespace Beta;", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => git.CommitAsync(fixture.Root, [fixture.GreeterPath], "coding: partial", TestContext.Current.CancellationToken));
        Assert.Contains("Beta/Program.cs", error.Message, StringComparison.Ordinal);
        Assert.Contains("outside the change set", error.Message, StringComparison.Ordinal);
    }
}
```

`DiskFixture` gains:

```csharp
    // A repository with the fixture files committed, for the git facts and the chat scenario.
    public async Task InitGitAsync()
    {
        var git = new ProcessRunner();
        foreach (var arguments in new[]
        {
            new[] { "init", "-q", "-b", "main" },
            new[] { "config", "user.email", "coding@digitalbrain.test" },
            new[] { "config", "user.name", "Coding fixture" },
            new[] { "config", "commit.gpgsign", "false" },
            new[] { "add", "-A" },
            new[] { "commit", "-q", "-m", "fixture" },
        })
        {
            var result = await git.RunAsync("git", arguments, Root, TimeSpan.FromSeconds(30), CancellationToken.None);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {arguments[0]} failed: {result.Error}");
            }
        }
    }
```

(`using DigitalBrain.Coding;` in the fixture file.)

- [ ] **Step 2: Run to verify they fail**

Run: `-- --filter-class DigitalBrain.Tests.Coding.RunnerFacts`
Expected: compile errors for the runner types.

- [ ] **Step 3: Write the process seam**

```csharp
// src/Modules/Coding/Coding/IProcessRunner.cs
namespace DigitalBrain.Coding;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}
```

```csharp
// src/Modules/Coding/Coding/ProcessResult.cs
namespace DigitalBrain.Coding;

public sealed record ProcessResult(int ExitCode, string Output, string Error, TimeSpan Duration, bool TimedOut);
```

```csharp
// src/Modules/Coding/Coding/ProcessRunner.cs
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DigitalBrain.Coding;

public sealed class ProcessRunner : IProcessRunner
{
    private const int MaximumOutputCharacters = 1024 * 1024;

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // MSBuild node reuse and the terminal logger would keep processes and colour codes around.
        start.Environment["MSBUILDNODEREUSE"] = "0";
        start.Environment["MSBUILDTERMINALLOGGER"] = "off";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = start };
        process.Start();
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), clock.Elapsed, TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            return new ProcessResult(-1, string.Empty, string.Empty, clock.Elapsed, TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (text.Length < MaximumOutputCharacters)
            {
                text.Append(buffer, 0, Math.Min(read, MaximumOutputCharacters - text.Length));
            }
        }

        return text.ToString();
    }
}
```

- [ ] **Step 4: Write the dotnet runner**

```csharp
// src/Modules/Coding/Coding/BuildOutcome.cs
namespace DigitalBrain.Coding;

public sealed record BuildOutcome(bool Succeeded, IReadOnlyList<DiagnosticHit> Errors, int WarningCount, double DurationSeconds, string Command, string? Detail);
```

```csharp
// src/Modules/Coding/Coding/TestFailure.cs
namespace DigitalBrain.Coding;

public sealed record TestFailure(string Name, string Message);
```

```csharp
// src/Modules/Coding/Coding/TestOutcome.cs
namespace DigitalBrain.Coding;

public sealed record TestOutcome(bool Succeeded, int Total, int Passed, int Failed, int Skipped, IReadOnlyList<TestFailure> Failures, double DurationSeconds, string Command, string? Detail);
```

```csharp
// src/Modules/Coding/Coding/DotnetRunner.cs
using System.Globalization;
using System.Text.RegularExpressions;

namespace DigitalBrain.Coding;

public sealed partial class DotnetRunner(IProcessRunner processes)
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(20);

    public async Task<BuildOutcome> BuildAsync(string solutionPath, string? artifactsPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var arguments = new List<string> { "build", solutionPath, "-c", "Release", "--nologo" };
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            arguments.Add("-p:ArtifactsPath=" + artifactsPath);
        }

        var result = await processes.RunAsync("dotnet", arguments, Path.GetDirectoryName(Path.GetFullPath(solutionPath))!, BuildTimeout, cancellationToken).ConfigureAwait(false);
        var hits = ParseDiagnostics(result.Output + "\n" + result.Error);
        var errors = hits.Where(static hit => hit.Severity == "Error").ToArray();
        return new BuildOutcome(
            result.ExitCode == 0 && !result.TimedOut,
            errors,
            hits.Count(static hit => hit.Severity == "Warning"),
            result.Duration.TotalSeconds,
            "dotnet " + string.Join(' ', arguments),
            Detail(result, errors.Length == 0 && result.ExitCode != 0 ? "the build failed without a parsable error; see the output" : null));
    }

    public async Task<TestOutcome> TestAsync(string projectOrSolutionPath, string? filterClass, string? artifactsPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectOrSolutionPath);
        var arguments = new List<string> { "test", projectOrSolutionPath, "-c", "Release", "--no-build" };
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            arguments.Add("-p:ArtifactsPath=" + artifactsPath);
        }

        if (!string.IsNullOrWhiteSpace(filterClass))
        {
            arguments.AddRange(["--", "--filter-class", filterClass]);
        }

        var result = await processes.RunAsync("dotnet", arguments, Path.GetDirectoryName(Path.GetFullPath(projectOrSolutionPath))!, TestTimeout, cancellationToken).ConfigureAwait(false);
        var text = result.Output + "\n" + result.Error;
        var total = Count(text, "total");
        var failed = Count(text, "failed");
        var passed = Count(text, "succeeded");
        var skipped = Count(text, "skipped");
        var failures = FailedTestPattern().Matches(text)
            .Select(match => new TestFailure(match.Groups["name"].Value, match.Groups["message"].Value.Trim()))
            .ToArray();
        return new TestOutcome(
            result.ExitCode == 0 && !result.TimedOut && failed == 0,
            total, passed, failed, skipped, failures, result.Duration.TotalSeconds,
            "dotnet " + string.Join(' ', arguments),
            Detail(result, total == 0 ? "no test summary was found in the output" : null));
    }

    private static string? Detail(ProcessResult result, string? fallback)
        => result.TimedOut ? "the process timed out" : fallback;

    private static int Count(string text, string label)
    {
        var match = SummaryPattern().Matches(text).LastOrDefault(match => match.Groups["label"].Value == label);
        return match is null ? 0 : int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<DiagnosticHit> ParseDiagnostics(string text)
        => DiagnosticPattern().Matches(text)
            .Select(match => new DiagnosticHit(
                match.Groups["id"].Value,
                match.Groups["severity"].Value == "error" ? "Error" : "Warning",
                match.Groups["message"].Value.Trim(),
                match.Groups["path"].Value.Trim(),
                match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture) : 0))
            .DistinctBy(static hit => (hit.Id, hit.Path, hit.Line, hit.Message))
            .ToArray();

    // "E:\repo\src\A\Thing.cs(12,9): error CS0103: message [E:\repo\src\A\A.csproj]" and the project-level
    // "E:\repo\src\A\A.csproj : error NU1101: message [..]" form without a position.
    [GeneratedRegex(@"^\s*(?<path>[^\r\n(]+?)(?:\((?<line>\d+),\d+\))?\s*:\s*(?<severity>error|warning)\s+(?<id>[A-Z]+\d+):\s*(?<message>.*?)(?:\s\[[^\]]*\])?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"^\s*(?<label>total|failed|succeeded|skipped):\s*(?<count>\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPattern();

    // "failed Namespace.Class.Test (12ms)" followed by the indented failure message.
    [GeneratedRegex(@"^failed (?<name>\S+) \([^)]*\)\r?\n(?<message>(?:[ \t]+.*\r?\n?)*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FailedTestPattern();
}
```

- [ ] **Step 5: Write the git runner**

```csharp
// src/Modules/Coding/Coding/GitCommitOutcome.cs
namespace DigitalBrain.Coding;

public sealed record GitCommitOutcome(string Hash, string Branch, IReadOnlyList<string> Files);
```

```csharp
// src/Modules/Coding/Coding/GitRunner.cs
namespace DigitalBrain.Coding;

public sealed class GitRunner(IProcessRunner processes)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<string>> ChangedPathsAsync(string repository, CancellationToken cancellationToken)
    {
        var status = await GitAsync(repository, ["status", "--porcelain", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        return status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd('\r'))
            .Where(static line => line.Length > 3)
            .Select(static line => line[3..].Split(" -> ")[^1].Trim('"'))
            .ToArray();
    }

    public async Task<string> CurrentBranchAsync(string repository, CancellationToken cancellationToken)
        => (await GitAsync(repository, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false)).Output.Trim();

    public async Task<string> EnsureBranchAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        var exists = await processes.RunAsync("git", ["rev-parse", "--verify", "--quiet", "refs/heads/" + branch], repository, Timeout, cancellationToken).ConfigureAwait(false);
        await GitAsync(repository, exists.ExitCode == 0 ? ["checkout", "-q", branch] : ["checkout", "-q", "-b", branch], cancellationToken).ConfigureAwait(false);
        return branch;
    }

    // Refuses when the tree carries changes outside the change set: a commit must contain exactly what the
    // snapshot wrote, never a stray edit the owner had not saved on purpose.
    public async Task<GitCommitOutcome> CommitAsync(string repository, IReadOnlyList<string> files, string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var relative = files.Select(file => Path.GetRelativePath(repository, file).Replace('\\', '/')).ToArray();
        var outside = (await ChangedPathsAsync(repository, cancellationToken).ConfigureAwait(false))
            .Where(path => !relative.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (outside.Length > 0)
        {
            throw new InvalidOperationException($"The working tree has changes outside the change set: {string.Join(", ", outside.Take(5))}. Commit or stash them first.");
        }

        await GitAsync(repository, ["add", "--", .. relative], cancellationToken).ConfigureAwait(false);
        await GitAsync(repository, ["commit", "-q", "-m", message], cancellationToken).ConfigureAwait(false);
        var hash = (await GitAsync(repository, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Output.Trim();
        return new GitCommitOutcome(hash, await CurrentBranchAsync(repository, cancellationToken).ConfigureAwait(false), relative);
    }

    private async Task<ProcessResult> GitAsync(string repository, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", ["--no-pager", "-c", "core.fsmonitor=false", .. arguments], repository, Timeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments[0]} failed: {(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim()}");
        }

        return result;
    }
}
```

`CodingModule.Configure` registers `TryAddSingleton<IProcessRunner, ProcessRunner>()`, `TryAddSingleton<DotnetRunner>()`, `TryAddSingleton<GitRunner>()`.

- [ ] **Step 6: The gated runner fact**

Append to `CodingSelfTestFacts`:

```csharp
    [Fact(Skip = Skip, SkipUnless = nameof(SelfTestsEnabled))]
    public async Task The_real_solution_builds_and_one_class_tests_through_the_runner()
    {
        var artifacts = Path.Combine(Path.GetDirectoryName(SolutionPath)!, "artifacts", "self-test");
        var runner = new DotnetRunner(new ProcessRunner());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

        var build = await runner.BuildAsync(SolutionPath, artifacts, timeout.Token);
        Assert.True(build.Succeeded, build.Detail ?? string.Join("; ", build.Errors.Select(error => $"{error.Path}:{error.Line} {error.Id}")));
        Assert.Empty(build.Errors);

        var tests = await runner.TestAsync(Path.Combine(Path.GetDirectoryName(SolutionPath)!, "tests", "DigitalBrain.Tests", "DigitalBrain.Tests.csproj"),
            "DigitalBrain.Tests.Coding.WorkspaceReadFacts", artifacts, timeout.Token);
        Assert.True(tests.Succeeded, tests.Detail ?? string.Join("; ", tests.Failures.Select(failure => failure.Name)));
        Assert.True(tests.Passed >= 7, $"passed {tests.Passed}");
    }
```

The build goes to `artifacts/self-test` (a separate output root, so the running test host's own assemblies
are not overwritten — the same mechanism phase 2 uses per slot). Add `artifacts/` to `.gitignore` if it is not there.

- [ ] **Step 7: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.RunnerFacts`, then the gated fact once:
`DIGITALBRAIN_CODING_SELF_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingSelfTestFacts`
(expect both facts to pass; the runner fact takes a few minutes: a full Release build into a fresh artifacts
root plus one class). Record the build and test durations from the outcomes in your report. Then the ungated
run shows 2 skipped.

- [ ] **Step 8: Commit**

```bash
git add .gitignore src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: dotnet build and test, git branch and commit through one process seam"
```

---

### Task 8: Nine more `code_*` tools

**Files:**
- Modify: `src/Modules/Coding/Coding/CodingNativeTools.cs`, `CodingModule.cs`, `src/Modules/AI/AI/ConversationalAgent.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs`

**Interfaces:**
- Consumes: everything above; `IGrainFactory` for the `changeset` grain.
- Produces: tools `code_skeleton`, `code_member`, `code_callers`, `code_implementations`, `code_propose_edit`, `code_check`,
  `code_commit`, `code_build`, `code_test`; `CodingNativeTools(SolutionWorkspace workspace, IGrainFactory grains, DotnetRunner dotnet, GitRunner git, IConfiguration configuration)`;
  configuration key `CodingModule.TestProjectKey = "DigitalBrain:Coding:TestProject"` (default: the solution path).

- [ ] **Step 1: Write the failing tool facts**

Change `CodingNativeToolFacts.StartAsync` to use a `DiskFixture` with git, a fake `DotnetRunner`, and to return the fixture:

```csharp
    private static async Task<(BrainSimulation Brain, NativeTools Tools, DiskFixture Fixture, FakeProcessRunner Dotnet)> StartAsync()
    {
        var fixture = DiskFixture.Create();
        await fixture.InitGitAsync();
        var dotnet = new FakeProcessRunner();
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(fixture.Open));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
                silo.Services.AddSingleton(new DotnetRunner(dotnet));
            },
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = fixture.SolutionPath },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        return (brain, brain.SiloServices.GetRequiredService<NativeTools>(), fixture, dotnet);
    }
```

Update the existing five facts to the new tuple (they ignore the fixture; `Map_is_a_graph_result_the_shell_can_open`
now sees 2 nodes with clusters `Alpha`/`Beta` under the temp root — its assertions on `kind` and edge hold),
and append:

```csharp
    [Fact]
    public async Task The_thirteen_tools_resolve()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        Assert.Equal(13, tools.Resolve(["code_find_symbols", "code_references", "code_diagnostics", "code_map", "code_skeleton", "code_member", "code_callers",
            "code_implementations", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test"]).Count());
    }

    [Fact]
    public async Task Skeleton_member_callers_and_implementations_answer_from_the_snapshot()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var skeleton = await InvokeAsync(tools, "code_skeleton", new() { ["path"] = fixture.GreeterPath });
        Assert.Equal(3, skeleton.GetProperty("members").GetArrayLength());
        var member = await InvokeAsync(tools, "code_member", new() { ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)" });
        Assert.Contains("Hello", member.GetProperty("source").GetString(), StringComparison.Ordinal);
        var callers = await InvokeAsync(tools, "code_callers", new() { ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)" });
        Assert.Equal("M:Beta.Program.Run", callers.GetProperty("items")[0].GetProperty("id").GetString());
        var implementations = await InvokeAsync(tools, "code_implementations", new() { ["symbolId"] = "T:Alpha.IWelcome" });
        Assert.Equal(1, implementations.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Propose_check_and_commit_land_a_change_on_a_coding_branch()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var proposed = await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t1",
            ["kind"] = "Rename",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["newName"] = "Hello",
        });
        Assert.Equal("Draft", proposed.GetProperty("status").GetString());
        Assert.Equal(1, proposed.GetProperty("edits").GetArrayLength());

        var checkedResult = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t1" });
        Assert.Equal("Checked", checkedResult.GetProperty("status").GetString());
        Assert.Contains("+", checkedResult.GetProperty("diff").GetString(), StringComparison.Ordinal);

        var committed = await InvokeAsync(tools, "code_commit", new() { ["changeId"] = "t1", ["message"] = "rename Greet to Hello" });
        Assert.Equal("Committed", committed.GetProperty("status").GetString());
        Assert.Equal("coding/t1", committed.GetProperty("branch").GetString());
        Assert.Equal(40, committed.GetProperty("commit").GetString()!.Length);
        Assert.Equal(2, committed.GetProperty("files").GetArrayLength());
        Assert.Contains(""".Hello("world")""", await File.ReadAllTextAsync(fixture.ProgramPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var log = await new ProcessRunner().RunAsync("git", ["log", "-1", "--format=%s"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("coding: rename Greet to Hello", log.Output.Trim());
    }

    [Fact]
    public async Task A_check_that_fails_reports_the_edit_and_the_diagnostics()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t2",
            ["kind"] = "ReplaceMember",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["source"] = "public string Greet(string name) => 42;",
        });
        var checkedResult = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t2" });
        Assert.Equal("Draft", checkedResult.GetProperty("status").GetString());
        Assert.StartsWith("edit 1", checkedResult.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("CS0029", checkedResult.GetProperty("diagnostics")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_unknown_edit_kind_is_advice()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var result = await InvokeAsync(tools, "code_propose_edit", new() { ["changeId"] = "t3", ["kind"] = "Explode" });
        Assert.Contains("ReplaceMember", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_and_test_return_the_parsed_outcomes()
    {
        var (brain, tools, fixture, dotnet) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        dotnet.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        var build = await InvokeAsync(tools, "code_build", new());
        Assert.True(build.GetProperty("succeeded").GetBoolean());
        Assert.Contains(fixture.SolutionPath.Replace('\\', '/'), build.GetProperty("command").GetString()!.Replace('\\', '/'), StringComparison.Ordinal);

        dotnet.Enqueue(0, "Test run summary: Passed!\n  total: 3\n  failed: 0\n  succeeded: 3\n  skipped: 0\n");
        var tests = await InvokeAsync(tools, "code_test", new() { ["filterClass"] = "Some.Class" });
        Assert.True(tests.GetProperty("succeeded").GetBoolean());
        Assert.Equal(3, tests.GetProperty("passed").GetInt32());
        Assert.Contains("--filter-class Some.Class", tests.GetProperty("command").GetString(), StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `-- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts`
Expected: `The_thirteen_tools_resolve` fails with 4 resolved.

- [ ] **Step 3: Write the tools**

`CodingNativeTools` becomes `public sealed class CodingNativeTools(SolutionWorkspace workspace, IGrainFactory grains, DotnetRunner dotnet, GitRunner git, IConfiguration configuration)`
and its function list grows with these local functions and registrations (descriptions verbatim):

```csharp
        Task<JsonElement> Skeleton([Description("Full path of one source file")] string path, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.SkeletonAsync(new SkeletonQuery(path), cancellationToken));

        Task<JsonElement> Member([Description("A symbol id from code_find_symbols or code_skeleton")] string symbolId, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.MemberAsync(new MemberQuery(symbolId), cancellationToken));

        Task<JsonElement> Callers([Description("A symbol id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.CallersAsync(new CallersQuery(symbolId, limit), cancellationToken));

        Task<JsonElement> Implementations([Description("An interface, abstract member or virtual member id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.ImplementationsAsync(new ImplementationsQuery(symbolId, limit), cancellationToken));

        Task<JsonElement> ProposeEdit(
            [Description("The change set id; edits with the same id compose into one snapshot")] string changeId,
            [Description("ReplaceMember | InsertMember | AddUsing | ReplaceRange | Rename | ApplyCodeFix")] string kind,
            [Description("Symbol id (ReplaceMember, InsertMember, Rename)")] string? symbolId = null,
            [Description("File path (AddUsing, ReplaceRange, ApplyCodeFix)")] string? path = null,
            [Description("One complete member declaration, or the replacement text for a range")] string? source = null,
            [Description("The new name (Rename)")] string? newName = null,
            [Description("First line, 1-based (ReplaceRange; optional for ApplyCodeFix)")] int? startLine = null,
            [Description("Last line, 1-based (ReplaceRange)")] int? endLine = null,
            [Description("Namespace to import (AddUsing)")] string? @namespace = null,
            [Description("Diagnostic id such as CS0219 (ApplyCodeFix)")] string? diagnosticId = null,
            [Description("Title of the fix to apply when several exist (ApplyCodeFix)")] string? fixTitle = null,
            CancellationToken cancellationToken = default)
            => ProposeAsync(changeId, kind, new EditRequest(default, symbolId, path, source, newName, startLine, endLine, @namespace, diagnosticId, fixTitle), cancellationToken);

        Task<JsonElement> Check([Description("The change set id")] string changeId, CancellationToken cancellationToken = default)
            => CheckAsync(changeId, cancellationToken);

        Task<JsonElement> Commit([Description("The change set id")] string changeId, [Description("One line saying what the change does; the commit message gets the coding: prefix")] string message, CancellationToken cancellationToken = default)
            => CommitAsync(changeId, message, cancellationToken);

        Task<JsonElement> Build([Description("Output root for bin and obj, or empty for the default")] string? artifactsPath = null, CancellationToken cancellationToken = default)
            => GuardedAsync(() => dotnet.BuildAsync(SolutionPath(), artifactsPath, cancellationToken));

        Task<JsonElement> Test([Description("A test class full name to run only that class, or empty for everything")] string? filterClass = null, [Description("The artifacts root the build used, or empty")] string? artifactsPath = null, CancellationToken cancellationToken = default)
            => GuardedAsync(() => dotnet.TestAsync(configuration[CodingModule.TestProjectKey] ?? SolutionPath(), filterClass, artifactsPath, cancellationToken));
```

```csharp
            AIFunctionFactory.Create(Skeleton, new AIFunctionFactoryOptions { Name = "code_skeleton", Description = "The types and member signatures of one file, without bodies, with symbol ids." }),
            AIFunctionFactory.Create(Member, new AIFunctionFactoryOptions { Name = "code_member", Description = "One declaration with its body, by symbol id." }),
            AIFunctionFactory.Create(Callers, new AIFunctionFactoryOptions { Name = "code_callers", Description = "The symbols that call a method or read a property, with the call sites." }),
            AIFunctionFactory.Create(Implementations, new AIFunctionFactoryOptions { Name = "code_implementations", Description = "The implementations of an interface or an abstract or virtual member." }),
            AIFunctionFactory.Create(ProposeEdit, new AIFunctionFactoryOptions { Name = "code_propose_edit", Description = "Add one edit to a change set. Nothing touches disk until code_commit; code_check compiles the snapshot first." }),
            AIFunctionFactory.Create(Check, new AIFunctionFactoryOptions { Name = "code_check", Description = "Apply a change set to one snapshot and compile it: diagnostics and a diff, no files written." }),
            AIFunctionFactory.Create(Commit, new AIFunctionFactoryOptions { Name = "code_commit", Description = "Write a clean change set to disk and commit it on a coding/<changeId> git branch. Refuses when the check has errors or the tree is dirty elsewhere." }),
            AIFunctionFactory.Create(Build, new AIFunctionFactoryOptions { Name = "code_build", Description = "dotnet build of the solution in Release; parsed errors and warnings." }),
            AIFunctionFactory.Create(Test, new AIFunctionFactoryOptions { Name = "code_test", Description = "dotnet test without rebuilding; counts and the failing tests. Run code_build first." }),
```

The private helpers:

```csharp
    private static readonly TimeSpan ReactionWait = TimeSpan.FromMinutes(2);

    private IChangeSet ChangeSet(string changeId)
        => grains.GetGrain<IChangeSet>(new NeuronId(CodingVocabulary.ChangeSetType, changeId).ToGrainId());

    private string SolutionPath()
        => workspace.Status.SolutionPath ?? throw new InvalidOperationException(workspace.Status.Advice);

    private Task<JsonElement> ProposeAsync(string changeId, string kind, EditRequest edit, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            if (!Enum.TryParse<EditKind>(kind, ignoreCase: true, out var parsed))
            {
                throw new InvalidOperationException($"'{kind}' is not an edit kind. Use one of: {string.Join(", ", Enum.GetNames<EditKind>())}.");
            }

            var changeSet = ChangeSet(changeId);
            var accepted = await changeSet.Propose(new ProposeEdit(CommandId.New(), edit with { Kind = parsed })).ConfigureAwait(false);
            return await WaitAsync(changeSet, snapshot => snapshot.Edits.Count >= accepted.Receipt.EditCount, cancellationToken).ConfigureAwait(false);
        });

    private Task<JsonElement> CheckAsync(string changeId, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            var changeSet = ChangeSet(changeId);
            await changeSet.Check(new CheckChangeSet(CommandId.New())).ConfigureAwait(false);
            return await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Checked || snapshot.Detail is not null, cancellationToken).ConfigureAwait(false);
        });

    private Task<JsonElement> CommitAsync(string changeId, string message, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            var changeSet = ChangeSet(changeId);
            await changeSet.Commit(new CommitChangeSet(CommandId.New(), message)).ConfigureAwait(false);
            var snapshot = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Committed || snapshot.Detail is not null, cancellationToken).ConfigureAwait(false);
            if (snapshot.Status != ChangeSetStatus.Committed)
            {
                return (object)snapshot;
            }

            var repository = Path.GetDirectoryName(SolutionPath())!;
            var branch = await git.EnsureBranchAsync(repository, "coding/" + changeId, cancellationToken).ConfigureAwait(false);
            var committed = await git.CommitAsync(repository, snapshot.Files, "coding: " + message, cancellationToken).ConfigureAwait(false);
            return new { status = snapshot.Status.ToString(), files = snapshot.Files, generation = snapshot.Generation, branch, commit = committed.Hash, diff = snapshot.Diff };
        });

    // Commands return at once; the reaction that does the work saves a new snapshot, which is what the model needs.
    private static async Task<ChangeSetSnapshot> WaitAsync(IChangeSet changeSet, Func<ChangeSetSnapshot, bool> done, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReactionWait);
        while (true)
        {
            var snapshot = await changeSet.Read().ConfigureAwait(false);
            if (done(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }
    }
```

`GuardedAsync<T>` already serializes any result; the `Committed` branch returns an anonymous object, so make
`GuardedAsync` accept `Func<Task<object>>` (or add an overload) — the map tool already goes through it with an
anonymous object. The snapshot's enum serializes as a string because the shared `Json` options add
`JsonStringEnumConverter` (add `Converters = { new JsonStringEnumConverter() }` to the static options if
`status` comes out as a number; the fact pins `"Draft"`).

`CodingModule`: `TestProjectKey`, the nine names in the registration loop (one array of thirteen names).
`ConversationalAgent.cs`: append the nine names to the allowlist and one paragraph to the instructions next to
the existing code paragraph: "To change code, propose edits into one change set with code_propose_edit
(a member by symbol id, a rename, a using, a line range or a code fix), run code_check and fix what it
reports, then code_commit; after a commit run code_build and code_test and report the outcome. Never claim
a change landed without a Committed status and a commit hash."

- [ ] **Step 4: Run the facts**

Run: `-- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts`, then `ConversationalAgentFacts`.
Expected: all PASS. If `code_commit`'s git step fails with "changes outside the change set" on the fixture,
`InitGitAsync` did not commit everything (`git add -A` then commit) or the `.slnx` placeholder was written
after the commit; if the rename's changed files come back as two paths but `git status` reports three, the
formatter touched a third document — `Files` must be exactly the written paths.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Coding src/Modules/AI/AI/ConversationalAgent.cs tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: skeleton, member, callers, implementations, propose, check, commit, build and test tools"
```

---

### Task 9: The scripted rename, the docs and the phase gate

**Files:**
- Create: `tests/DigitalBrain.Tests/Features/Coding/CodingChatFacts.cs`
- Modify: `docs/coding/README.md`, `docs/coding/NOTES.md`, `docs/coding/STATUS.md`

**Interfaces:**
- Consumes: everything above, `ScriptedChatClient`, `ScriptedContentScreen`, `TableAgentFacts.RunAsync`.

- [ ] **Step 1: Write the scripted chat fact**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/CodingChatFacts.cs
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Coding;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// The design's phase 1 exit, scripted: the model drives propose, check, commit, build and test through the
// workspace agent, and the rename lands as a git commit on a coding/<id> branch.
public sealed class CodingChatFacts
{
    [Fact]
    public async Task Rename_X_to_Y_lands_as_a_commit_through_the_chat()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync();
        var dotnet = new FakeProcessRunner();
        dotnet.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        dotnet.Enqueue(0, "Test run summary: Passed!\n  total: 2\n  failed: 0\n  succeeded: 2\n  skipped: 0\n");
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(fixture.Open));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
                silo.Services.AddSingleton(new DotnetRunner(dotnet));
            },
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = fixture.SolutionPath },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);

        using var model = new ScriptedChatClient();
        model.CallTool("code_find_symbols", JsonSerializer.Serialize(new { query = "Greet" }));
        model.CallTool("code_propose_edit", JsonSerializer.Serialize(new { changeId = "rename-1", kind = "Rename", symbolId = "M:Alpha.Greeter.Greet(System.String)", newName = "Hello" }));
        model.CallTool("code_check", JsonSerializer.Serialize(new { changeId = "rename-1" }));
        model.CallTool("code_commit", JsonSerializer.Serialize(new { changeId = "rename-1", message = "rename Greet to Hello" }));
        model.CallTool("code_build", "{}");
        model.CallTool("code_test", "{}");
        model.Say("Renamed Greet to Hello on branch coding/rename-1; build and tests are green.");

        await using var app = await StartAgentAsync(brain, model);
        using var client = app.GetTestClient();
        var events = await TableAgentFacts.RunAsync(client, "Rename Greeter.Greet to Hello and run the tests");
        var results = events.Where(item => item.GetProperty("type").GetString() == "TOOL_CALL_RESULT")
            .Select(item => JsonSerializer.Deserialize<JsonElement>(item.GetProperty("content").GetString()!))
            .ToArray();

        Assert.Equal(6, results.Length);
        Assert.Contains(results[0].GetProperty("items").EnumerateArray(), hit => hit.GetProperty("id").GetString() == "M:Alpha.Greeter.Greet(System.String)");
        Assert.Equal("Draft", results[1].GetProperty("status").GetString());
        Assert.Equal("Checked", results[2].GetProperty("status").GetString());
        Assert.Equal("Committed", results[3].GetProperty("status").GetString());
        Assert.Equal("coding/rename-1", results[3].GetProperty("branch").GetString());
        Assert.True(results[4].GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, results[5].GetProperty("passed").GetInt32());
        Assert.Contains(""".Hello("world")""", await File.ReadAllTextAsync(fixture.ProgramPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var log = await new ProcessRunner().RunAsync("git", ["log", "-1", "--format=%s"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("coding: rename Greet to Hello", log.Output.Trim());
        Assert.Contains(events, item => item.GetProperty("type").GetString() == "TEXT_MESSAGE_CONTENT" && item.GetProperty("delta").GetString()!.Contains("coding/rename-1", StringComparison.Ordinal));
    }

    private static async Task<WebApplication> StartAgentAsync(BrainSimulation brain, IChatClient model)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<NativeTools>());
        builder.Services.AddSingleton(new ChatClientBuilder(model).UseFunctionInvocation().Build());
        builder.AddConversationalAgent();
        var app = builder.Build();
        app.MapConversationalAgent();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
```

`TableAgentFacts.RunAsync` and `AddConversationalAgent`/`MapConversationalAgent` are `internal` to the test and silo
assemblies; the test project already has `InternalsVisibleTo` from the silo (phase 0 used `BasicAuthGate`), and
`RunAsync` lives in the same assembly. If `TEXT_MESSAGE_CONTENT` arrives in several deltas, join them before the
last assertion.

- [ ] **Step 2: Run it**

Run: `-- --filter-class DigitalBrain.Tests.Coding.CodingChatFacts`
Expected: PASS. The tool results reach the shell as JSON strings through `TOOL_CALL_RESULT.content`; if the
function-invocation loop stops after the first tool because the scripted client returns an empty message
when its script is exhausted, the script order above is exactly the call order the model must make.

- [ ] **Step 3: Docs**

`docs/coding/README.md`: add the `changeset` neuron (five methods, the `EditRequest` kinds and the fields each
reads, the snapshot with `Detail` and `Files`), the five new workspace reads, the nine new tools with one line
each, the configuration keys `DigitalBrain:Coding:WorkspaceKey` (default `digitalbrain`) and
`DigitalBrain:Coding:TestProject` (default: the solution), the watcher's behaviour (`.cs` saves fold in, project
files flag `reloadNeeded` in the snapshot), the commit path (D10: snapshot applied, files written in order,
`coding/<changeId>` branch, refusal on a dirty tree outside the change set), the runners (`artifactsPath`),
the new fact classes and the second gated fact, and "What phase 2 adds".

`docs/coding/NOTES.md`: a `## Phase 1 spikes` section stating that design 9.1 lists none and naming the API
checks made instead (Context7 on 2026-09-14, listed in this plan's Global Constraints); a `## Phase 1 outcome`
section with the fact counts and the exit criterion; a `## Phase 1 deviations` list (fill from the execution
ledger the controller keeps); the live transcript the controller records; observations (code-fix discovery by
reflection, formatter annotations, reload-needed semantics).

- [ ] **Step 4: Gates (controller)**

The controller runs the full gate, the gated self-tests, the live exit check through `aspire run` ("rename
`TimerNeuron.Alarm` to `AlarmFor` and run the tests" — the private method `Alarm(long generation)` in
`src/Modules/Time/Time/TimerNeuron.cs`), records the transcript in NOTES, updates STATUS, and opens the PR.

- [ ] **Step 5: Commit**

```bash
git add tests/DigitalBrain.Tests/Features/Coding/CodingChatFacts.cs docs/coding/README.md docs/coding/NOTES.md
git commit -m "coding: the scripted rename lands through the chat; phase 1 docs"
```

## Self-review

- **Spec coverage (design 9.1).** Contract `IChangeSet` with `propose/check/commit/discard/read`, `ChangeSetSnapshot`,
  `ChangeSetStatus`, `EditRequest` with the six kinds and fields: Task 1. Workspace additions `skeleton`, `member`,
  `callers`, `implementations`, `derived`: Tasks 1-2; durable map cache in `WorkspaceState` and the watcher: Task 6.
  Services `ChangeSetEditor` (DocumentEditor, SyntaxGenerator-equivalent syntax factory, Renamer, CodeFixProviders
  from the Features package; check diagnoses changed projects and dependents): Tasks 3-4; `DotnetRunner` (build and
  test, parsed errors, failures and durations, `-p:ArtifactsPath`) and `GitRunner` (status, branch, commit; refuses on
  a dirty tree outside the change set): Task 7. Tools, all nine: Task 8. Tests: adhoc facts for every edit kind
  including a cross-project rename (Tasks 3-4); check refuses and names the edit (Tasks 3, 5); commit writes only
  the changed files and records the generation (Tasks 3, 5); the watcher folds an external edit (Task 6); a gated
  fact runs `DotnetRunner` on the real solution (Task 7); the scripted chat drives propose, check, commit, build,
  test (Task 9). Exit: the live check in Task 9 step 4. Deviations from the letter of 9.1: the snapshot carries
  `Detail` and `Files` beyond the five listed members (needed to name the responsible edit and the written files);
  `SyntaxGenerator` is not used because `SyntaxFactory.ParseMemberDeclaration` plus `DocumentEditor` cover the four
  member edits; the changeset's version for `ExpectedVersion` is its edit count.
- **Placeholders.** None: every step carries its code, command and expected output; the only "fill from the ledger"
  is the NOTES deviations list, which the controller owns.
- **Type consistency.** `EditRequest(Kind, SymbolId, Path, Source, NewName, StartLine, EndLine, Namespace, DiagnosticId, FixTitle)`
  is used positionally in Task 8's tool and by name in Tasks 3-5; `EditOutcome(Changed, Diagnostics, Diff, ChangedPaths, FailingEdit, Detail)`
  in Tasks 3-5; `CommitOutcome(WrittenPaths, Generation)` in Tasks 3 and 5; `ChangeSetSnapshot(Status, Edits, Diagnostics, Diff, Generation, Detail, Files)`
  in Tasks 1, 5, 8; `DotnetRunner.BuildAsync(solutionPath, artifactsPath, ct)` / `TestAsync(path, filterClass, artifactsPath, ct)`
  in Tasks 7-9; `GitRunner.EnsureBranchAsync`/`CommitAsync` in Tasks 7-8; `SolutionWorkspace.QueryAsync`, `CommitAsync(Func<Solution, CancellationToken, Task<Solution>>)`,
  `FoldAsync`, `MarkReloadNeeded`, `Opened` across Tasks 2-6; `DiskFixture.Create/InitGitAsync/Open/Root/SolutionPath/GreeterPath/ProgramPath/UnusedPath`
  across Tasks 3-9; `FixtureSolutions.Documents/Build/GreeterSource` across Tasks 2-7.
