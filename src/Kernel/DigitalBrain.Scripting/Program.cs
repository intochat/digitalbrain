using System.Diagnostics;
using System.Text;

var workRoot = Path.Combine(Path.GetTempPath(), "digitalbrain-scripting", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workRoot);

try
{
    var generated = ChartPointScriptGenerator.Write(workRoot);
    Console.WriteLine($"generated:{generated.ScriptPath}");

    var stdout = await ChartPointScriptRunner.RunAsync(generated.ScriptPath).ConfigureAwait(false);
    Console.WriteLine("--- script stdout ---");
    Console.WriteLine(stdout);
    Console.WriteLine("--- end script ---");
}
finally
{
    try
    {
        Directory.Delete(workRoot, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

internal static class ChartPointScriptGenerator
{
    internal static GeneratedScript Write(string workRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        var repoRoot = LocateRepoRoot();
        var aspireProject = Path.Combine(
            repoRoot,
            "src",
            "Kernel",
            "Aspire",
            "DigitalBrain.Aspire",
            "DigitalBrain.Aspire.csproj");
        var uiContracts = Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "UI",
            "DigitalBrain.Modules.UI.Contracts",
            "DigitalBrain.Modules.UI.Contracts.csproj");
        var scriptPath = Path.Combine(workRoot, "chart-point.cs");

        File.WriteAllText(
            scriptPath,
            $$"""
            #:project {{aspireProject}}
            #:project {{uiContracts}}
            #:property TreatWarningsAsErrors=false
            #:property PublishAot=false

            using DigitalBrain.Aspire;
            using DigitalBrain.Client;
            using DigitalBrain.UI;

            var brain = await DigitalBrainClient.ConnectAsync(args);
            await brain.FireAsync(new ChartPoint("cpu", DateTimeOffset.Now.ToString("HH:mm"), 42));

            Console.WriteLine("ChartPoint fired.");
            """,
            Encoding.UTF8);

        return new GeneratedScript(scriptPath);
    }

    private static string LocateRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate DigitalBrain.slnx above the scripting process. Run from the product tree.");
    }
}

internal sealed record GeneratedScript(string ScriptPath);

internal static class ChartPointScriptRunner
{
    internal static async Task<string> RunAsync(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        var start = new ProcessStartInfo("dotnet", $"run --file \"{scriptPath}\" --no-launch-profile")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
        };

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key is null || start.Environment.ContainsKey(key))
            {
                continue;
            }

            start.Environment[key] = entry.Value?.ToString() ?? string.Empty;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start dotnet run for the generated script.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Generated script failed (exit {process.ExitCode}).{Environment.NewLine}{stderr}{stdout}");
        }

        return stdout;
    }
}
