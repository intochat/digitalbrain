using System.Text;
using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

[GrainType(Fqn)]
[Neuron]
internal sealed partial class Dotnet : Neuron, IDotnet, ICallNeuronTarget, IHandle<DotnetRequest>
{
    public const string Fqn = "DigitalBrain.Developer.Dotnet";

    private string GetWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")) || 
                System.IO.Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return @"e:\digitalbrain";
    }

    private async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string command, string arguments)
    {
        var workingDir = GetWorkspaceRoot();
        Logger.LogInformation("Dotnet executing process: {Cmd} {Args} in {Dir}", command, arguments, workingDir);

        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = command;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WorkingDirectory = workingDir;

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return (process.ExitCode, outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Dotnet process execution failed: {Cmd}", command);
            return (-1, "", ex.Message);
        }
    }

    public async Task<string> AskAsync(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return "Usage: build | test | format | run";

        var parts = prompt.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var extraArgs = parts.Length > 1 ? parts[1] : string.Empty;

        switch (cmd)
        {
            case "build":
                var (bCode, bOut, bErr) = await RunProcessAsync("dotnet", $"build {extraArgs}");
                return bCode == 0 ? "build success" : $"build failed: {bErr}\n{bOut}";
            case "test":
                var (tCode, tOut, tErr) = await RunProcessAsync("dotnet", $"test {extraArgs}");
                return tCode == 0 ? "test success" : $"test failed: {tErr}\n{tOut}";
            case "format":
                var (fCode, fOut, fErr) = await RunProcessAsync("dotnet", $"format {extraArgs}");
                return fCode == 0 ? "format success" : $"format failed: {fErr}\n{fOut}";
            case "run":
                var (rCode, rOut, rErr) = await RunProcessAsync("dotnet", $"run {extraArgs}");
                return rCode == 0 ? "run success" : $"run failed: {rErr}\n{rOut}";
            default:
                return $"Unknown command: {cmd}";
        }
    }

    public Task HandleAsync(DotnetRequest synapse, CancellationToken cancellationToken) => Handle(synapse);

    public async Task Handle(DotnetRequest synapse)
    {
        var cmd = synapse.Command.ToLowerInvariant();
        var extraArgs = synapse.Arguments ?? string.Empty;

        var (exitCode, output, error) = await RunProcessAsync("dotnet", $"{cmd} {extraArgs}");

        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = new DotnetResponse(
            Success: exitCode == 0,
            ExitCode: exitCode,
            Output: output,
            ErrorMessage: exitCode == 0 ? null : error
        ) { Headers = responseHeaders };

        await FireSynapseAsync(response);
    }
}
