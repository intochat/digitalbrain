using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.Roslyn;

// The configured workspace key keeps the warmed, file-watched workspace; every other key gets its own,
// so opening a second solution never replaces the one another caller is editing.
public sealed class SolutionWorkspaces(
    SolutionWorkspace configured,
    ISolutionLoader loader,
    ILogger<SolutionWorkspace> logger,
    IOptions<RoslynModuleOptions> options) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<SolutionWorkspace>> _isolated = new(StringComparer.Ordinal);

    public SolutionWorkspace For(string key)
        => IsConfigured(key)
            ? configured
            : _isolated.GetOrAdd(key, _ => new Lazy<SolutionWorkspace>(() => new SolutionWorkspace(loader, logger))).Value;

    public void Close(string key)
    {
        if (!IsConfigured(key) && _isolated.TryRemove(key, out var workspace) && workspace.IsValueCreated)
        {
            workspace.Value.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var workspace in _isolated.Values.Where(workspace => workspace.IsValueCreated))
        {
            workspace.Value.Dispose();
        }

        _isolated.Clear();
    }

    private bool IsConfigured(string key) => string.Equals(key, options.Value.WorkspaceKey, StringComparison.Ordinal);
}
