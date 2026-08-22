namespace DigitalBrain.AI;

public sealed class TestImageGeneration : IImageGeneration
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GeneratedKitImage(Convert.FromBase64String(OnePixelPngBase64), "image/png", "test-image"));
    }
}
