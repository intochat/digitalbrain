using System.ComponentModel;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

// Modules contribute AI tools without the AI module referencing them (IAgentToolSource,
// Task 4). This is the UI module's contribution: render_chart is always offered;
// generate_image only appears once an IImageGeneration provider is actually configured
// (the honesty gate UIModule.Configure applies via sp.GetService<IImageGeneration>()).
internal sealed class KitToolSource(
    IGrainFactory grains,
    IImageGeneration? imageGeneration,
    IKitImageStore imageStore) : IAgentToolSource
{
    public IReadOnlyList<AIFunction> ToolsFor(OwnerId owner)
    {
        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(RenderChartAsync, new AIFunctionFactoryOptions
            {
                Name = "render_chart",
                Description = "Render a chart for the owner. It appears as a live card in the chat and can "
                    + "be shown on surfaces later. Use it whenever the owner asks to see data as a chart.",
            }),
        };

        if (imageGeneration is not null)
        {
            tools.Add(AIFunctionFactory.Create(GenerateImageAsync, new AIFunctionFactoryOptions
            {
                Name = "generate_image",
                Description = "Generate an image from a text prompt and show it as a card in the chat. "
                    + "Use it whenever the owner asks for a picture, illustration, or image.",
            }));
        }

        return tools;
    }

    private async Task<string> RenderChartAsync(
        [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
        [Description("Short chart title")] string title,
        [Description("bar or line")] string chartKind,
        [Description("Point labels, one per value")] string[] labels,
        [Description("Point values, one per label")] double[] values,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "title must not be blank.";
        }

        if (labels.Length == 0 || labels.Length != values.Length)
        {
            return "labels and values must be non-empty and the same length.";
        }

        var trimmedTitle = title.Trim();
        var name = $"chart-{Guid.NewGuid():N}"[..14];
        var instance = KitInstanceNames.Sibling(chatName, name);
        var points = labels.Zip(values, static (label, value) => new ChartPoint(label, value)).ToList();

        await grains.GetGrain<IChart>(instance).Render(new ChartState(trimmedTitle, chartKind, points));
        await grains.GetGrain<IChat>(chatName)
            .HandleAsync(new KitCardOffer(KitCardKinds.Chart, name, trimmedTitle), cancellationToken);

        return $"Chart '{trimmedTitle}' is now showing in the chat as card '{name}'.";
    }

    private async Task<string> GenerateImageAsync(
        [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
        [Description("What the image should depict")] string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "prompt must not be blank.";
        }

        var trimmedPrompt = prompt.Trim();
        var generated = await imageGeneration!.GenerateAsync(trimmedPrompt, cancellationToken);

        var name = $"image-{Guid.NewGuid():N}"[..14];
        var blobName = $"{name}.png";
        await imageStore.SaveAsync(blobName, generated.Content, generated.MediaType, cancellationToken);

        var instance = KitInstanceNames.Sibling(chatName, name);
        await grains.GetGrain<IImage>(instance)
            .Describe(new ImageState(trimmedPrompt, generated.Model, generated.MediaType, blobName));
        await grains.GetGrain<IChat>(chatName)
            .HandleAsync(new KitCardOffer(KitCardKinds.Image, name, trimmedPrompt), cancellationToken);

        return $"Image for '{trimmedPrompt}' is now showing in the chat as card '{name}'.";
    }
}
