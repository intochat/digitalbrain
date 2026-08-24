using System.ClientModel;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Images;

namespace DigitalBrain.AI;

internal sealed class OpenAIImageGeneration(ImageModel model, IConfiguration configuration) : IImageGeneration
{
    public async Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var apiKeyKey = $"{AIClients.ConfigurationRoot}:{model.Provider}:ApiKey";
        var apiKey = configuration[apiKeyKey]
            ?? throw new InvalidOperationException($"Image generation requires {apiKeyKey}.");

        var client = new OpenAIClient(new ApiKeyCredential(apiKey)).GetImageClient(model.Id);
        var image = await client.GenerateImageAsync(
            prompt,
            new ImageGenerationOptions { ResponseFormat = GeneratedImageFormat.Bytes },
            cancellationToken).ConfigureAwait(false);

        return new GeneratedKitImage(image.Value.ImageBytes.ToArray(), model.MediaType, model.Id);
    }
}
