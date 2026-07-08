using System.Diagnostics;

namespace DigitalBrain.Kernel.Foundry;

public record BuildOutcome(bool Success, string Log);

public interface IBuildRunner
{
    Task<BuildOutcome> VerifyBuildAsync(string moduleName, string source, CancellationToken cancellationToken = default);
}

public sealed class ProcessBuildRunner : IBuildRunner
{
    public async Task<BuildOutcome> VerifyBuildAsync(string moduleName, string source, CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "foundry-verify-" + moduleName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, moduleName + ".cs"), source, cancellationToken);
            var kernelProject = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "DigitalBrain.Kernel.csproj"));
            if (!File.Exists(kernelProject))
            {
                kernelProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "DigitalBrain.Kernel.csproj"));
            }

            await File.WriteAllTextAsync(Path.Combine(tempDir, "verify.csproj"), VerifyProject(kernelProject), cancellationToken);

            var psi = new ProcessStartInfo("dotnet", $"build \"{Path.Combine(tempDir, "verify.csproj")}\" -c Release")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);
            return new BuildOutcome(process.ExitCode == 0, stdoutTask.Result + stderrTask.Result);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static string VerifyProject(string siloProjectPath) => $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{siloProjectPath}" />
  </ItemGroup>
</Project>
""";
}
