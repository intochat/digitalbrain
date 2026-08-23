namespace DigitalBrain.AI;

public sealed record GeneratedKitImage(byte[] Content, string MediaType, string Model);

public interface IImageGeneration
{
    Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
