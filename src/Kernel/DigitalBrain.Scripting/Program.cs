using System.Diagnostics;
using System.Text;

var workRoot = Path.Combine(Path.GetTempPath(), "digitalbrain-scripting", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workRoot);

try
{
    var generated = BrainChatProbeGenerator.Write(workRoot);
    Console.WriteLine($"generated:{generated.ProgramPath}");
    Console.WriteLine($"project:{generated.ProjectPath}");

    var stdout = await BrainChatProbeRunner.RunAsync(generated.ProjectPath).ConfigureAwait(false);
    Console.WriteLine("--- probe stdout ---");
    Console.WriteLine(stdout);
    Console.WriteLine("--- end probe ---");
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

internal static class BrainChatProbeGenerator
{
    internal static GeneratedProbe Write(string workRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        var repoRoot = LocateRepoRoot();
        var programPath = Path.Combine(workRoot, "Program.cs");
        var projectPath = Path.Combine(workRoot, "DigitalBrain.Scripting.Probe.csproj");

        File.WriteAllText(programPath, ProbeProgramSource, Encoding.UTF8);
        File.WriteAllText(
            projectPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <LangVersion>latest</LangVersion>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{repoRoot}}\src\Kernel\Aspire\DigitalBrain.Aspire\DigitalBrain.Aspire.csproj" />
                <ProjectReference Include="{{repoRoot}}\src\Kernel\DigitalBrain.Client\DigitalBrain.Client.csproj" />
                <ProjectReference Include="{{repoRoot}}\src\Modules\UI\Contracts\DigitalBrain.Modules.UI.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """,
            Encoding.UTF8);

        return new GeneratedProbe(programPath, projectPath);
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

    private const string ProbeProgramSource =
        """
        using DigitalBrain.Abstractions;
        using DigitalBrain.Aspire;
        using DigitalBrain.Chat;
        using DigitalBrain.Client;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;

        var builder = Host.CreateApplicationBuilder(args);
        builder.AddDigitalBrainClient();

        using var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);

        var brain = host.Services.GetRequiredService<IDigitalBrain>();
        await brain.ActivateAsync().ConfigureAwait(false);

        var command = new CommandId(Guid.NewGuid());
        const string chatName = "scripting-proof";
        var chatId = NeuronId.For<IChat>(brain.Owner, chatName);

        await brain.GetGrainProxy<IChat>(chatName)
            .Send(new SendMessage(command, "Who are you?"))
            .ConfigureAwait(false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0,
            timeout.Token).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is Responded response && response.CommandId == command)
                {
                    Console.WriteLine(response.Text);
                    await host.StopAsync().ConfigureAwait(false);
                    return;
                }
            }
        }

        throw new TimeoutException("Assistant did not answer the scripting probe within 5 minutes.");
        """;
}

internal sealed record GeneratedProbe(string ProgramPath, string ProjectPath);

internal static class BrainChatProbeRunner
{
    internal static async Task<string> RunAsync(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var start = new ProcessStartInfo("dotnet", $"run --project \"{projectPath}\" --no-launch-profile")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory,
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
            ?? throw new InvalidOperationException("Failed to start dotnet run for the generated probe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Generated probe failed (exit {process.ExitCode}).{Environment.NewLine}{stderr}{stdout}");
        }

        return stdout;
    }
}
