using System.ClientModel;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Images;

namespace DigitalBrain.AI;

internal sealed class OpenAIImageGeneration(IConfiguration configuration) : IImageGeneration
{
    private const string DefaultModel = "gpt-image-1";

    public async Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var model = configuration[$"{AIClients.ConfigurationRoot}:OpenAI:ImageModel"] ?? DefaultModel;
        var apiKey = configuration[$"{AIClients.ConfigurationRoot}:OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Image generation requires DigitalBrain:AI:OpenAI:ApiKey.");

        var client = new OpenAIClient(new ApiKeyCredential(apiKey)).GetImageClient(model);
        var image = await client.GenerateImageAsync(
            prompt,
            new ImageGenerationOptions { ResponseFormat = GeneratedImageFormat.Bytes },
            cancellationToken).ConfigureAwait(false);

        return new GeneratedKitImage(image.Value.ImageBytes.ToArray(), "image/png", model);
    }
}
