using DigitalBrain.AI.Interactions;

namespace DigitalBrain.Tests;

internal sealed class ScriptedContentScreen : IUntrustedContentScreen
{
    public Task ScreenAsync(string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Contains("ignore all previous instructions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("External content refused.");
        }

        return Task.CompletedTask;
    }
}
