using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.Options;

namespace DigitalBrain.CSharpExpert;

public sealed record WorkspaceLocation(string Root, string SolutionPath);

public sealed class WorkspacePreparer(IProcessRunner processes, IOptions<CSharpExpertModuleOptions> options)
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);

    public async Task<WorkspaceLocation> PrepareAsync(string solutionPath, string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var full = Path.GetFullPath(solutionPath);
        var repository = await RepositoryRootAsync(full, cancellationToken).ConfigureAwait(false);
        if (repository is not null)
        {
            var root = options.Value.WorkspaceRoot ?? Path.Combine(Path.GetDirectoryName(repository)!, "wt-runs");
            var worktree = Path.Combine(root, runId);
            Directory.CreateDirectory(root);
            await GitAsync(repository, ["worktree", "add", "--detach", "--force", worktree], cancellationToken).ConfigureAwait(false);
            return new WorkspaceLocation(worktree, Path.Combine(worktree, Path.GetRelativePath(repository, full)));
        }

        var copyRoot = options.Value.WorkspaceRoot ?? Path.Combine(Path.GetTempPath(), "csharp-expert-runs");
        var folder = Path.Combine(copyRoot, runId);
        CopyDirectory(Path.GetDirectoryName(full)!, folder);
        return new WorkspaceLocation(folder, Path.Combine(folder, Path.GetFileName(full)));
    }

    private async Task<string?> RepositoryRootAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var result = await processes.RunAsync("git", ["--no-pager", "-c", "core.fsmonitor=false", "rev-parse", "--show-toplevel"], directory, GitTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new InvalidOperationException($"git rev-parse timed out after {GitTimeout.TotalSeconds:0}s");
        }

        var root = result.Output.Trim();
        return result.ExitCode == 0 && root.Length > 0 ? Path.GetFullPath(root) : null;
    }

    private async Task GitAsync(string repository, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", ["--no-pager", "-c", "core.fsmonitor=false", .. arguments], repository, GitTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new InvalidOperationException($"git {arguments[0]} timed out after {GitTimeout.TotalSeconds:0}s");
        }

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException($"git {arguments[0]} failed: {detail.Trim()}");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (Directory.Exists(entry))
            {
                if (name is "bin" or "obj" or ".git")
                {
                    continue;
                }

                CopyDirectory(entry, Path.Combine(destination, name));
            }
            else
            {
                File.Copy(entry, Path.Combine(destination, name), overwrite: true);
            }
        }
    }
}
