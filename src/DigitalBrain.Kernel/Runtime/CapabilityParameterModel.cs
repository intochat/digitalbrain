using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel.Runtime;

public interface ICapabilityParameterModel
{
    Task<RetainedInoCapabilityPayload> ExtractAsync(CapabilityParameterRequest request, CancellationToken cancellationToken = default);
}

public sealed record CapabilityParameterRequest
{
    internal const int MaximumPromptLength = 4096;

    public CapabilityParameterRequest(CapabilityDescriptor descriptor, string prompt)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (prompt.Length > MaximumPromptLength || prompt.Any(char.IsControl))
            throw new ArgumentException("Capability extraction prompts must be at most 4096 characters and contain no control characters.", nameof(prompt));
        Descriptor = descriptor;
        Prompt = prompt;
    }

    public CapabilityDescriptor Descriptor { get; }
    public string CapabilityId => Descriptor.Id;
    public string Prompt { get; }
}

public sealed class CapabilityParameterModel : ICapabilityParameterModel
{
    private static readonly JsonElement ExtractionSchema = CreateExtractionSchema();
    private readonly IChatClient _chatClient;
    private readonly IFeatureDraftTemplate[] _templates;

    public CapabilityParameterModel(
        IChatClient chatClient,
        IEnumerable<IFeatureDraftTemplate> templates)
    {
        _chatClient = chatClient;
        _templates = templates.ToArray();
    }

    public CapabilityParameterModel(IChatClient chatClient)
        : this(chatClient, [])
    {
    }

    public async Task<RetainedInoCapabilityPayload> ExtractAsync(CapabilityParameterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var template in _templates)
        {
            if (template.TryCreatePayload(request.Descriptor, request.Prompt, out var payload))
                return payload;
        }

        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, BuildExtractionGuidance(request.Descriptor, request.Prompt))],
            new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    ExtractionSchema,
                    "retained_capability_payload",
                    "The server-selected capability id with the arguments extracted from the user's request.")
            },
            cancellationToken);

        var extracted = JsonSerializer.Deserialize<RetainedInoCapabilityPayload>(response.Text, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The extraction model returned no payload.");
        if (!string.Equals(extracted.ToolId, request.CapabilityId, StringComparison.Ordinal))
            throw new InvalidOperationException("The extraction model changed the selected capability.");

        return new RetainedInoCapabilityPayload(request.CapabilityId, extracted.Arguments);
    }

    private static JsonElement CreateExtractionSchema()
    {
        using var document = JsonDocument.Parse(
            """{"type":"object","properties":{"toolId":{"type":"string"},"arguments":{"type":"object"}},"required":["toolId","arguments"]}""");
        return document.RootElement.Clone();
    }

    private static string BuildExtractionGuidance(CapabilityDescriptor descriptor, string prompt) => $$"""
        The capability for this request has already been selected by the server and cannot be changed: {{descriptor.Id}} ({{descriptor.Name}}: {{descriptor.Description}}).
        Extract only the arguments for this capability from the user's request below. Do not select or propose a different capability.
        User request: {{prompt}}
        """;
}
