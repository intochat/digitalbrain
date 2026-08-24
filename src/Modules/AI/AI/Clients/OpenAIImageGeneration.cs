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
            OptionsFor(model),
            cancellationToken).ConfigureAwait(false);

        var bytes = image.Value.ImageBytes
            ?? throw new InvalidOperationException(
                $"{model.DisplayName} returned no image bytes. A model that answers with a URL must set "
                + $"{nameof(ImageModel.AcceptsResponseFormat)} so bytes are requested explicitly.");

        return new GeneratedKitImage(bytes.ToArray(), model.MediaType, model.Id);
    }

    // Asking gpt-image-1 for a response format is HTTP 400 unknown_parameter: it
    // always answers with base64. Only models that accept the option get it.
    internal static ImageGenerationOptions OptionsFor(ImageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.AcceptsResponseFormat
            ? new ImageGenerationOptions { ResponseFormat = GeneratedImageFormat.Bytes }
            : new ImageGenerationOptions();
    }
}
