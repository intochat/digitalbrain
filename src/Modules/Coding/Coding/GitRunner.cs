using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.Coding;

public sealed class GitRunner(IProcessRunner processes)
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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
        var probe = await RunGitAsync(repository, ["rev-parse", "--verify", "--quiet", "refs/heads/" + branch], cancellationToken).ConfigureAwait(false);
        var exists = probe.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw Failed("rev-parse", probe),
        };
        await GitAsync(repository, exists ? ["checkout", "-q", branch] : ["checkout", "-q", "-b", branch], cancellationToken).ConfigureAwait(false);
        return branch;
    }

    // Refuses when the tree carries changes outside the change set: a commit must contain exactly what the
    // snapshot wrote, never a stray edit the owner had not saved on purpose. The caller runs this before it
    // ensures the branch, so a refused commit never leaves the tree parked on a branch it just created.
    public async Task RefuseIfDirtyOutsideAsync(string repository, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var relative = RelativePaths(repository, files);
        var outside = (await ChangedPathsAsync(repository, cancellationToken).ConfigureAwait(false))
            .Where(path => !relative.Contains(path, PathComparer))
            .ToArray();
        if (outside.Length > 0)
        {
            throw new InvalidOperationException($"The working tree has changes outside the change set: {string.Join(", ", outside.Take(5))}. Commit or stash them first.");
        }
    }

    public async Task<GitCommitOutcome> CommitAsync(string repository, IReadOnlyList<string> files, string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        // Checked again here, not only by the caller: a commit must never carry a stray edit even when it
        // was reached without the tool's earlier check.
        await RefuseIfDirtyOutsideAsync(repository, files, cancellationToken).ConfigureAwait(false);
        var relative = RelativePaths(repository, files);
        await GitAsync(repository, ["add", "--", .. relative], cancellationToken).ConfigureAwait(false);
        await GitAsync(repository, ["commit", "-q", "-m", message], cancellationToken).ConfigureAwait(false);
        var hash = (await GitAsync(repository, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Output.Trim();
        return new GitCommitOutcome(hash, await CurrentBranchAsync(repository, cancellationToken).ConfigureAwait(false), relative);
    }

    private static IReadOnlyList<string> RelativePaths(string repository, IReadOnlyList<string> files)
        => [.. files.Select(file => Path.GetRelativePath(repository, file).Replace('\\', '/'))];

    private async Task<ProcessResult> GitAsync(string repository, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw Failed(arguments[0], result);
        }

        return result;
    }

    // Every git call goes through here so the timeout check and the pager/fsmonitor/quotepath flags apply
    // uniformly - including the EnsureBranchAsync probe, whose non-zero exit codes are not all errors.
    private async Task<ProcessResult> RunGitAsync(string repository, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", ["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.quotepath=false", .. arguments], repository, GitTimeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new InvalidOperationException($"git {arguments[0]} timed out after {GitTimeout.TotalSeconds:0}s");
        }

        return result;
    }

    private static InvalidOperationException Failed(string verb, ProcessResult result)
        => new($"git {verb} failed: {(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim()}");
}