using System.Diagnostics;

namespace DigitalBrain.Templates.Tests.Unit;

public sealed class TemplateSmokeFacts
{
    [Fact]
    public async Task IntochatModuleTemplateInTreeScaffoldsContractsModuleAndTests()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Root();
        var work = Path.Combine(Path.GetTempPath(), "db-template-unit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var hive = Path.Combine(work, "hive");
            var templatePath = Path.Combine(root, "templates", "DigitalBrain.Templates", "content", "intochat-module");
            await Dotnet(work, ["new", "install", templatePath, "--debug:custom-hive", hive], ct);

            const string name = "SampleInTreeModule";
            var outDir = Path.Combine(work, name);

            // Scaffold with --inTree true
            await Dotnet(work, ["new", "intochat-module", "-n", name, "-o", outDir, "--ModuleId", "acme.sample", "--inTree", "true", "--debug:custom-hive", hive], ct);

            // Verify in-tree module files scaffolded
            Assert.True(File.Exists(Path.Combine(outDir, name, name + ".csproj")), "Module .csproj should exist");
            Assert.True(File.Exists(Path.Combine(outDir, name, $"{name}Module.cs")), "Module class should exist");
            Assert.True(File.Exists(Path.Combine(outDir, name, "Contracts", $"I{name}.cs")), "Contracts interface should exist");
            Assert.True(File.Exists(Path.Combine(outDir, name, "src", "NoteNeuron.cs")), "Neuron should exist");
            Assert.True(File.Exists(Path.Combine(outDir, "tests", $"{name}.Tests.Unit", $"{name}.Tests.Unit.csproj")), "Unit test .csproj should exist");
            Assert.True(File.Exists(Path.Combine(outDir, "tests", $"{name}.Tests.Integration", $"{name}.Tests.Integration.csproj")), "Integration test .csproj should exist");
            Assert.True(File.Exists(Path.Combine(outDir, "README.md")), "README.md should exist");

            // Verify root repo config files are NOT present when --inTree true
            Assert.False(File.Exists(Path.Combine(outDir, "Directory.Build.props")), "Directory.Build.props must NOT exist in in-tree scaffold");
            Assert.False(File.Exists(Path.Combine(outDir, "Directory.Packages.props")), "Directory.Packages.props must NOT exist in in-tree scaffold");
            Assert.False(File.Exists(Path.Combine(outDir, "global.json")), "global.json must NOT exist in in-tree scaffold");
            Assert.False(File.Exists(Path.Combine(outDir, "nuget.config")), "nuget.config must NOT exist in in-tree scaffold");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task IntochatModuleTemplateStandaloneScaffoldsRepoFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Root();
        var work = Path.Combine(Path.GetTempPath(), "db-template-unit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var hive = Path.Combine(work, "hive");
            var templatePath = Path.Combine(root, "templates", "DigitalBrain.Templates", "content", "intochat-module");
            await Dotnet(work, ["new", "install", templatePath, "--debug:custom-hive", hive], ct);

            const string name = "SampleStandaloneModule";
            var outDir = Path.Combine(work, name);

            // Scaffold with --inTree false (standalone mode)
            await Dotnet(work, ["new", "intochat-module", "-n", name, "-o", outDir, "--ModuleId", "acme.standalone", "--inTree", "false", "--debug:custom-hive", hive], ct);

            // Verify module structure exists
            Assert.True(File.Exists(Path.Combine(outDir, name, name + ".csproj")), "Module .csproj should exist");
            Assert.True(File.Exists(Path.Combine(outDir, name, $"{name}Module.cs")), "Module class should exist");
            Assert.True(File.Exists(Path.Combine(outDir, name, "Contracts", $"I{name}.cs")), "Contracts interface should exist");
            Assert.True(File.Exists(Path.Combine(outDir, name, "src", "NoteNeuron.cs")), "Neuron should exist");
            Assert.True(File.Exists(Path.Combine(outDir, "tests", $"{name}.Tests.Unit", $"{name}.Tests.Unit.csproj")), "Unit test .csproj should exist");
            Assert.True(File.Exists(Path.Combine(outDir, "tests", $"{name}.Tests.Integration", $"{name}.Tests.Integration.csproj")), "Integration test .csproj should exist");
            Assert.True(File.Exists(Path.Combine(outDir, "README.md")), "README.md should exist");

            // Verify standalone repo config files ARE present when --inTree false
            Assert.True(File.Exists(Path.Combine(outDir, "Directory.Build.props")), "Directory.Build.props must exist in standalone scaffold");
            Assert.True(File.Exists(Path.Combine(outDir, "Directory.Packages.props")), "Directory.Packages.props must exist in standalone scaffold");
            Assert.True(File.Exists(Path.Combine(outDir, "global.json")), "global.json must exist in standalone scaffold");
            Assert.True(File.Exists(Path.Combine(outDir, "nuget.config")), "nuget.config must exist in standalone scaffold");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<string> Dotnet(string directory, string[] arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (var name in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "DOTNET_HOST_PATH", "TESTINGPLATFORM_DOTNETTEST_EXECUTIONID" })
        {
            start.Environment.Remove(name);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = (await stdout) + (await stderr);
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} exited {process.ExitCode}:\n{output}");
        return output;
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("DigitalBrain.slnx not found above " + AppContext.BaseDirectory);
    }
}
