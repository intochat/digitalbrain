using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class RepositoryDiffTests : IAsyncLifetime
{
    private const string TempPrefix = "digitalbrain-review-test-";
    private static readonly OwnerId Owner = new("reviewer");
    // Shell punctuation and spaces are deliberate: the path must remain a literal directory.
    private readonly string _path = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"), "repo & echo injected");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_path);
        await GitAsync("init", "--initial-branch=main");
        await GitAsync("config", "user.name", "Repository Review Tests");
        await GitAsync("config", "user.email", "review@example.invalid");
    }

    [Fact]
    public void ToolIsOptInAndAvailableOnlyToConfiguredOwner()
    {
        Assert.Empty(Source(path: "").PrepareTestTools(Owner));
        Assert.Empty(Source().PrepareTestTools(new OwnerId("someone-else")));
        Assert.Single(Source().PrepareTestTools(Owner));
        var fallback = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Workspace:RepositoryPath"] = _path,
            ["DigitalBrain:Owner"] = Owner.Value,
        }).Build();
        Assert.Single(new RepositoryDiffToolSource(fallback).PrepareTestTools(Owner));
        Assert.Empty(new RepositoryDiffToolSource(fallback).PrepareTestTools(new OwnerId("dev")));
    }

    [Fact]
    public async Task WorkingTreeIncludesStagedAndUnstagedChangesWhileStagedUsesOnlyIndex()
    {
        await CommitBaselineAsync();
        await WriteAsync("tracked.txt", "baseline\nstaged-change\n");
        await GitAsync("add", "tracked.txt");
        await WriteAsync("tracked.txt", "baseline\nstaged-change\nunstaged-change\n");

        var working = await ReadAsync();
        var staged = await ReadAsync("staged");

        Assert.Contains("+staged-change", working);
        Assert.Contains("+unstaged-change", working);
        Assert.Contains("+staged-change", staged);
        Assert.DoesNotContain("+unstaged-change", staged);
        Assert.Contains(_path.Replace('\\', '/'), working);
        Assert.Contains("Branch: main", working);
        Assert.Contains("Truncated: false", working);
        Assert.Equal("MM tracked.txt", (await GitAsync("status", "--short")).Trim());
    }

    [Fact]
    public async Task UntrackedFilesAreNamedWithoutClaimingTheirContentsWereReviewed()
    {
        await CommitBaselineAsync();
        await WriteAsync("new-file.txt", "private-untracked-content");

        var result = await ReadAsync();

        Assert.Contains("?? new-file.txt", result);
        Assert.Contains("Untracked file contents are NOT included", result);
        Assert.DoesNotContain("private-untracked-content", result);
    }

    [Fact]
    public async Task LargeDiffExplicitlyReportsTruncation()
    {
        await CommitBaselineAsync();
        await WriteAsync("tracked.txt", new string('x', 20_000));

        var result = await ReadAsync(source: Source(maxOutputCharacters: 1024));

        Assert.Contains("TRUNCATED", result);
        Assert.Contains("review is incomplete", result);
        Assert.True(result.Length < 1200);
    }

    [Fact]
    public async Task MissingAndNonRepositoryPathsReturnAnHonestFailure()
    {
        var missing = await ReadAsync(source: Source(path: Path.Combine(_path, "missing")));
        var plainDirectory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_path)!, "not-a-repo"));
        var notRepository = await ReadAsync(source: Source(path: plainDirectory.FullName));

        Assert.Contains("configured directory does not exist", missing);
        Assert.Contains("Repository diff unavailable", notRepository);
        Assert.Contains("not a git repository", notRepository);
    }

    [Fact]
    public async Task InvalidScopeCannotInjectCommandsAndRepositoryDiffProgramsAreDisabled()
    {
        await CommitBaselineAsync();
        await WriteAsync("tracked.txt", "baseline\nreview-this-change\n");
        await WriteAsync(".gitattributes", "*.txt diff=untrusted\n");
        await GitAsync("config", "diff.external", "THIS_PROGRAM_MUST_NOT_RUN");
        await GitAsync("config", "diff.untrusted.textconv", "THIS_PROGRAM_MUST_NOT_RUN");
        await GitAsync("config", "core.fsmonitor", "THIS_PROGRAM_MUST_NOT_RUN");

        var rejected = await ReadAsync("working_tree; echo injected");
        var valid = await ReadAsync();

        Assert.Contains("scope must be working_tree or staged", rejected);
        Assert.Contains("+review-this-change", valid);
        Assert.DoesNotContain("Repository diff unavailable", valid);
    }

    [Fact]
    public async Task RepositoryWithoutACommitShowsSeparateStagedAndUnstagedPatches()
    {
        await WriteAsync("tracked.txt", "initial-version\n");
        await GitAsync("add", "tracked.txt");
        await WriteAsync("tracked.txt", "initial-version\nnext-version\n");

        var working = await ReadAsync();
        var staged = await ReadAsync("staged");

        Assert.Contains("no commits yet", working);
        Assert.Contains("Staged patch (no HEAD)", working);
        Assert.Contains("Unstaged patch (against index; no HEAD)", working);
        Assert.Contains("+initial-version", working);
        Assert.Contains("+next-version", working);
        Assert.DoesNotContain("next-version", staged);
    }

    public ValueTask DisposeAsync()
    {
        var testDirectory = Path.GetFullPath(Path.GetDirectoryName(_path)!);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!testDirectory.StartsWith(Path.Combine(tempRoot, TempPrefix), StringComparison.Ordinal)
            || Path.GetDirectoryName(testDirectory) != Path.TrimEndingDirectorySeparator(tempRoot))
        {
            throw new InvalidOperationException("Refusing cleanup outside this test's temporary directory.");
        }

        if (Directory.Exists(testDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(testDirectory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(testDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private RepositoryDiffToolSource Source(string? path = null, int maxOutputCharacters = 64 * 1024)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Workspace:RepositoryPath"] = path ?? _path,
            ["DigitalBrain:Workspace:Owner"] = Owner.Value,
        }).Build(), maxOutputCharacters);

    private async Task<string> ReadAsync(string? scope = null, RepositoryDiffToolSource? source = null)
    {
        var arguments = new AIFunctionArguments();
        if (scope is not null)
        {
            arguments["scope"] = scope;
        }

        var result = await (source ?? Source()).PrepareTestTools(Owner).Single().InvokeAsync(arguments, TestContext.Current.CancellationToken);
        return result!.ToString()!;
    }

    private Task WriteAsync(string name, string content)
        => File.WriteAllTextAsync(Path.Combine(_path, name), content, TestContext.Current.CancellationToken);

    private async Task CommitBaselineAsync()
    {
        await WriteAsync("tracked.txt", "baseline\n");
        await GitAsync("add", "tracked.txt");
        await GitAsync("commit", "-m", "baseline");
    }

    private async Task<string> GitAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-c", "commit.gpgsign=false", "-c", $"core.hooksPath={Path.Combine(_path, "no-hooks")}" }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
