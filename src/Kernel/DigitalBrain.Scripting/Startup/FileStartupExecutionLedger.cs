using System.Text;
using System.Text.Json;

namespace DigitalBrain.Scripting.Startup;

internal sealed class FileStartupExecutionLedger : IStartupExecutionLedger
{
    private const string FileName = "startup-executions.jsonl";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<StartupExecutionKey, StartupExecution> executions = [];
    private readonly string path;
    private bool isLoaded;

    public FileStartupExecutionLedger(string stateDirectory)
    {
        path = Path.Combine(stateDirectory, FileName);
    }

    public async Task<StartupExecution?> FindAsync(
        StartupExecutionKey key,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            return executions.GetValueOrDefault(key);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordAsync(StartupExecution execution, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
            if (executions.ContainsKey(execution.Key))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteLineAsync(JsonSerializer.Serialize(execution, SerializerOptions));
            await writer.FlushAsync(cancellationToken);
            executions.Add(execution.Key, execution);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (isLoaded)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            foreach (var line in lines)
            {
                var execution = JsonSerializer.Deserialize<StartupExecution>(line, SerializerOptions)
                    ?? throw new InvalidDataException($"Startup execution ledger entry in '{path}' was empty.");
                executions.TryAdd(execution.Key, execution);
            }
        }

        isLoaded = true;
    }
}
