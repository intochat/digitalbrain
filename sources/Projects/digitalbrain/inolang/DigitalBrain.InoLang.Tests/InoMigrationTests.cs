namespace DigitalBrain.InoLang.Tests;

public sealed class InoMigrationTests
{
    static IContractCatalog Catalog() => DeferredContractCatalog.Instance;

    [Fact]
    public async Task Ino_neuron_compiles_links_and_passes_scenario_cleanly()
    {
        // 1. Resolve exact path to Ino.ino
        var baseDir = AppContext.BaseDirectory;
        var inoPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../../kernel/DigitalBrain.Kernel/Ino/Ino.ino"));
        
        if (!File.Exists(inoPath))
        {
            // Fallback for different build output layouts
            inoPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../kernel/DigitalBrain.Kernel/Ino/Ino.ino"));
        }

        File.Exists(inoPath).Should().BeTrue($"Ino.ino must exist at: {inoPath}");
        var source = await File.ReadAllTextAsync(inoPath, TestContext.Current.CancellationToken);

        // 2. Compile and assert successful parsing and linking
        var compiled = InoCompiler.Compile(source, Catalog());
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}:{d.Message} at {d.Span.Start}-{d.Span.End}")));

        // 3. Verify that the states are correctly parsed in the AST
        compiled.Linked.Should().NotBeNull();
        compiled.Linked!.Doc.States.Should().NotBeNull();
        compiled.Linked.Doc.States.Should().HaveCount(2);
        compiled.Linked.Doc.States![0].Name.Should().Be("chatHistory");
        compiled.Linked.Doc.States![0].Type.Should().Be("list");
        compiled.Linked.Doc.States![1].Name.Should().Be("activeResponse");
        compiled.Linked.Doc.States![1].Type.Should().Be("string");

        // 4. Run the BDD self-testing scenario block in the InoLang execution sandbox
        var gate = await compiled.EvaluateGateAsync(TestContext.Current.CancellationToken);
        gate.CanActivate.Should().BeTrue(gate.Reason);
    }

    [Fact]
    public async Task HelloLogs_neuron_compiles_links_and_passes_scenario_cleanly()
    {
        // 1. Resolve exact path to HelloLogs.ino
        var baseDir = AppContext.BaseDirectory;
        var inoPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../../examples/HelloLogs.ino"));
        
        if (!File.Exists(inoPath))
        {
            // Fallback for different build output layouts
            inoPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../examples/HelloLogs.ino"));
        }

        File.Exists(inoPath).Should().BeTrue($"HelloLogs.ino must exist at: {inoPath}");
        var source = await File.ReadAllTextAsync(inoPath, TestContext.Current.CancellationToken);

        var catalog = DeferredContractCatalog.Instance;

        // 2. Compile and assert successful parsing and linking
        var compiled = InoCompiler.Compile(source, catalog);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}:{d.Message} at {d.Span.Start}-{d.Span.End}")));

        // 3. Run the BDD self-testing scenario block in the InoLang execution sandbox
        var gate = await compiled.EvaluateGateAsync(TestContext.Current.CancellationToken);
        gate.CanActivate.Should().BeTrue(gate.Reason);
    }

    [Fact]
    public async Task ComparisonOrchestrator_neuron_compiles_links_and_passes_scenario_cleanly()
    {
        // 1. Resolve exact path to ComparisonOrchestrator.ino
        var baseDir = AppContext.BaseDirectory;
        var inoPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../../examples/ComparisonOrchestrator.ino"));
        
        if (!File.Exists(inoPath))
        {
            // Fallback for different build output layouts
            inoPath = Path.GetFullPath(Path.Combine(baseDir, "../../../../examples/ComparisonOrchestrator.ino"));
        }

        File.Exists(inoPath).Should().BeTrue($"ComparisonOrchestrator.ino must exist at: {inoPath}");
        var source = await File.ReadAllTextAsync(inoPath, TestContext.Current.CancellationToken);

        var catalog = DeferredContractCatalog.Instance;

        // 2. Compile and assert successful parsing and linking
        var compiled = InoCompiler.Compile(source, catalog);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}:{d.Message} at {d.Span.Start}-{d.Span.End}")));

        // 3. Run the BDD self-testing scenario block in the InoLang execution sandbox
        var gate = await compiled.EvaluateGateAsync(TestContext.Current.CancellationToken);
        gate.CanActivate.Should().BeTrue(gate.Reason);
    }

    [Fact]
    public async Task Compile_all_ino_files_in_repository()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("DigitalBrain.slnx must be found in ancestors");
        var root = dir!.FullName;
        var files = InoFileDiscovery.Enumerate(root)
            .Where(f => !f.Contains("examples" + Path.DirectorySeparatorChar) && !f.Contains("examples/"))
            .ToList();
        files.Should().NotBeEmpty("There should be active .ino files in the repository");

        var failures = new List<string>();
        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            var compiled = InoCompiler.Compile(source);
            if (!compiled.Success)
            {
                failures.Add($"File {Path.GetRelativePath(root, file)} failed compilation:\n" +
                    string.Join("\n", compiled.Diagnostics.Select(d => $"[{d.Severity}] {d.Code} {d.Message}")));
            }
        }

        failures.Should().BeEmpty(string.Join("\n\n", failures));
    }
}


