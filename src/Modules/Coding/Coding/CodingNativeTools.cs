using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Coding;

public sealed class CodingNativeTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly SolutionWorkspace _workspace;
    private readonly IGrainFactory _grains;
    private readonly DotnetRunner _dotnet;
    private readonly GitRunner _git;
    private readonly IConfiguration _configuration;
    private readonly CodingToolOptions _options;
    private readonly Lazy<IReadOnlyList<AIFunction>> _functions;

    public CodingNativeTools(SolutionWorkspace workspace, IGrainFactory grains, DotnetRunner dotnet, GitRunner git, IConfiguration configuration, CodingToolOptions options)
    {
        _workspace = workspace;
        _grains = grains;
        _dotnet = dotnet;
        _git = git;
        _configuration = configuration;
        _options = options;
        _functions = new(Build);
    }

    public IReadOnlyList<AIFunction> Functions => _functions.Value;

    public AIFunction Named(string name) => Functions.Single(function => function.Name == name);

    private IReadOnlyList<AIFunction> Build() =>
    [
        AIFunctionFactory.Create(FindSymbols, new AIFunctionFactoryOptions
        {
            Name = "code_find_symbols",
            Description = "Find types and members by name in the loaded solution. Returns ids to use with code_references.",
        }),
        AIFunctionFactory.Create(References, new AIFunctionFactoryOptions
        {
            Name = "code_references",
            Description = "Every place a symbol is used, with file, line and the source line. Semantic, not text search.",
        }),
        AIFunctionFactory.Create(Diagnostics, new AIFunctionFactoryOptions
        {
            Name = "code_diagnostics",
            Description = "Compiler errors and warnings for a file, a project, or the whole solution, without running a build.",
        }),
        AIFunctionFactory.Create(Map, new AIFunctionFactoryOptions
        {
            Name = "code_map",
            Description = "A graph of every project in the solution and the references between them. "
                + "Use it whenever the person asks to see or map the solution, its projects, or their dependencies.",
        }),
        AIFunctionFactory.Create(Skeleton, new AIFunctionFactoryOptions { Name = "code_skeleton", Description = "The types and member signatures of one file, without bodies, with symbol ids." }),
        AIFunctionFactory.Create(Member, new AIFunctionFactoryOptions { Name = "code_member", Description = "One declaration with its body, by symbol id." }),
        AIFunctionFactory.Create(Callers, new AIFunctionFactoryOptions { Name = "code_callers", Description = "The symbols that call a method or read a property, with the call sites." }),
        AIFunctionFactory.Create(Implementations, new AIFunctionFactoryOptions { Name = "code_implementations", Description = "The implementations of an interface or an abstract or virtual member." }),
        AIFunctionFactory.Create(Derived, new AIFunctionFactoryOptions { Name = "code_derived", Description = "The classes derived from a class or the interfaces extending an interface." }),
        AIFunctionFactory.Create(ProposeEdit, new AIFunctionFactoryOptions { Name = "code_propose_edit", Description = "Add one edit to a change set. Nothing touches disk until code_commit; code_check compiles the snapshot first." }),
        AIFunctionFactory.Create(Check, new AIFunctionFactoryOptions { Name = "code_check", Description = "Apply a change set to one snapshot and compile it: diagnostics and a diff, no files written." }),
        AIFunctionFactory.Create(Commit, new AIFunctionFactoryOptions
        {
            Name = "code_commit",
            Description = "Write a clean change set to disk and commit it on a coding/<changeId> git branch. Refuses when the check has errors or the tree is dirty elsewhere. "
                + "The working tree stays on the coding/<changeId> branch afterwards; later change sets commit on top of it.",
        }),
        AIFunctionFactory.Create(BuildSolution, new AIFunctionFactoryOptions { Name = "code_build", Description = "dotnet build of the solution in Release; parsed errors and warnings." }),
        AIFunctionFactory.Create(Test, new AIFunctionFactoryOptions { Name = "code_test", Description = "dotnet test without rebuilding; counts and the failing tests. Run code_build first." }),
    ];

    private IChangeSet ChangeSet(string changeId)
        => _grains.GetGrain<IChangeSet>(new NeuronId(CodingVocabulary.ChangeSetType, changeId).ToGrainId());

    // The same grain WorkspaceWarmup records the open on, so a read that the grain can answer from its
    // durable state (the map, while a reload is in flight) is not refused by the live service instead.
    private ICodeWorkspace Workspace()
        => _grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType,
            _configuration[CodingModule.WorkspaceKeyKey] ?? "digitalbrain").ToGrainId());

    private string SolutionPath()
        => _workspace.Status.SolutionPath ?? throw new InvalidOperationException(_workspace.Status.Advice);

    // Build and test must name the same artifacts root even though TestProjectKey points TestAsync at the
    // test project's own directory: resolving here, once, against the solution directory before either
    // runner sees the path is what lets "artifacts/x" mean one folder for both code_build and code_test.
    private string? ResolveArtifactsPath(string? artifactsPath)
        => string.IsNullOrWhiteSpace(artifactsPath) ? artifactsPath : Path.GetFullPath(artifactsPath, Path.GetDirectoryName(SolutionPath())!);

    private Task<JsonElement> FindSymbols(
        [Description("Part of a symbol name, case-insensitive")] string query,
        [Description("Maximum hits, default 20")] int limit = 20,
        CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.FindSymbolsAsync(new SymbolSearch(query, limit), cancellationToken));

    private Task<JsonElement> References(
        [Description("A symbol id from code_find_symbols, such as T:DigitalBrain.Time.ITimer")] string symbolId,
        [Description("Maximum hits, default 50")] int limit = 50,
        CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.ReferencesAsync(new ReferenceSearch(symbolId, limit), cancellationToken));

    private Task<JsonElement> Diagnostics(
        [Description("Full path of one source file, or empty")] string? path = null,
        [Description("A project name, or empty for the whole solution")] string? project = null,
        CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.DiagnosticsAsync(new DiagnosticsQuery(path, project), cancellationToken));

    private Task<JsonElement> Map(
        [Description("Title for the map card")] string title = "Solution map",
        CancellationToken cancellationToken = default)
        => GuardedAsync(async () =>
        {
            var map = await Workspace().Map(new MapQuery(), cancellationToken).ConfigureAwait(false);
            var nodes = map.Projects.Select(project => new GraphNodeState(project.Name, project.Name, GraphNodeKinds.Module, project.Cluster)).ToArray();
            var edges = map.References.Select(edge => new GraphEdgeState($"{edge.From}-{edge.To}", edge.From, edge.To)).ToArray();
            // One artifact per solution: the shell keys artifacts by id, so a second map replaces the first.
            var id = "map-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(map.SolutionPath)))[..8];
            return new { kind = "graph", id, name = id, title, nodes, edges };
        });

    private Task<JsonElement> Skeleton([Description("Full path of one source file")] string path, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.SkeletonAsync(new SkeletonQuery(path), cancellationToken));

    private Task<JsonElement> Member([Description("A symbol id from code_find_symbols or code_skeleton")] string symbolId, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.MemberAsync(new MemberQuery(symbolId), cancellationToken));

    private Task<JsonElement> Callers([Description("A symbol id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.CallersAsync(new CallersQuery(symbolId, limit), cancellationToken));

    private Task<JsonElement> Implementations([Description("An interface, abstract member or virtual member id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.ImplementationsAsync(new ImplementationsQuery(symbolId, limit), cancellationToken));

    private Task<JsonElement> Derived([Description("A class or interface id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
        => GuardedAsync(() => _workspace.DerivedAsync(new DerivedQuery(symbolId, limit), cancellationToken));

    private Task<JsonElement> ProposeEdit(
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

    private Task<JsonElement> ProposeAsync(string changeId, string kind, EditRequest edit, CancellationToken cancellationToken)
        => GuardedAsync(async () =>
        {
            if (!Enum.TryParse<EditKind>(kind, ignoreCase: true, out var parsed))
            {
                throw new InvalidOperationException($"'{kind}' is not an edit kind. Use one of: {string.Join(", ", Enum.GetNames<EditKind>())}.");
            }

            var changeSet = ChangeSet(changeId);
            // Read before issuing: the grain rejects a stale proposal outright, and the wait below only
            // accepts a snapshot whose Revision moved past this one, i.e. this command's own settle.
            var before = await changeSet.Read().ConfigureAwait(false);
            await changeSet.Propose(new ProposeEdit(CommandId.New(), edit with { Kind = parsed }, before.Edits.Count)).ConfigureAwait(false);
            return await WaitAsync(changeSet, before, cancellationToken).ConfigureAwait(false);
        });

    private Task<JsonElement> Check([Description("The change set id")] string changeId, CancellationToken cancellationToken = default)
        => GuardedAsync(async () =>
        {
            var changeSet = ChangeSet(changeId);
            var before = await changeSet.Read().ConfigureAwait(false);
            await changeSet.Check(new CheckChangeSet(CommandId.New())).ConfigureAwait(false);
            return await WaitAsync(changeSet, before, cancellationToken).ConfigureAwait(false);
        });

    private Task<JsonElement> Commit(
        [Description("The change set id")] string changeId,
        [Description("One line saying what the change does; the commit message gets the coding: prefix")] string message,
        CancellationToken cancellationToken = default)
        => GuardedAsync(async () =>
        {
            var changeSet = ChangeSet(changeId);
            var before = await changeSet.Read().ConfigureAwait(false);
            await changeSet.Commit(new CommitChangeSet(CommandId.New(), message)).ConfigureAwait(false);
            var snapshot = await WaitAsync(changeSet, before, cancellationToken).ConfigureAwait(false);
            if (snapshot.Status != ChangeSetStatus.Committed)
            {
                return Result(snapshot, baseBranch: null, branch: null, commit: null, advice: snapshot.Detail);
            }

            var repository = Path.GetDirectoryName(SolutionPath())!;
            string? baseBranch = null;
            string? branch = null;
            try
            {
                // The branch the tree was on when this commit started, and the dirty-tree refusal, both come
                // before EnsureBranchAsync: a refused commit must not leave the tree parked on a coding branch
                // it just created and checked out.
                var current = await _git.CurrentBranchAsync(repository, cancellationToken).ConfigureAwait(false);
                baseBranch = string.IsNullOrWhiteSpace(current) ? null : current;
                await _git.RefuseIfDirtyOutsideAsync(repository, snapshot.Files, cancellationToken).ConfigureAwait(false);
                branch = await _git.EnsureBranchAsync(repository, "coding/" + changeId, cancellationToken).ConfigureAwait(false);
                var outcome = await _git.CommitAsync(repository, snapshot.Files, "coding: " + message, cancellationToken).ConfigureAwait(false);
                return Result(snapshot, baseBranch, branch, outcome.Hash, advice: null);
            }
#pragma warning disable CA1031 // a missing git binary (or any other git failure) must reach the model as advice, not an exception
            catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
            {
                // The grain already wrote the files and closed the change set; git merely failed the extra
                // branch/commit step, so that must not read as "nothing happened" to whoever reads this result.
                // branch stays null unless EnsureBranchAsync itself already succeeded.
                return Result(snapshot, baseBranch, branch, commit: null, advice: error.Message + " The change set already wrote these files; commit them yourself.");
            }

            static object Result(ChangeSetSnapshot snapshot, string? baseBranch, string? branch, string? commit, string? advice)
                => new { status = snapshot.Status, files = snapshot.Files, generation = snapshot.Generation, diff = snapshot.Diff, detail = snapshot.Detail, branch, baseBranch, commit, advice };
        });

    private Task<JsonElement> BuildSolution(
        [Description("Optional output root for this build, relative to the solution directory (use artifacts/<name>); never a path under a project folder")] string? artifactsPath = null,
        CancellationToken cancellationToken = default)
        => GuardedAsync(() => _dotnet.BuildAsync(SolutionPath(), ResolveArtifactsPath(artifactsPath), cancellationToken));

    private Task<JsonElement> Test(
        [Description("A test class full name to run only that class, or empty for everything")] string? filterClass = null,
        [Description("Optional output root for this build, relative to the solution directory (use artifacts/<name>); never a path under a project folder")] string? artifactsPath = null,
        CancellationToken cancellationToken = default)
        => GuardedAsync(() => _dotnet.TestAsync(_configuration[CodingModule.TestProjectKey] is { Length: > 0 } project ? project : SolutionPath(), filterClass, ResolveArtifactsPath(artifactsPath), cancellationToken));

    // Commands return at once; the reaction that does the work saves a new snapshot, which is what the model needs.
    // Every reaction that saves - propose, check, commit (success or failure), discard - bumps Revision, so
    // waiting for Revision to move past the pre-command snapshot always recognizes this command's own settle,
    // never a leftover Detail/Status a previous command already produced (Detail is a pure function of the edit
    // list and can repeat verbatim across two otherwise-unrelated reactions).
    // The deadline is internal bookkeeping, not a real cancellation: when it fires (and the caller did not itself
    // cancel), it becomes advice instead of an OperationCanceledException escaping GuardedAsync's filter.
    private async Task<ChangeSetSnapshot> WaitAsync(IChangeSet changeSet, ChangeSetSnapshot before, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ReactionWait);
        var last = before;
        // A reaction that settles at once is usually caught by the first or second poll; the backoff keeps a
        // long check or commit from costing hundreds of grain reads while it runs.
        var poll = TimeSpan.FromMilliseconds(20);
        try
        {
            while (true)
            {
                // Read() takes no token of its own, and a genuinely stuck reaction holds the grain's one turn,
                // queuing Read() behind it too - bounding the call from our own side is what lets the deadline
                // fire at all in that case, instead of Orleans' own much longer request timeout dominating it.
                last = await changeSet.Read().WaitAsync(timeout.Token).ConfigureAwait(false);
                if (last.Revision > before.Revision)
                {
                    return last;
                }

                await Task.Delay(poll, timeout.Token).ConfigureAwait(false);
                poll = TimeSpan.FromMilliseconds(Math.Min(500, poll.TotalMilliseconds * 2));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"The change set did not settle within {_options.ReactionWait.TotalSeconds:0}s; its status is still {last.Status}. Read it again or start a new change set.");
        }
    }

    private static async Task<JsonElement> GuardedAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return JsonSerializer.SerializeToElement(await query().ConfigureAwait(false), Json);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Advice(error);
        }
    }

    // A rejection is advice: the model reads why and what to do next instead of a failed tool call.
    private static JsonElement Advice(Exception error)
        => JsonSerializer.SerializeToElement(new { advice = error.Message }, Json);
}
