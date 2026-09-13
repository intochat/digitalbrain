using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Coding;

public sealed class CodingNativeTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lazy<IReadOnlyList<AIFunction>> _functions;

    public CodingNativeTools(SolutionWorkspace workspace) => _functions = new Lazy<IReadOnlyList<AIFunction>>(() => Build(workspace));

    public IReadOnlyList<AIFunction> Functions => _functions.Value;

    public AIFunction Named(string name) => Functions.Single(function => function.Name == name);

    private static IReadOnlyList<AIFunction> Build(SolutionWorkspace workspace)
    {
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
        ];
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
