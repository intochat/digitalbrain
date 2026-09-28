using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI;

public sealed class InferenceService(ModelProfiles profiles, IOptionsMonitor<AIOptions> settings)
{
    internal bool HasProfile(string name) => settings.CurrentValue.ModelProfiles.ContainsKey(name);
    internal ResolvedAgentModel Resolve(AgentModelSelection? selection, Type? marker = null, bool tools = false)
    {
        if (selection?.Capabilities is not null)
        { throw new ArgumentException("Capabilities must be declared in a host model profile.", nameof(selection)); }
        var preset = marker is null ? null : LLMModel.FindByMarker(marker)
            ?? throw new ArgumentException($"Unknown model marker {marker.Name}.", nameof(marker));
        if (preset is not null && string.IsNullOrWhiteSpace(selection?.Profile)
            && string.IsNullOrWhiteSpace(selection?.Model) && string.IsNullOrWhiteSpace(selection?.Provider))
        { selection = (selection ?? new()) with { Model = preset.Marker.Name }; }
        var resolved = profiles.Resolve(selection, tools);
        if (preset is not null)
        {
            var expected = profiles.Resolve(new(Model: preset.Marker.Name));
            if (resolved.Provider != expected.Provider || resolved.Model != expected.Model)
            { throw new ArgumentException($"The selected profile does not match typed model {preset.Marker.Name}.", nameof(selection)); }
        }
        return resolved;
    }

    public ModelDescriptor Describe(AgentModelSelection? selection = null, Type? marker = null)
    {
        var model = Resolve(selection, marker);
        return DescribeResolved(model, selection?.Capabilities);
    }

    public ModelDescriptor DescribeResolved(ResolvedAgentModel model, LlmCapabilities? declaredCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var configuration = settings.CurrentValue;
        var profile = model.Profile is not null ? configuration.ModelProfiles.GetValueOrDefault(model.Profile) : null;
        var declared = declaredCapabilities ?? profile?.Capabilities;
        if (profile?.ContextWindowTokens is <= 0 || profile?.MaximumOutputTokens is <= 0
            || profile?.MaximumOutputTokens > profile?.ContextWindowTokens)
        { throw new ArgumentException("Configured model token limits must be positive and output cannot exceed context."); }
        if (profile?.AllowedReasoning?.Any(value => value is not ("none" or "low" or "medium" or "high" or "xhigh")) == true)
        { throw new ArgumentException("AllowedReasoning contains an unsupported reasoning value."); }
        CapabilitySupport Support(LlmCapabilities capability) => model.Capabilities.HasFlag(capability)
            ? CapabilitySupport.Supported : declared is null ? CapabilitySupport.Unknown : CapabilitySupport.Unsupported;
        return new(model.Provider, model.Model, model.Profile, model.Revision,
            Support(LlmCapabilities.Tools), Support(LlmCapabilities.Vision), Support(LlmCapabilities.StructuredOutput),
            profile?.ContextWindowTokens, profile?.MaximumOutputTokens, SupportFlag(profile?.SupportsTemperature),
            SupportFlag(profile?.SupportsTopP), profile?.AllowedReasoning?.ToArray());
    }

    private static CapabilitySupport SupportFlag(bool? value)
        => value is null ? CapabilitySupport.Unknown : value.Value ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;

    public async Task<InferenceResult> Generate(InferenceRequest request, Type? marker = null, CancellationToken cancellationToken = default)
    {
        var (model, messages, options) = Prepare(request, marker);
        using var client = profiles.CreateInferenceClient(model);
        var result = await client.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        return new(result.Messages.Select(InferenceMapping.FromChatMessage).ToArray(), result.FinishReason?.Value,
            InferenceMapping.Usage(result.Usage), result.ResponseId, result.ModelId ?? model.Model);
    }

    public async IAsyncEnumerable<InferenceUpdate> GenerateStreaming(InferenceRequest request, Type? marker = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (model, messages, options) = Prepare(request, marker);
        using var client = profiles.CreateInferenceClient(model);
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return new(update.Role?.Value, update.Contents.Where(c => c is not UsageContent).Select(InferenceMapping.FromContent).ToArray(),
                update.FinishReason?.Value, InferenceMapping.Usage(update.Contents.OfType<UsageContent>().LastOrDefault()?.Details),
                update.ResponseId, update.MessageId, update.ModelId ?? model.Model);
        }
    }

    private (ResolvedAgentModel Model, List<ChatMessage> Messages, ChatOptions Options) Prepare(InferenceRequest request, Type? marker)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages is not { Count: > 0 }) { throw new ArgumentException("At least one message is required.", nameof(request)); }
        var selection = request.Model;
        if (request.Options?.Reasoning is not null || request.Options?.MaxOutputTokens is not null)
        {
            selection = (selection ?? new()) with
            {
                Reasoning = request.Options.Reasoning ?? selection?.Reasoning,
                MaxOutputTokens = request.Options.MaxOutputTokens ?? selection?.MaxOutputTokens,
            };
        }
        var model = Resolve(selection, marker, request.Tools is { Count: > 0 });
        var descriptor = DescribeResolved(model, selection?.Capabilities);
        InferenceMapping.ValidateRequest(request, descriptor);
        var options = InferenceMapping.CreateOptions(model, request.Options is null ? null : request.Options with { Reasoning = null }, descriptor);
        if (request.Tools is { Count: > 0 })
        {
            if (request.Tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != request.Tools.Count)
            { throw new ArgumentException("Tool names must be unique.", nameof(request)); }
            options.Tools = request.Tools.Select(tool => (AITool)AIFunctionFactory.CreateDeclaration(tool.Name, tool.Description,
                JsonSerializer.Deserialize<JsonElement>(tool.ParametersJson))).ToList();
        }
        return (model, request.Messages.Select(InferenceMapping.ToChatMessage).ToList(), options);
    }
}