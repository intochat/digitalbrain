using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;

namespace Ino.Telegram.Host.Services;

/// <summary>
/// Voice-message download path. Telegram delivers voice payloads as a file_id
/// reference — we resolve that to a temp .ogg via the Bot API file endpoint,
/// hand it to the configured <see cref="IAudioTranscriptionService"/>, and
/// clean up the temp file when transcription completes (success or failure).
///
/// <para>The legacy bot also handled photo/document uploads to Azure Blob
/// Storage via a kernel-side <c>BlobFileStorage</c> service. That dependency
/// is gone in the POC — bring it back when the bot needs to deliver media,
/// not before.</para>
/// </summary>
public sealed class TelegramFileService(
    ITelegramBotClient botClient,
    IOptions<TelegramBotOptions> options,
    IHttpClientFactory httpClientFactory,
    IAudioTranscriptionService transcriptionService)
{
    public async Task<string> TranscribeVoiceAsync(string fileId, CancellationToken ct)
    {
        var file = await botClient.GetFileAsync(fileId);
        var downloadUrl = $"{botClient.Options.ServerAddress}/file/bot{options.Value.BotToken}/{file.FilePath}";

        using var http = httpClientFactory.CreateClient();
        await using var responseStream = await http.GetStreamAsync(downloadUrl, ct);

        var tempPath = Path.Combine(Path.GetTempPath(), $"ino_voice_{Guid.NewGuid()}.ogg");
        try
        {
            await using (var fileStream = File.Create(tempPath))
                await responseStream.CopyToAsync(fileStream, ct);
            return await transcriptionService.TranscribeAsync(tempPath, ct);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
