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

    public CapabilityParameterRequest(string capabilityId, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (prompt.Length > MaximumPromptLength || prompt.Any(char.IsControl))
            throw new ArgumentException("Capability extraction prompts must be at most 4096 characters and contain no control characters.", nameof(prompt));
        CapabilityId = capabilityId;
        Prompt = prompt;
    }

    public string CapabilityId { get; }
    public string Prompt { get; }
}

public sealed class CapabilityParameterModel(IChatClient chatClient, ICapabilityCatalog catalog) : ICapabilityParameterModel
{
    public async Task<RetainedInoCapabilityPayload> ExtractAsync(CapabilityParameterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var descriptor = catalog.Snapshot().FirstOrDefault(candidate =>
                string.Equals(candidate.Id, request.CapabilityId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown capability '{request.CapabilityId}'.", nameof(request));

        var response = await chatClient.GetResponseAsync<RetainedInoCapabilityPayload>(
            new ChatMessage(ChatRole.User, BuildExtractionGuidance(descriptor, request.Prompt)),
            cancellationToken: cancellationToken);

        var extracted = response.Result;
        if (!string.Equals(extracted.ToolId, request.CapabilityId, StringComparison.Ordinal))
            throw new InvalidOperationException("The extraction model changed the selected capability.");

        return new RetainedInoCapabilityPayload(request.CapabilityId, extracted.Arguments);
    }

    private static string BuildExtractionGuidance(CapabilityDescriptor descriptor, string prompt) => $$"""
        The capability for this request has already been selected by the server and cannot be changed: {{descriptor.Id}} ({{descriptor.Name}}: {{descriptor.Description}}).
        Extract only the arguments for this capability from the user's request below. Do not select or propose a different capability.
        User request: {{prompt}}
        """;
}
