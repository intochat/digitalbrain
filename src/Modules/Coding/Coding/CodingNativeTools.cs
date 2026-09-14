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

public sealed class CodingNativeTools(SolutionWorkspace workspace, IGrainFactory grains, DotnetRunner dotnet, GitRunner git, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly TimeSpan ReactionWait = TimeSpan.FromMinutes(2);

    // A field initializer cannot close over instance members (CS0236), so Build takes every dependency the primary
    // constructor captured as an explicit parameter and threads them through its local functions instead.
    private readonly Lazy<IReadOnlyList<AIFunction>> _functions = new(() => Build(workspace, grains, dotnet, git, configuration));

    public IReadOnlyList<AIFunction> Functions => _functions.Value;

    public AIFunction Named(string name) => Functions.Single(function => function.Name == name);

    private static IReadOnlyList<AIFunction> Build(SolutionWorkspace workspace, IGrainFactory grains, DotnetRunner dotnet, GitRunner git, IConfiguration configuration)
    {
        IChangeSet ChangeSet(string changeId)
            => grains.GetGrain<IChangeSet>(new NeuronId(CodingVocabulary.ChangeSetType, changeId).ToGrainId());

        string SolutionPath()
            => workspace.Status.SolutionPath ?? throw new InvalidOperationException(workspace.Status.Advice);

        Task<JsonElement> FindSymbols(
            [Description("Part of a symbol name, case-insensitive")] string query,
            [Description("Maximum hits, default 20")] int limit = 20,
            CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.FindSymbolsAsync(new SymbolSearch(query, limit), cancellationToken));

        Task<JsonElement> References(
            [Description("A symbol id from code_find_symbols, such as T:DigitalBrain.Time.ITimer")] string symbolId,
            [Description("Maximum hits, default 50")] int limit = 50,
            CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.ReferencesAsync(new ReferenceSearch(symbolId, limit), cancellationToken));

        Task<JsonElement> Diagnostics(
            [Description("Full path of one source file, or empty")] string? path = null,
            [Description("A project name, or empty for the whole solution")] string? project = null,
            CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.DiagnosticsAsync(new DiagnosticsQuery(path, project), cancellationToken));

        Task<JsonElement> Map(
            [Description("Title for the map card")] string title = "Solution map",
            CancellationToken cancellationToken = default)
            => GuardedAsync(async () =>
            {
                var map = await workspace.MapAsync(new MapQuery(), cancellationToken).ConfigureAwait(false);
                var nodes = map.Projects.Select(project => new GraphNodeState(project.Name, project.Name, GraphNodeKinds.Module, project.Cluster)).ToArray();
                var edges = map.References.Select(edge => new GraphEdgeState($"{edge.From}-{edge.To}", edge.From, edge.To)).ToArray();
                // One artifact per solution: the shell keys artifacts by id, so a second map replaces the first.
                var id = "map-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(map.SolutionPath)))[..8];
                return new { kind = "graph", id, name = id, title, nodes, edges };
            });

        Task<JsonElement> Skeleton([Description("Full path of one source file")] string path, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.SkeletonAsync(new SkeletonQuery(path), cancellationToken));

        Task<JsonElement> Member([Description("A symbol id from code_find_symbols or code_skeleton")] string symbolId, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.MemberAsync(new MemberQuery(symbolId), cancellationToken));

        Task<JsonElement> Callers([Description("A symbol id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.CallersAsync(new CallersQuery(symbolId, limit), cancellationToken));

        Task<JsonElement> Implementations([Description("An interface, abstract member or virtual member id")] string symbolId, [Description("Maximum hits, default 50")] int limit = 50, CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.ImplementationsAsync(new ImplementationsQuery(symbolId, limit), cancellationToken));

        Task<JsonElement> ProposeAsync(string changeId, string kind, EditRequest edit, CancellationToken cancellationToken)
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

        Task<JsonElement> CheckAsync(string changeId, CancellationToken cancellationToken)
            => GuardedAsync(async () =>
            {
                var changeSet = ChangeSet(changeId);
                await changeSet.Check(new CheckChangeSet(CommandId.New())).ConfigureAwait(false);
                return await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Checked || snapshot.Detail is not null, cancellationToken).ConfigureAwait(false);
            });

        Task<JsonElement> CommitAsync(string changeId, string message, CancellationToken cancellationToken)
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

        return
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
            AIFunctionFactory.Create(ProposeEdit, new AIFunctionFactoryOptions { Name = "code_propose_edit", Description = "Add one edit to a change set. Nothing touches disk until code_commit; code_check compiles the snapshot first." }),
            AIFunctionFactory.Create(Check, new AIFunctionFactoryOptions { Name = "code_check", Description = "Apply a change set to one snapshot and compile it: diagnostics and a diff, no files written." }),
            AIFunctionFactory.Create(Commit, new AIFunctionFactoryOptions { Name = "code_commit", Description = "Write a clean change set to disk and commit it on a coding/<changeId> git branch. Refuses when the check has errors or the tree is dirty elsewhere." }),
            AIFunctionFactory.Create(Build, new AIFunctionFactoryOptions { Name = "code_build", Description = "dotnet build of the solution in Release; parsed errors and warnings." }),
            AIFunctionFactory.Create(Test, new AIFunctionFactoryOptions { Name = "code_test", Description = "dotnet test without rebuilding; counts and the failing tests. Run code_build first." }),
        ];
    }

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
