using System.Diagnostics;

namespace DigitalBrain.PackageTests;

internal static class DotnetCli
{
    public static (int ExitCode, string Output) Run(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the dotnet CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, standardOutput.Result + standardError.Result);
    }

    public static string RunChecked(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var (exitCode, output) = Run(workingDirectory, environment, arguments);
        return exitCode == 0
            ? output
            : throw new InvalidOperationException(
                $"dotnet {string.Join(' ', arguments)} failed with exit code {exitCode}:{Environment.NewLine}{output}");
    }
}
