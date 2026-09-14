using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class RunnerFacts
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    private const string BuildOutput = """
          Determining projects to restore...
        E:\repo\src\A\Thing.cs(12,9): error CS0103: The name 'x' does not exist in the current context [E:\repo\src\A\A.csproj]
        E:\repo\src\A\Thing.cs(12,9): error CS0103: The name 'x' does not exist in the current context [E:\repo\src\A\A.csproj]
        E:\repo\src\A\Other.cs(3,1): warning CS8019: Unnecessary using directive. [E:\repo\src\A\A.csproj]
        E:\repo\src\A\A.csproj : error NU1101: Unable to find package Missing. [E:\repo\src\A\A.csproj]

        Build FAILED.
        """;

    private const string TestOutput = """
        Running tests from E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64)
        failed DigitalBrain.Tests.Coding.RunnerFacts.Nope (12ms)
          Assert.Equal() Failure: Values differ

             at DigitalBrain.Tests.Coding.RunnerFacts.Nope() in E:\repo\tests\RunnerFacts.cs:line 42
        E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64) failed [+327/x1/?5] (1m 37s)

        Test run summary: Failed!
          total: 333
          failed: 1
          succeeded: 327
          skipped: 5
          duration: 1m 37s 446ms
        """;

    private const string ParameterizedTestOutput = """
        Running tests from E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64)
        failed Ns.Facts.Rejects(name: "a b") (12ms)
          Assert.True() Failure
        E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64) failed [+0/x1/?0] (1s)

        Test run summary: Failed!
          total: 1
          failed: 1
          succeeded: 0
          skipped: 0
        """;

    private const string SingleLineSummaryOutput = """
        Running tests from E:\repo\tests\bin\Release\net11.0\Tests.dll (net11.0|x64)

        Test run summary: Zero tests ran
          total: 5, failed: 1, succeeded: 3, skipped: 1
        """;

    [Fact]
    public async Task Build_errors_are_parsed_once_each_with_path_line_and_id()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, BuildOutput);
        var outcome = await new DotnetRunner(processes).BuildAsync("E:/repo/Repo.slnx", "E:/repo/artifacts/slot-b", TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Errors.Count);
        Assert.Equal(("CS0103", @"E:\repo\src\A\Thing.cs", 12), (outcome.Errors[0].Id, outcome.Errors[0].Path, outcome.Errors[0].Line));
        Assert.Equal(("NU1101", @"E:\repo\src\A\A.csproj", 0), (outcome.Errors[1].Id, outcome.Errors[1].Path, outcome.Errors[1].Line));
        Assert.Equal(1, outcome.WarningCount);
        var call = Assert.Single(processes.Calls);
        Assert.Equal("dotnet", call.FileName);
        // GetFullPath yields backslashes on Windows; the argument's separators are normalized before comparing.
        Assert.Equal(["build", "E:/repo/Repo.slnx", "-c", "Release", "--nologo", "-p:ArtifactsPath=E:/repo/artifacts/slot-b"],
            call.Arguments.Select(static argument => argument.Replace('\\', '/')));
        Assert.Equal("E:/repo", call.WorkingDirectory.Replace('\\', '/'));
    }

    [Fact]
    public async Task A_relative_artifacts_path_is_rooted_at_the_solution_directory()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        await new DotnetRunner(processes).BuildAsync("E:/repo/Repo.slnx", "artifacts/slot-b", TestContext.Current.CancellationToken);
        var call = Assert.Single(processes.Calls);
        var artifactsArgument = Assert.Single(call.Arguments, static argument => argument.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));
        Assert.EndsWith("E:/repo/artifacts/slot-b", artifactsArgument.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_results_are_parsed_with_the_failing_test_named()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, TestOutput);
        var outcome = await new DotnetRunner(processes).TestAsync("E:/repo/tests/Tests.csproj", "DigitalBrain.Tests.Coding.RunnerFacts", null, TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded);
        Assert.Equal((333, 327, 1, 5), (outcome.Total, outcome.Passed, outcome.Failed, outcome.Skipped));
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal("DigitalBrain.Tests.Coding.RunnerFacts.Nope", failure.Name);
        Assert.Contains("Values differ", failure.Message, StringComparison.Ordinal);
        Assert.Contains("RunnerFacts.cs:line 42", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["test", "E:/repo/tests/Tests.csproj", "-c", "Release", "--no-build", "--", "--filter-class", "DigitalBrain.Tests.Coding.RunnerFacts"], processes.Calls[0].Arguments);
    }

    [Fact]
    public async Task A_relative_test_artifacts_path_is_rooted_at_the_project_directory()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "Test run summary: Passed!\n  total: 0\n  failed: 0\n  succeeded: 0\n  skipped: 0\n");
        await new DotnetRunner(processes).TestAsync("E:/repo/tests/Tests.csproj", null, "artifacts/slot-b", TestContext.Current.CancellationToken);
        var call = Assert.Single(processes.Calls);
        var artifactsArgument = Assert.Single(call.Arguments, static argument => argument.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));
        Assert.EndsWith("E:/repo/tests/artifacts/slot-b", artifactsArgument.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_parameterized_test_failure_keeps_its_display_name()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, ParameterizedTestOutput);
        var outcome = await new DotnetRunner(processes).TestAsync("E:/repo/tests/Tests.csproj", null, null, TestContext.Current.CancellationToken);
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal("Ns.Facts.Rejects(name: \"a b\")", failure.Name);
    }

    [Fact]
    public async Task A_single_line_summary_is_parsed()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(8, SingleLineSummaryOutput);
        var outcome = await new DotnetRunner(processes).TestAsync("E:/repo/tests/Tests.csproj", null, null, TestContext.Current.CancellationToken);
        Assert.Equal((5, 3, 1, 1), (outcome.Total, outcome.Passed, outcome.Failed, outcome.Skipped));
    }

    [Fact]
    public async Task A_timed_out_process_is_a_failed_outcome_with_advice()
    {
        var timedOut = new TimedOutProcessRunner();
        var outcome = await new DotnetRunner(timedOut).BuildAsync("E:/repo/Repo.slnx", null, TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded);
        Assert.Contains("timed out", outcome.Detail, StringComparison.Ordinal);
    }

    private sealed class TimedOutProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, timeout, TimedOut: true));
    }

    [Fact]
    public async Task Git_commits_the_change_set_files_on_a_coding_branch()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync(TestContext.Current.CancellationToken);
        var git = new GitRunner(new ProcessRunner());
        await File.WriteAllTextAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource.Replace("Hello", "Hi", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var branch = await git.EnsureBranchAsync(fixture.Root, "coding/c1", TestContext.Current.CancellationToken);
        var outcome = await git.CommitAsync(fixture.Root, [fixture.GreeterPath], "coding: friendlier greeting", TestContext.Current.CancellationToken);

        Assert.Equal("coding/c1", branch);
        Assert.Equal("coding/c1", outcome.Branch);
        Assert.Equal(40, outcome.Hash.Length);
        Assert.Equal(["Alpha/Greeter.cs"], outcome.Files);
        Assert.Equal("coding/c1", await git.CurrentBranchAsync(fixture.Root, TestContext.Current.CancellationToken));
        Assert.Empty(await git.ChangedPathsAsync(fixture.Root, TestContext.Current.CancellationToken));
        Assert.Equal("coding/c1", await git.EnsureBranchAsync(fixture.Root, "coding/c1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Git_refuses_when_the_tree_is_dirty_outside_the_change_set()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync(TestContext.Current.CancellationToken);
        var git = new GitRunner(new ProcessRunner());
        await File.WriteAllTextAsync(fixture.GreeterPath, "namespace Alpha;", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(fixture.ProgramPath, "namespace Beta;", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => git.CommitAsync(fixture.Root, [fixture.GreeterPath], "coding: partial", TestContext.Current.CancellationToken));
        Assert.Contains("Beta/Program.cs", error.Message, StringComparison.Ordinal);
        Assert.Contains("outside the change set", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timed_out_branch_probe_is_an_error_not_a_missing_branch()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(-1, string.Empty, timedOut: true);
        var git = new GitRunner(processes);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => git.EnsureBranchAsync("E:/repo", "coding/c1", TestContext.Current.CancellationToken));
        Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_git_call_disables_the_pager_and_fsmonitor()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, string.Empty);
        var git = new GitRunner(processes);
        await git.CurrentBranchAsync("E:/repo", TestContext.Current.CancellationToken);
        var call = Assert.Single(processes.Calls);
        Assert.Equal(["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.quotepath=false", "branch", "--show-current"], call.Arguments);
    }

    [Fact]
    public async Task A_failing_process_reports_its_exit_code_and_stderr()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync(TestContext.Current.CancellationToken);
        var result = await new ProcessRunner().RunAsync("git", ["rev-parse", "--verify", "nonexistent-ref"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(128, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.False(result.TimedOut);
    }

    [Fact(Skip = "Windows only", SkipUnless = nameof(IsWindows))]
    public async Task A_process_that_outlives_its_timeout_is_killed_and_its_partial_output_kept()
    {
        var result = await new ProcessRunner().RunAsync("cmd", ["/c", "ping", "-n", "30", "127.0.0.1"], Path.GetTempPath(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.True(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10));
        Assert.Contains("Pinging", result.Output, StringComparison.Ordinal);
    }
}
