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
