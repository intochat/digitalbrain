using DigitalBrain.CSharpExpert;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class WorkspacePreparerFacts
{
    [Fact]
    public async Task ASolutionOutsideARepositoryIsCopiedToTheRunRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = NewTempDirectory();
        try
        {
            var preparer = new WorkspacePreparer(
                new ScriptedProcessRunner(new ScriptedProcessScript { IsGitRepository = false }),
                Options.Create(new CSharpExpertModuleOptions { WorkspaceRoot = root }));
            var location = await preparer.PrepareAsync(CSharpExpertTestHost.SampleSolution, "run-copy", ct);
            Assert.Equal(Path.Combine(root, "run-copy"), location.Root);
            Assert.Equal(Path.Combine(location.Root, "SampleInbox.sln"), location.SolutionPath);
            Assert.True(File.Exists(location.SolutionPath));
            Assert.True(File.Exists(Path.Combine(location.Root, "SampleInbox", "Inbox.cs")));
            Assert.True(File.Exists(Path.Combine(location.Root, "SampleInbox.Tests", "InboxTests.cs")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task ASolutionInsideARepositoryGetsAWorktree()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = NewTempDirectory();
        var root = NewTempDirectory();
        try
        {
            var solution = Path.Combine(repository, "Sample.sln");
            await File.WriteAllTextAsync(solution, "solution", ct);
            var git = new ProcessRunner();
            await RunGitAsync(git, repository, ["init", "-q"], ct);
            await RunGitAsync(git, repository, ["add", "Sample.sln"], ct);
            await RunGitAsync(git, repository, ["-c", "user.email=tests@digitalbrain", "-c", "user.name=DigitalBrain", "commit", "-q", "-m", "init"], ct);

            var preparer = new WorkspacePreparer(git, Options.Create(new CSharpExpertModuleOptions { WorkspaceRoot = root }));
            var location = await preparer.PrepareAsync(solution, "run-worktree", ct);
            Assert.Equal(Path.Combine(root, "run-worktree"), location.Root);
            Assert.Equal(Path.Combine(location.Root, "Sample.sln"), location.SolutionPath);
            Assert.True(File.Exists(location.SolutionPath));
        }
        finally
        {
            Delete(root);
            Delete(repository);
        }
    }

    private static async Task RunGitAsync(IProcessRunner git, string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync("git", arguments, directory, TimeSpan.FromSeconds(60), cancellationToken);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.Error}");
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "csharp-expert-preparer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
