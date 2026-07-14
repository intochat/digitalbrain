using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using System.Text;
namespace DigitalBrain.Features.EmailSummarizer;

public sealed class EmailSummarizerFeature : IFeature
{
    private const string InputKind = "gmail.message.summary.requested.v1";
    private const string WorkflowId = "email-summary";
    private const string ModelOperationKey = "generate-summary";
    private const string SurfaceOperationKey = "publish-summary";
    private readonly IGmailMessageReader _gmail;
    public EmailSummarizerFeature(IGmailMessageReader gmail)
    {
        ArgumentNullException.ThrowIfNull(gmail);
        _gmail = gmail;
    }
    public async Task HandleAsync(FeatureInput input, IFeatureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(input.Kind, InputKind, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported Feature input kind: {input.Kind}.", nameof(input));
        }
        if (!input.Facts.TryGetValue("messageId", out var messageId))
        {
            throw new ArgumentException("Feature input requires messageId.", nameof(input));
        }
        var message = await _gmail.ReadAsync(new GmailMessageReadRequest(messageId), cancellationToken);
        var prefix = $"Summarize this email.\nSubject: {message.Subject}\nBody: ";
        var remainingBytes = 32_768 - Encoding.UTF8.GetByteCount(prefix);
        if (remainingBytes < 0)
        {
            throw new InvalidOperationException("Email subject exceeds the model prompt budget.");
        }
        var body = TruncateUtf8(message.PlainTextBody, remainingBytes);
        var prompt = prefix + body;
        var response = await context.Models.CompleteAsync(new ModelRequest(WorkflowId, prompt, ModelOperationKey), cancellationToken);
        context.Intents.AddTextSurface(new TextSurfaceIntent(SurfaceOperationKey, "Email summary", response.Text));
    }
    private static string TruncateUtf8(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }
        var bytes = 0;
        var characters = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maximumBytes)
            {
                break;
            }
            bytes += rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }
        return value[..characters];
    }
}
