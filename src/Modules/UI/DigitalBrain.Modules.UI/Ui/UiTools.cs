using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

internal sealed class UiTools(
    IGrainFactory grains,
    INeuronInvoker invoker,
    IImageGeneration? imageGeneration,
    IUiImageStore imageStore)
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    internal const string InvalidChat = "chatName must be a uichat neuron. Copy the current chat exactly from the Chat: line in the conversation context (for example, uichat:desk).";

    internal IReadOnlyList<AIFunction> Create()
    {
        Task<JsonElement> RenderChart(
            [Description("Short chart title")] string title,
            [Description("bar or line")] string chartKind,
            [Description("Point labels, one per value")] string[] labels,
            [Description("Point values, one per label")] double[] values,
            CancellationToken cancellationToken,
            [Description("The current chat, exactly as stated in the conversation context; omit when the context names none")] string? chatName = null)
            => RenderChartAsync(chatName, title, chartKind, labels, values, cancellationToken);

        Task<string> ShowGraph(
            [Description("The current chat, exactly as stated in the conversation context")] string chatName,
            [Description("Short graph title")] string title,
            [Description("Node ids, one per node. The first id is the graph's centre")] string[] nodeIds,
            [Description("Node labels, one per node id, in the same order")] string[] nodeLabels,
            [Description("Edges as 'sourceId>targetId', one per edge")] string[] edges,
            CancellationToken cancellationToken)
            => ShowGraphAsync(chatName, title, nodeIds, nodeLabels, edges, cancellationToken);

        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(RenderChart, new AIFunctionFactoryOptions
            {
                Name = "render_chart",
                Description = "Render a bar or line chart from labels and values. It appears as a live card "
                    + "in the chat when a chat is named and opens in the workspace otherwise. Use it whenever "
                    + "the person asks to see data as a chart.",
            }),
            AIFunctionFactory.Create(ShowGraph, new AIFunctionFactoryOptions
            {
                Name = "show_graph",
                Description = "Render a navigable graph. It appears as a live card in the chat and can be "
                    + "shown on surfaces later. Use it whenever the person asks to see how things connect, "
                    + "relate, or depend on each other.",
            }),
        };

        if (imageGeneration is { } generator)
        {
            Task<string> GenerateImage(
                [Description("The current chat, exactly as stated in the conversation context")] string chatName,
                [Description("What the image should depict")] string prompt,
                CancellationToken cancellationToken)
                => GenerateImageAsync(generator, chatName, prompt, cancellationToken);

            tools.Add(AIFunctionFactory.Create(GenerateImage, new AIFunctionFactoryOptions
            {
                Name = "generate_image",
                Description = "Generate an image from a text prompt and show it as a card in the chat. "
                    + "Use it whenever the person asks for a picture, illustration, or image.",
            }));
        }

        return tools;
    }

    private async Task<string> ShowGraphAsync(
        string chatName,
        string title,
        string[] nodeIds,
        string[] nodeLabels,
        string[] edges,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!NeuronId.TryParse(chatName, out var chat) || chat.Type != UIVocabulary.ChatType)
            {
                return InvalidChat;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return "title must not be blank.";
            }

            if (nodeIds.Length == 0)
            {
                return "nodeIds must contain at least one node.";
            }

            if (nodeIds.Length != nodeLabels.Length)
            {
                return "nodeIds and nodeLabels must have the same length.";
            }

            var known = nodeIds.Select(static id => id.Trim()).ToHashSet(StringComparer.Ordinal);

            // The first id anchors the graph's centre; every other node sits on the shell.
            var nodes = nodeIds
                .Select((id, index) => new GraphNodeState(
                    id.Trim(),
                    nodeLabels[index].Trim(),
                    index == 0 ? GraphNodeKinds.Hub : GraphNodeKinds.Leaf))
                .ToArray();

            var parsed = new List<GraphEdgeState>();
            foreach (var edge in edges)
            {
                var parts = edge.Split('>', 2);
                if (parts.Length != 2
                    || !known.Contains(parts[0].Trim())
                    || !known.Contains(parts[1].Trim()))
                {
                    return $"edge '{edge}' must be 'sourceId>targetId' using ids from nodeIds.";
                }

                var source = parts[0].Trim();
                var target = parts[1].Trim();
                parsed.Add(new GraphEdgeState($"{source}-{target}", source, target));
            }

            var trimmedTitle = title.Trim();
            var name = $"graph-{Guid.NewGuid():N}"[..14];
            var neuron = new NeuronId(UIVocabulary.GraphType, name);

            // Connect first: the reaction fires the card signal and needs a chat listener.
            await grains.GetGrain<INeuron>(neuron.ToGrainId()).Connect(chat, UIVocabulary.GraphRendered).ConfigureAwait(false);
            await invoker.InvokeAsync(neuron, "ui.graph", "render",
                JsonSerializer.SerializeToElement(new RenderGraph(CommandId.New(), trimmedTitle, nodes, parsed),
                    UIJson.Default.RenderGraph), cancellationToken).ConfigureAwait(false);
            await WaitForCardAsync(chat, UiCardKinds.Graph, name, cancellationToken).ConfigureAwait(false);

            return $"Graph '{trimmedTitle}' is now showing in the chat as card '{name}'.";
        }
        catch (Exception error) when (error is not OperationCanceledException && !TransientFailure.Covers(error))
        {
            return $"show_graph failed: {error.GetType().Name}: {error.Message}";
        }
    }

    private async Task<JsonElement> RenderChartAsync(
        string? chatName,
        string title,
        string chartKind,
        string[] labels,
        double[] values,
        CancellationToken cancellationToken)
    {
        try
        {
            NeuronId? chat = null;
            if (!string.IsNullOrWhiteSpace(chatName))
            {
                if (!NeuronId.TryParse(chatName, out var named) || named.Type != UIVocabulary.ChatType)
                {
                    return Text(InvalidChat);
                }

                chat = named;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return Text("title must not be blank.");
            }

            labels ??= [];
            values ??= [];
            if (labels.Length == 0 || labels.Length != values.Length)
            {
                return Text("labels and values must be non-empty and the same length.");
            }

            var trimmedTitle = title.Trim();
            var kind = string.IsNullOrWhiteSpace(chartKind) ? "bar" : chartKind.Trim();
            var name = $"chart-{Guid.NewGuid():N}"[..14];
            var neuron = new NeuronId(UIVocabulary.ChartType, name);
            var points = labels.Zip(values, static (label, value) => new ChartPoint(label, value)).ToList();

            if (chat is { } listener)
            {
                // Connect first: the reaction fires the card signal and needs a chat listener.
                await grains.GetGrain<INeuron>(neuron.ToGrainId()).Connect(listener, UIVocabulary.ChartRendered).ConfigureAwait(false);
            }

            await invoker.InvokeAsync(neuron, "ui.chart", "render",
                JsonSerializer.SerializeToElement(new RenderChart(CommandId.New(), trimmedTitle, kind, points),
                    UIJson.Default.RenderChart), cancellationToken).ConfigureAwait(false);

            if (chat is { } target)
            {
                await WaitForCardAsync(target, UiCardKinds.Chart, name, cancellationToken).ConfigureAwait(false);
            }

            // The chart data rides along so a chat without cards (the workspace) can draw it from this result.
            return JsonSerializer.SerializeToElement(new
            {
                kind = UiCardKinds.Chart,
                id = name,
                title = trimmedTitle,
                chartKind = kind,
                points = points.Select(static point => new { point.Label, point.Value }),
                message = chat is null
                    ? $"Chart '{trimmedTitle}' is saved as '{name}'; the workspace opens it from this result."
                    : $"Chart '{trimmedTitle}' is now showing in the chat as card '{name}'.",
            }, WireJson);
        }
        catch (Exception error) when (error is not OperationCanceledException && !TransientFailure.Covers(error))
        {
            return Text($"render_chart failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static JsonElement Text(string message) => JsonSerializer.SerializeToElement(message, WireJson);

    private async Task<string> GenerateImageAsync(
        IImageGeneration generator,
        string chatName,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!NeuronId.TryParse(chatName, out var chat) || chat.Type != UIVocabulary.ChatType)
            {
                return InvalidChat;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return "prompt must not be blank.";
            }

            var trimmedPrompt = prompt.Trim();
            var generated = await generator.GenerateAsync(trimmedPrompt, cancellationToken).ConfigureAwait(false);

            var name = $"image-{Guid.NewGuid():N}"[..14];
            var blobName = $"{name}.png";
            await imageStore.SaveAsync(blobName, generated.Content, generated.MediaType, cancellationToken).ConfigureAwait(false);

            var neuron = new NeuronId(UIVocabulary.ImageType, name);

            // Connect first: the reaction fires the card signal and needs a chat listener.
            await grains.GetGrain<INeuron>(neuron.ToGrainId()).Connect(chat, UIVocabulary.ImageDescribed).ConfigureAwait(false);
            await invoker.InvokeAsync(neuron, "ui.image", "describe",
                JsonSerializer.SerializeToElement(new DescribeImage(CommandId.New(), trimmedPrompt, generated.Model, generated.MediaType, blobName),
                    UIJson.Default.DescribeImage), cancellationToken).ConfigureAwait(false);
            await WaitForCardAsync(chat, UiCardKinds.Image, name, cancellationToken).ConfigureAwait(false);

            return $"Image for '{trimmedPrompt}' is now showing in the chat as card '{name}'.";
        }
        catch (Exception error) when (error is not OperationCanceledException && !TransientFailure.Covers(error))
        {
            return $"generate_image failed: {error.GetType().Name}: {error.Message}";
        }
    }

    private async Task WaitForCardAsync(NeuronId chat, string kind, string name, CancellationToken cancellationToken)
    {
        // Render commands acknowledge admission, not completion. The responder must not
        // settle the turn until the chat has applied the card offer to its snapshot.
        var target = grains.GetGrain<IChat>(chat.ToGrainId());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                var turns = await target.ReadTurns(new ReadTurns()).WaitAsync(deadline.Token).ConfigureAwait(false);
                if (turns.Turns.Any(turn => turn.Cards?.Any(card => card.Kind == kind && card.Name == name) == true))
                {
                    return;
                }

                await Task.Delay(25, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Card '{name}' has not reached chat '{chat}' yet.");
        }
    }
}
