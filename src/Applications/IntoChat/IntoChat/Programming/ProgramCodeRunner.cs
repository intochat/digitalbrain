using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IntoChat;

// Deployed code is trusted local C#: a separate process bounds its lifetime and output,
// but is deliberately not described as an operating-system security sandbox.
public sealed class ProgramCodeRunner
{
    private const int MaximumSourceLength = 128 * 1024;
    private const int MaximumInputLength = 64 * 1024;
    private const int MaximumOutputLength = 64 * 1024;
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(30);

    public async Task<JsonElement> RunAsync(
        string code,
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (Encoding.UTF8.GetByteCount(code) > MaximumSourceLength)
        {
            throw new ArgumentException("A C# neuron can contain at most 128 KB of source.", nameof(code));
        }

        var inputJson = input.ValueKind is JsonValueKind.Undefined ? "{}" : input.GetRawText();
        if (Encoding.UTF8.GetByteCount(inputJson) > MaximumInputLength)
        {
            throw new ArgumentException("A C# neuron's JSON input can contain at most 64 KB.", nameof(input));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var directory = Directory.CreateTempSubdirectory("intochat-code-");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ExecutionTimeout);
        using var process = new Process();
        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "neuron.cs");
            await File.WriteAllTextAsync(sourcePath, code, deadline.Token).ConfigureAwait(false);
            process.StartInfo = CreateStartInfo(directory.FullName, sourcePath);
            if (!process.Start())
            {
                throw new InvalidOperationException("The .NET SDK could not start the C# neuron.");
            }

            outputTask = ReadBoundedAsync(process.StandardOutput, process, deadline.Token);
            errorTask = ReadBoundedAsync(process.StandardError, process, deadline.Token);
            await process.StandardInput.WriteAsync(inputJson.AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var diagnostics = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"C# neuron exited with code {process.ExitCode}: {diagnostics}\n{output}".Trim());
            }

            try
            {
                using var document = JsonDocument.Parse(output);
                return document.RootElement.Clone();
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException(
                    "C# neurons must write one JSON value to standard output. Send diagnostics to Console.Error.", error);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The C# neuron exceeded its 30-second execution limit.", error);
        }
        finally
        {
            StopProcess(process);
            await deadline.CancelAsync().ConfigureAwait(false);
            await ObserveAsync(outputTask).ConfigureAwait(false);
            await ObserveAsync(errorTask).ConfigureAwait(false);
            try
            {
                // Only the fresh directory created above is removed; user code paths never select it.
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
                // A just-terminated child can briefly retain a handle; cleanup must not mask its result.
            }
            catch (UnauthorizedAccessException)
            {
                // The execution result remains useful if the OS refuses temporary-file cleanup.
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string directory, string sourcePath)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[]
        {
            "run", "--file", sourcePath, "--no-launch-profile", "--disable-build-servers", "--verbosity", "quiet",
            "--property:PublishAot=false", "--property:EnableNETAnalyzers=false",
            "--property:EnforceCodeStyleInBuild=false", "--property:TreatWarningsAsErrors=false",
            "--property:ImportDirectoryBuildProps=false", "--property:ImportDirectoryBuildTargets=false",
        })
        {
            start.ArgumentList.Add(argument);
        }

        // Do not incidentally pass integration credentials from the server to a transform.
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "PROGRAMFILES", "PROGRAMFILES(X86)", "DOTNET_ROOT", "DOTNET_ROOT_X64" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                environment[name] = value;
            }
        }

        start.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, Process process, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        var outputBytes = 0;
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) is var count && count > 0)
        {
            outputBytes += Encoding.UTF8.GetByteCount(buffer, 0, count);
            if (outputBytes > MaximumOutputLength)
            {
                StopProcess(process);
                throw new InvalidOperationException("C# neuron output exceeded the 64 KB limit.");
            }

            output.Append(buffer, 0, count);
        }

        return output.ToString();
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process never started or exited concurrently with cancellation.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process can exit between the status check and the operating-system call.
        }
    }

    private static async Task ObserveAsync(Task<string>? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
