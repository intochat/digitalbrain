using DigitalBrain.InoLang.Tests;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.InoLang.Tests.Runner;

public sealed class InoScenarioProjectionTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ino-projection-" + Guid.NewGuid().ToString("N"));

    public InoScenarioProjectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    static IContractCatalog GreenCatalog() => DeferredContractCatalog.Instance;

    const string GreenSingleScenario = """
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

    const string GreenTwoScenarios = GreenSingleScenario + """

        scenario "second one also passes"
          given gpt returns "crowded market"
          when synapse ask(text: "again")
          then db has "crowded market"
          and signal ready emitted with summary == "crowded market"
          and counter done == 1
        """;

    const string DuplicateScenarioNames = GreenSingleScenario + """

        scenario "produces an analysis"
          given gpt returns "crowded market"
          when synapse ask(text: "again")
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

    static string LabelOf(TheoryDataRow<string, string, string> row) => row.Label ?? string.Empty;

    [Fact]
    public void Discover_yields_one_row_per_scenario_in_a_green_file()
    {
        Write("green.ino", GreenSingleScenario);

        var rows = InoScenarioProjection.Discover(_root).ToList();

        rows.Should().ContainSingle()
            .Which.Label.Should().Be("green.ino :: produces an analysis");
    }

    [Fact]
    public void Discover_yields_one_row_per_scenario_in_a_multi_scenario_file()
    {
        Write("multi.ino", GreenTwoScenarios);

        var rows = InoScenarioProjection.Discover(_root).ToList();

        rows.Select(LabelOf).Should().BeEquivalentTo(
            new[]
            {
                "multi.ino :: produces an analysis",
                "multi.ino :: second one also passes",
            });
    }

    [Fact]
    public void Discover_preserves_nested_relative_paths_with_forward_slashes()
    {
        Write(Path.Combine("nested", "deep", "leaf.ino"), GreenSingleScenario);

        var rows = InoScenarioProjection.Discover(_root).ToList();

        rows.Should().ContainSingle()
            .Which.Label.Should().Be("nested/deep/leaf.ino :: produces an analysis");
    }

    [Fact]
    public void Discover_orders_files_ordinally_then_scenarios_in_declaration_order()
    {
        Write("b.ino", GreenSingleScenario);
        Write("a.ino", GreenTwoScenarios);
        Write("c.ino", GreenSingleScenario);

        var rows = InoScenarioProjection.Discover(_root).Select(LabelOf).ToList();

        rows.Should().Equal(
            "a.ino :: produces an analysis",
            "a.ino :: second one also passes",
            "b.ino :: produces an analysis",
            "c.ino :: produces an analysis");
    }

    [Fact]
    public void Discover_emits_synthetic_compile_error_row_for_broken_file()
    {
        Write("broken.ino", "::not valid::");

        var rows = InoScenarioProjection.Discover(_root).ToList();

        rows.Should().ContainSingle()
            .Which.Label.Should().Be("broken.ino :: <compile error>");
    }

    [Fact]
    public void Discover_emits_synthetic_no_scenarios_row_for_compiling_but_scenarioless_file()
    {
        const string noScenarios = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: ask.text)
            """;
        Write("empty.ino", noScenarios);

        var rows = InoScenarioProjection.Discover(_root).ToList();

        rows.Should().ContainSingle()
            .Which.Label.Should().Be("empty.ino :: <no scenarios>");
    }

    [Fact]
    public void Discover_returns_empty_for_directory_with_no_ino_files()
    {
        // Tree-emptiness is the consumer's L6 concern (the outer Theory caller).
        var rows = InoScenarioProjection.Discover(_root).ToList();
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Discover_emits_synthetic_missing_root_row_when_root_does_not_exist()
    {
        // xUnit-v3 enumerates MemberData at collection time — throwing here
        // would kill the whole Theory before any row runs. A failing synthetic
        // row keeps the failure addressable.
        var missing = Path.Combine(_root, "does-not-exist");

        var rows = InoScenarioProjection.Discover(missing).ToList();

        rows.Should().ContainSingle()
            .Which.Label.Should().StartWith("<missing root>:");
    }

    [Fact]
    public void Discover_yields_separately_addressable_rows_for_duplicate_scenario_names()
    {
        Write("dup.ino", DuplicateScenarioNames);

        var rows = InoScenarioProjection.Discover(_root).Select(LabelOf).ToList();

        // Index disambiguator stops the two same-named scenarios collapsing.
        rows.Should().HaveCount(2);
        rows.Should().AllSatisfy(label => label.Should().Contain("produces an analysis"));
        rows.Should().Contain(label => label.Contains("[#0]"));
        rows.Should().Contain(label => label.Contains("[#1]"));
    }

    [Fact]
    public async Task RunAsync_returns_passed_for_a_green_scenario()
    {
        Write("green.ino", GreenSingleScenario);

        var report = await InoScenarioProjection.RunAsync(
            _root, "green.ino", "produces an analysis", "scenario:0",
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeTrue(report.Message);
    }

    [Fact]
    public async Task RunAsync_returns_failed_with_failure_messages_for_a_red_scenario()
    {
        var red = GreenSingleScenario.Replace("counter done == 1", "counter done == 99");
        Write("red.ino", red);

        var report = await InoScenarioProjection.RunAsync(
            _root, "red.ino", "produces an analysis", "scenario:0",
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.Message.Should().Contain("done");
    }

    [Fact]
    public async Task RunAsync_with_synthetic_compile_error_key_returns_diagnostics()
    {
        Write("broken.ino", "::not valid::");

        var report = await InoScenarioProjection.RunAsync(
            _root, "broken.ino", "<compile error>",
            InoScenarioProjection.CompileErrorScenarioKey,
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.Message.Should().NotBeNullOrEmpty(
            "the synthetic compile-error scenario must surface diagnostics in the failure message");
    }

    [Fact]
    public async Task RunAsync_with_synthetic_no_scenarios_key_fails_per_L6()
    {
        const string noScenarios = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: ask.text)
            """;
        Write("empty.ino", noScenarios);

        var report = await InoScenarioProjection.RunAsync(
            _root, "empty.ino", "<no scenarios>",
            InoScenarioProjection.NoScenariosScenarioKey,
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse(
            "v3 §L6: a file with no scenarios cannot satisfy spec-first");
    }

    [Fact]
    public async Task RunAsync_with_synthetic_missing_root_key_fails_cleanly()
    {
        var report = await InoScenarioProjection.RunAsync(
            Path.Combine(_root, "does-not-exist"),
            string.Empty, "<missing root>",
            InoScenarioProjection.MissingRootScenarioKey,
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.Message.Should().Contain("does-not-exist");
    }

    [Fact]
    public async Task RunAsync_dispatches_duplicate_scenarios_by_index()
    {
        // Make the second scenario red while the first stays green — index
        // dispatch must reach the right one, even though both share a name.
        var twoSameNames = GreenSingleScenario + """

            scenario "produces an analysis"
              given gpt returns "crowded market"
              when synapse ask(text: "again")
              then db has "crowded market"
              and signal ready emitted with summary == "crowded market"
              and counter done == 99
            """;
        Write("dup.ino", twoSameNames);

        var first = await InoScenarioProjection.RunAsync(
            _root, "dup.ino", "produces an analysis", "scenario:0",
            GreenCatalog(), CancellationToken.None);
        var second = await InoScenarioProjection.RunAsync(
            _root, "dup.ino", "produces an analysis", "scenario:1",
            GreenCatalog(), CancellationToken.None);

        first.Passed.Should().BeTrue(first.Message);
        second.Passed.Should().BeFalse(
            "the second copy expects counter done == 99 and should fail");
    }

    [Fact]
    public async Task RunAsync_returns_failed_for_unparseable_scenario_key()
    {
        Write("green.ino", GreenSingleScenario);

        var report = await InoScenarioProjection.RunAsync(
            _root, "green.ino", "produces an analysis", "not-a-key",
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.Message.Should().Contain("scenario key");
    }

    [Fact]
    public async Task RunAsync_returns_failed_for_out_of_range_scenario_index()
    {
        Write("green.ino", GreenSingleScenario);

        var report = await InoScenarioProjection.RunAsync(
            _root, "green.ino", "produces an analysis", "scenario:99",
            GreenCatalog(), CancellationToken.None);

        report.Passed.Should().BeFalse();
        report.Message.Should().Contain("out of range");
    }

    [Fact]
    public async Task RunAsync_propagates_cancellation()
    {
        Write("green.ino", GreenSingleScenario);
        var cancelled = new CancellationToken(canceled: true);

        Func<Task> act = () => InoScenarioProjection.RunAsync(
            _root, "green.ino", "produces an analysis", "scenario:0",
            GreenCatalog(), cancelled);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "infrastructure faults (cancellation, IO) cross the boundary as exceptions; " +
            "scenario failures are the only thing surfaced via ScenarioRunReport");
    }

    [Fact]
    public void Discover_uses_the_same_exclusions_as_file_discovery()
    {
        // bin/obj/Generated/.git/node_modules must not leak scenarios — L6
        // would otherwise count generated/build outputs against spec-first.
        Write(Path.Combine("bin", "x.ino"), GreenSingleScenario);
        Write(Path.Combine("obj", "y.ino"), GreenSingleScenario);
        Write(Path.Combine("Generated", "z.ino"), GreenSingleScenario);
        Write("kept.ino", GreenSingleScenario);

        var rows = InoScenarioProjection.Discover(_root).Select(LabelOf).ToList();

        rows.Should().ContainSingle().Which.Should().StartWith("kept.ino");
    }

    [Fact]
    public void Discover_does_not_collide_user_scenario_named_like_compile_error_sentinel()
    {
        // A user-authored `scenario "<compile error>"` must NOT be misread as the
        // sentinel — dispatch is by key, not by name.
        var sneaky = GreenSingleScenario.Replace(
            "scenario \"produces an analysis\"",
            "scenario \"<compile error>\"");
        Write("sneaky.ino", sneaky);

        var rows = InoScenarioProjection.Discover(_root).ToList();

        rows.Should().ContainSingle().Which.Should().Match<TheoryDataRow<string, string, string>>(
            r => r.Label!.Contains("<compile error>")
              && !r.Label.Contains(InoScenarioProjection.CompileErrorScenarioKey));
    }
}
