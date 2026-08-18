using System.Diagnostics;

var repoRoot = LocateRepoRoot();
var scriptingDir = Path.Combine(repoRoot, "src", "Kernel", "DigitalBrain.Scripting");

// 1) Membership pruning mutates the live cluster table — explicit opt-in only.
if (Environment.GetEnvironmentVariable("DIGITALBRAIN_PRUNE_MEMBERSHIP") == "1")
{
    var prune = Path.Combine(scriptingDir, "prune-membership.cs");
    Console.WriteLine($"prune:{prune}");
    Console.WriteLine(await RunScriptAsync(prune).ConfigureAwait(false));
}
else
{
    Console.WriteLine("membership prune skipped (set DIGITALBRAIN_PRUNE_MEMBERSHIP=1 to enable)");
}

// 2) Wait for kernel HTTP — starts in parallel; prune unblocks cluster join.
Console.WriteLine("waiting for kernel http://localhost:5080/health …");
using var wait = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
while (DateTimeOffset.UtcNow < deadline)
{
    try
    {
        var response = await wait.GetAsync("http://localhost:5080/health").ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("kernel healthy");
            break;
        }
    }
    catch
    {
        // not up yet
    }

    await Task.Delay(2000).ConfigureAwait(false);
}

// 3) Wave 2 registry probe.
var probe = Path.Combine(scriptingDir, "wave2-registry-probe.cs");
Console.WriteLine($"probe:{probe}");
Console.WriteLine(await RunScriptAsync(probe).ConfigureAwait(false));

static async Task<string> RunScriptAsync(string scriptPath)
{
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
        ?? throw new InvalidOperationException("Failed to start nested script.");

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync().ConfigureAwait(false);
    var stdout = await stdoutTask.ConfigureAwait(false);
    var stderr = await stderrTask.ConfigureAwait(false);

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Script failed (exit {process.ExitCode}).{Environment.NewLine}{stderr}{stdout}");
    }

    return stdout + stderr;
}

static string LocateRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Could not locate DigitalBrain.slnx.");
}
