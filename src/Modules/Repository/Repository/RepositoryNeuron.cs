using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Repository;

[GrainType(IRepository.GrainTypeName)]
public sealed class RepositoryNeuron : Neuron, IRepository
{
    private const string StateName = "repository.state";
    private const int MaxEnumerate = 5000;

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<RepoState> _states;

    public RepositoryNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<RepoState>>();
    }

    public Task HandleAsync(OpenRepository synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var root = ResolveRoot(synapse.RootPath);
        if (!Directory.Exists(root))
        {
            throw new NeuronAuthorizationException(
                $"Repository root '{root}' does not exist.");
        }

        var files = EnumerateFiles(root, extension: null, limit: MaxEnumerate);
        Stage(new RepoState(root, TimeProvider.GetUtcNow()));
        return ReplyAsync(
            new RepositoryOpened(synapse.CommandId, root, files.Length),
            cancellationToken);
    }

    public Task HandleAsync(ListRepositoryFiles synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var state = Load()
            ?? throw new NeuronAuthorizationException(
                $"Repository '{Id}' has no open root. Fire db.open-repository first.");

        var limit = synapse.Limit <= 0 ? 30 : Math.Min(synapse.Limit, 200);
        var files = EnumerateFiles(state.RootPath, synapse.Extension, limit);
        return ReplyAsync(new RepositoryFilesListed(synapse.CommandId, files), cancellationToken);
    }

    public Task HandleAsync(ReadRepositoryFile synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var state = Load()
            ?? throw new NeuronAuthorizationException(
                $"Repository '{Id}' has no open root. Fire db.open-repository first.");

        var relative = (synapse.RelativePath ?? "").Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative)
            || relative.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new NeuronAuthorizationException("Relative path is required and must stay under the root.");
        }

        var full = Path.GetFullPath(Path.Combine(state.RootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(state.RootPath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full))
        {
            throw new NeuronAuthorizationException($"File '{relative}' is outside the open repository or missing.");
        }

        var maxChars = synapse.MaxChars <= 0 ? 4000 : Math.Min(synapse.MaxChars, 32_000);
        var text = File.ReadAllText(full);
        var truncated = text.Length > maxChars;
        if (truncated)
        {
            text = text[..maxChars];
        }

        return ReplyAsync(
            new RepositoryFileContent(synapse.CommandId, relative, text, truncated),
            cancellationToken);
    }

    private static string ResolveRoot(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new NeuronAuthorizationException("RootPath is required.");
        }

        var full = Path.GetFullPath(raw.Trim());
        // Refuse obvious system roots; allow project trees.
        if (full is "/" or "\\"
            || full.Equals(Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
        {
            throw new NeuronAuthorizationException("Refusing to open a filesystem root as a repository.");
        }

        return full;
    }

    private static string[] EnumerateFiles(string root, string? extension, int limit)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? null : extension.Trim();
        if (ext is { Length: > 0 } && !ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        IEnumerable<string> query = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        if (ext is not null)
        {
            query = query.Where(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        // Skip bin/obj/.git noise for stance quality.
        query = query.Where(p =>
        {
            var rel = Path.GetRelativePath(root, p);
            return !rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !rel.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !rel.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !rel.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        });

        return
        [
            .. query
                .Take(limit)
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private RepoState? Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(RepoState state)
        => _state.Value = _states.SerializeToArray(state);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A repository command requires a command id.");
        }
    }
}
