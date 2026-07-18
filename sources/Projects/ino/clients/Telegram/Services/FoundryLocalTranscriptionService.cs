using System.Text;
using Microsoft.AI.Foundry.Local;

namespace Ino.Telegram.Host.Services;

/// <summary>
/// Local-only audio transcription via Foundry Local's Whisper model. Runs as
/// an <see cref="IHostedService"/> so the model is downloaded + loaded at silo
/// startup (download can take minutes on first boot); voice messages received
/// before init completes throw a clear "still initializing" error rather than
/// hanging or swallowing the audio.
///
/// <para>Falls back from the GPU/CUDA variant to the generic-cpu variant
/// automatically if the first inference fails with a CUDA/CUDNN error —
/// matters on developer laptops without a usable CUDA install.</para>
/// </summary>
public sealed class FoundryLocalTranscriptionService(
    IConfiguration configuration,
    ILogger<FoundryLocalTranscriptionService> logger,
    IAudioConverter? audioConverter = null)
    : IAudioTranscriptionService, IHostedService, IWhisperReadiness, IAsyncDisposable
{
    static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];
    static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    // Priority order — newest large-v3 first, fall back to large-v2 then base.
    // Foundry Local catalog ids may carry suffixes; resolution does a contains-id
    // match so e.g. "Whisper-large-v3-cuda-gpu" matches the "Whisper-large-v3" key.
    static readonly string[] WhisperModelPreference =
    [
        "Whisper-large-v3",
        "Whisper-large-v2",
        "whisper-base",
    ];

    IModel? _model;

    public bool IsReady { get; private set; }
    public bool InitializationFailed { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Initializing Foundry Local for Whisper...");
            await InitializeFoundryManagerAsync(ct);

            var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
            _model = await ResolveModelAsync(catalog);

            logger.LogInformation("Downloading Whisper model {ModelId}...", _model.Id);
            await WithTimeout(_model.DownloadAsync(), DownloadTimeout, "Whisper model download", ct);

            logger.LogInformation("Loading Whisper model {ModelId}...", _model.Id);
            await WithTimeout(_model.LoadAsync(), LoadTimeout, "Whisper model load", ct);

            IsReady = true;
            logger.LogInformation("Whisper model ready: {ModelId}", _model.Id);
        }
        catch (Exception ex)
        {
            InitializationFailed = true;
            ErrorMessage = ex.Message;
            logger.LogError(ex, "Whisper initialization failed");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_model is null) return;

        logger.LogInformation("Unloading Whisper model {ModelId}", _model.Id);
        try
        {
            await _model.UnloadAsync();
            _model = null;
        }
        catch (Exception ex) { logger.LogWarning(ex, "Error unloading Whisper model"); }
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        EnsureReady();

        var extension = Path.GetExtension(audioFilePath).ToLowerInvariant();
        var fileToTranscribe = audioFilePath;
        string? convertedFilePath = null;

        if (OggExtensions.Contains(extension) && audioConverter is not null)
        {
            logger.LogDebug("Converting {Source} to WAV", audioFilePath);
            convertedFilePath = audioConverter.ConvertToWav(audioFilePath);
            fileToTranscribe = convertedFilePath;
        }

        try
        {
            return await RunTranscription(fileToTranscribe, ct);
        }
        finally
        {
            if (convertedFilePath is not null && File.Exists(convertedFilePath))
                File.Delete(convertedFilePath);
        }
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        var tempPath = Path.Combine(Path.GetTempPath(), $"ino_voice_{Guid.NewGuid()}{Path.GetExtension(fileName)}");
        try
        {
            await using (var fileStream = File.Create(tempPath))
                await audioStream.CopyToAsync(fileStream, ct);
            return await TranscribeAsync(tempPath, ct);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsReady || _model is null) return;
        try { await _model.UnloadAsync(); }
        catch { /* best-effort cleanup */ }
    }

    void EnsureReady()
    {
        if (IsReady) return;
        if (InitializationFailed)
            throw new InvalidOperationException($"Whisper is not available: {ErrorMessage}");
        throw new InvalidOperationException("Whisper model is still initializing");
    }

    // DownloadAsync/LoadAsync don't accept CancellationToken, so we race against Task.Delay.
    // On timeout the underlying task keeps running in the background — acceptable since
    // the service is marked as failed and won't serve transcription requests.
    static async Task WithTimeout(Task task, TimeSpan timeout, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
            throw new TimeoutException($"{operation} timed out after {timeout.TotalMinutes:F0} minutes");
        await task;
    }

    async Task InitializeFoundryManagerAsync(CancellationToken ct)
    {
        try
        {
            _ = FoundryLocalManager.Instance;
            logger.LogDebug("Foundry Local manager already initialized");
        }
        catch (FoundryLocalException)
        {
            logger.LogInformation("Creating new Foundry Local manager...");
            var config = new Configuration { AppName = "ino" };
            await FoundryLocalManager.CreateAsync(config, logger);
        }
    }

    async Task<string> RunTranscription(string filePath, CancellationToken ct)
    {
        logger.LogDebug("Transcribing: {Path}", filePath);
        var client = await _model!.GetAudioClientAsync();
        var result = new StringBuilder();

        await foreach (var chunk in client.TranscribeAudioStreamingAsync(filePath, ct))
            result.Append(chunk.Text);

        var transcription = result.ToString().Trim();
        logger.LogInformation("Transcription complete: {Length} chars", transcription.Length);
        return transcription;
    }

    async Task<IModel> ResolveModelAsync(ICatalog catalog)
    {
        // Explicit override via Telegram:WhisperModelId wins over the priority list.
        var configuredId = configuration["Telegram:WhisperModelId"];
        if (!string.IsNullOrEmpty(configuredId))
        {
            var configured = await catalog.GetModelAsync(configuredId);
            if (configured is not null)
            {
                logger.LogInformation("Using configured Whisper model: {ModelId}", configuredId);
                return configured;
            }
            logger.LogWarning("Configured whisper model '{ModelId}' not found in catalog, falling back", configuredId);
        }

        foreach (var modelId in WhisperModelPreference)
        {
            var found = await catalog.GetModelAsync(modelId);
            if (found is not null)
            {
                logger.LogInformation("Resolved Whisper model via fallback: {ModelId}", found.Id);
                return found;
            }
        }

        throw new InvalidOperationException(
            "No whisper model found in Foundry Local catalog. " +
            $"Expected one of: {string.Join(", ", WhisperModelPreference)}");
    }
}
