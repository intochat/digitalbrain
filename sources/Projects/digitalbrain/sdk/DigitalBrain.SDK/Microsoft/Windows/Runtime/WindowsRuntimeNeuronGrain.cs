using System.Diagnostics;
using System.Runtime.InteropServices;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.Microsoft.Windows.Runtime;

[GrainType(NeuronTargetFqn)]
public sealed class WindowsRuntimeNeuronGrain(ILogger<WindowsRuntimeNeuronGrain> logger) : Grain, ICallNeuronTarget
{
    public const string NeuronTargetFqn = "DigitalBrain.SDK.Windows.Runtime";

    public Task<string> AskAsync(string prompt)
    {
        var key = this.GetPrimaryKeyString();
        var (_, innerKey) = BrainScopeHelper.ParseScopedNeuronKey(key);
        if (string.IsNullOrEmpty(innerKey))
        {
            innerKey = key;
        }

        logger.LogInformation("Windows process neuron invoked. Key: {Key}, InnerKey: {InnerKey}, Prompt: {Prompt}", key, innerKey, prompt);

        string targetProcess = "";
        string arguments = "";

        // Case 1: Keyed activation (e.g. neuron(DigitalBrain.SDK.Windows.Runtime["notepad"]))
        if (!string.Equals(innerKey, NeuronTargetFqn, StringComparison.Ordinal))
        {
            targetProcess = innerKey.ToLowerInvariant();
            arguments = prompt;
        }
        // Case 2: Unkeyed activation (e.g. neuron(DigitalBrain.SDK.Windows.Runtime))
        else
        {
            var trimmedPrompt = prompt.Trim();
            if (trimmedPrompt.StartsWith("process.start.", StringComparison.OrdinalIgnoreCase))
            {
                targetProcess = trimmedPrompt.Substring("process.start.".Length).Trim();
            }
            else if (trimmedPrompt.StartsWith("process.start:", StringComparison.OrdinalIgnoreCase))
            {
                targetProcess = trimmedPrompt.Substring("process.start:".Length).Trim();
            }
            else if (trimmedPrompt.StartsWith("start ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmedPrompt.Substring("start ".Length).Trim().Split(' ', 2);
                targetProcess = parts[0];
                if (parts.Length > 1)
                {
                    arguments = parts[1];
                }
            }
            else if (trimmedPrompt.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmedPrompt.Substring("open ".Length).Trim().Split(' ', 2);
                targetProcess = parts[0];
                if (parts.Length > 1)
                {
                    arguments = parts[1];
                }
            }
            else if (trimmedPrompt.StartsWith("run ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmedPrompt.Substring("run ".Length).Trim().Split(' ', 2);
                targetProcess = parts[0];
                if (parts.Length > 1)
                {
                    arguments = parts[1];
                }
            }
            else if (trimmedPrompt.Equals("system.info", StringComparison.OrdinalIgnoreCase) ||
                     trimmedPrompt.Equals("system info", StringComparison.OrdinalIgnoreCase) ||
                     trimmedPrompt.Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                var os = RuntimeInformation.OSDescription;
                var arch = RuntimeInformation.OSArchitecture;
                var framework = RuntimeInformation.FrameworkDescription;
                var procCount = Environment.ProcessorCount;
                var machine = Environment.MachineName;
                return Task.FromResult($"OS: {os} ({arch}), Framework: {framework}, Processors: {procCount}, Machine: {machine}");
            }
            else if (trimmedPrompt.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase))
            {
                var cmd = trimmedPrompt.Substring("powershell ".Length).Trim();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    logger.LogInformation("Executing real PowerShell command: {Cmd}", cmd);
                    var psi = new ProcessStartInfo("powershell.exe")
                    {
                        Arguments = $"-NoProfile -NonInteractive -Command \"{cmd.Replace("\"", "\\\"")}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) return Task.FromResult("Error: Failed to start powershell process.");
                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    return Task.FromResult($"Exit Code: {proc.ExitCode}\n\nSTDOUT:\n{stdout}\n\nSTDERR:\n{stderr}");
                }
                else
                {
                    logger.LogInformation("Mocking PowerShell command on non-Windows host: {Cmd}", cmd);
                    return Task.FromResult($"Success (Simulated): PowerShell command executed: {cmd}");
                }
            }
            else if (trimmedPrompt.Equals("ps", StringComparison.OrdinalIgnoreCase) ||
                     trimmedPrompt.Equals("process.list", StringComparison.OrdinalIgnoreCase) ||
                     trimmedPrompt.Equals("processes", StringComparison.OrdinalIgnoreCase))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var allProcs = Process.GetProcesses()
                        .OrderBy(p => p.ProcessName)
                        .Take(50)
                        .Select(p => $"PID: {p.Id} - Name: {p.ProcessName}");
                    return Task.FromResult("Active Processes (Top 50):\n" + string.Join("\n", allProcs));
                }
                else
                {
                    return Task.FromResult("Active Processes (Simulated):\nPID: 1001 - Name: dotnet\nPID: 1002 - Name: redis-server");
                }
            }
            else if (trimmedPrompt.StartsWith("kill ", StringComparison.OrdinalIgnoreCase) ||
                     trimmedPrompt.StartsWith("process.kill ", StringComparison.OrdinalIgnoreCase))
            {
                var target = trimmedPrompt.StartsWith("kill ", StringComparison.OrdinalIgnoreCase)
                    ? trimmedPrompt.Substring("kill ".Length).Trim()
                    : trimmedPrompt.Substring("process.kill ".Length).Trim();

                if (string.IsNullOrWhiteSpace(target))
                {
                    return Task.FromResult("Error: Target process PID or Name is required.");
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        if (int.TryParse(target, out var pid))
                        {
                            using var proc = Process.GetProcessById(pid);
                            proc.Kill(true);
                            return Task.FromResult($"Success: Process with PID {pid} was terminated.");
                        }
                        else
                        {
                            var procs = Process.GetProcessesByName(target);
                            if (procs.Length == 0) return Task.FromResult($"Warning: No process found with name '{target}'.");
                            foreach (var p in procs)
                            {
                                p.Kill(true);
                            }
                            return Task.FromResult($"Success: Terminated {procs.Length} instance(s) of '{target}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return Task.FromResult($"Error: Failed to kill target '{target}'. Detail: {ex.Message}");
                    }
                }
                else
                {
                    return Task.FromResult($"Success (Simulated): Terminated process '{target}' on non-Windows host.");
                }
            }
            else if (trimmedPrompt.Equals("system.resources", StringComparison.OrdinalIgnoreCase) ||
                     trimmedPrompt.Equals("resources", StringComparison.OrdinalIgnoreCase))
            {
                var totalAlloc = GC.GetTotalMemory(false) / (1024 * 1024.0);
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => $"Drive {d.Name} - Free Space: {d.AvailableFreeSpace / (1024 * 1024 * 1024.0):F2} GB / Total: {d.TotalSize / (1024 * 1024 * 1024.0):F2} GB");

                var driveSummary = string.Join("\n", drives);
                return Task.FromResult($"Substrate Managed Memory: {totalAlloc:F2} MB\nHost Drives:\n{driveSummary}");
            }
            else
            {
                // General fallback: interpret the prompt as the command itself (e.g. "notepad")
                var parts = trimmedPrompt.Split(' ', 2);
                targetProcess = parts[0];
                if (parts.Length > 1)
                {
                    arguments = parts[1];
                }
            }
        }

        // Canonical mapping of friendly shortcuts
        targetProcess = targetProcess.ToLowerInvariant() switch
        {
            "notepad" => "notepad.exe",
            "calc" or "calculator" => "calc.exe",
            "explorer" => "explorer.exe",
            "cmd" or "terminal" => "cmd.exe",
            var other => other
        };

        if (string.IsNullOrWhiteSpace(targetProcess))
        {
            return Task.FromResult("Error: No process name was specified.");
        }

        // Platform-guarded process launching
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logger.LogInformation("Launching real Windows process: {Process} with args: {Args}", targetProcess, arguments);
                var psi = new ProcessStartInfo(targetProcess)
                {
                    Arguments = arguments,
                    UseShellExecute = true
                };
                using var proc = Process.Start(psi);
                return Task.FromResult($"Success: Process '{targetProcess}' launched with PID {proc?.Id ?? 0}.");
            }
            else
            {
                logger.LogInformation("Mocking process launch on non-Windows OS: {Process} with args: {Args}", targetProcess, arguments);
                return Task.FromResult($"Success (Simulated): Process '{targetProcess}' launched on non-Windows host.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start process: {Process}", targetProcess);
            return Task.FromResult($"Error: Failed to launch process '{targetProcess}'. Detail: {ex.Message}");
        }
    }
}
