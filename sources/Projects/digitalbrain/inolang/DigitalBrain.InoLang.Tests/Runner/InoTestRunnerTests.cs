using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.InoLang.Tests.Runner;

public sealed class InoTestRunnerTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ino-runner-" + Guid.NewGuid().ToString("N"));

    public InoTestRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    static IContractCatalog GreenCatalog() => DeferredContractCatalog.Instance;

    const string Green = """
        neuron A.X
          using ask   = synapse(A.Req)
          using gpt   = neuron(A.Gpt)
          using db    = neuron(A.Db)
          using ready = signal(A.Done)
          @telemetry:counter:done
          on ask:
            let s = ask gpt to "analyze {ask.text}"
            save s into db
            count done
            emit ready(summary: s)
        scenario "produces an analysis"
          given gpt returns "crowded market"
          when synapse ask(text: "anything")
          then db has "crowded market"
          and signal ready emitted with summary == "crowded market"
          and counter done == 1
        """;

    string Write(string relative, string source)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    [Fact]
    public async Task Green_file_passes()
    {
        var path = Write("green.ino", Green);

        var report = await InoTestRunner.RunFileAsync(
            path, "green.ino", GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeTrue(
            "compile diagnostics: " + string.Join("; ", report.CompileDiagnostics.Select(d => d.Message)) +
            " | scenario failures: " + string.Join("; ", report.Scenarios?.Results.SelectMany(r => r.Failures) ?? []));
        report.CompileDiagnostics.Should().BeEmpty();
        report.Scenarios!.AllPassed.Should().BeTrue();
    }

    [Fact]
    public async Task Red_scenario_in_file_is_reported_not_thrown()
    {
        var redSource = Green.Replace("counter done == 1", "counter done == 99");
        var path = Write("red.ino", redSource);

        var report = await InoTestRunner.RunFileAsync(
            path, "red.ino", GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.Scenarios!.AllPassed.Should().BeFalse();
        report.Scenarios.Results[0].Failures
            .Should().Contain(f => f.Contains("done"));
    }

    [Fact]
    public async Task Compile_error_is_surfaced_not_thrown()
    {
        // Missing scenario keyword - clear parse failure.
        const string broken = """
            neuron A.X
              ::this is not valid syntax::
            """;
        var path = Write("broken.ino", broken);

        var report = await InoTestRunner.RunFileAsync(
            path, "broken.ino", GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.CompileDiagnostics
            .Should().Contain(d => d.Severity == DiagnosticSeverity.Error,
                "a broken .ino must surface its diagnostics as failures, not throw");
        report.Scenarios.Should().BeNull(
            "no plan was produced — there is nothing to run scenarios against");
    }

    [Fact]
    public async Task Zero_scenarios_in_file_is_failure_per_L6()
    {
        const string noScenarios = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: ask.text)
            """;
        var path = Write("no-scenarios.ino", noScenarios);

        var report = await InoTestRunner.RunFileAsync(
            path, "no-scenarios.ino", GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.CompileDiagnostics.Should().BeEmpty(
            "the file compiles; the failure is the missing scenario");
        report.Scenarios!.AllPassed.Should().BeFalse();
    }

    [Fact]
    public async Task Run_directory_aggregates_files()
    {
        Write("green.ino", Green);
        Write("red.ino", Green.Replace("counter done == 1", "counter done == 99"));

        var report = await InoTestRunner.RunDirectoryAsync(
            _root, GreenCatalog(), CancellationToken.None);

        report.Files.Should().HaveCount(2);
        report.AllPassed.Should().BeFalse(
            "one of the two scenarios is red — the whole tree is red");
    }

    [Fact]
    public async Task Empty_directory_is_not_all_passed()
    {
        var report = await InoTestRunner.RunDirectoryAsync(
            _root, GreenCatalog(), CancellationToken.None);

        report.Files.Should().BeEmpty();
        report.AllPassed.Should().BeFalse(
            "v3 §L6: a tree with zero .ino files cannot satisfy spec-first");
    }

    [Fact]
    public async Task File_relative_path_is_preserved_in_report()
    {
        Write("nested/leaf.ino", Green);

        var report = await InoTestRunner.RunDirectoryAsync(
            _root, GreenCatalog(), CancellationToken.None);

        report.Files.Should().ContainSingle()
            .Which.RelativePath.Should().Be(Path.Combine("nested", "leaf.ino"));
    }
}
