using DigitalBrain.Core;
using DigitalBrain.Ino;

namespace DigitalBrain.Ino;

internal interface IInoIntentHandler
{
    IReadOnlyList<InoCapabilityRecord> Capabilities => [];
    Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken);
}

internal static class InoIntentHandlers
{
    public static IReadOnlyList<IInoIntentHandler> Default { get; } =
    [
        new RelationGraphInoIntentHandler(),
        new SchemaVisualizationInoIntentHandler(),
        new LlmSettingsInoIntentHandler(),
        new ApproveProposalInoIntentHandler(),
        new RunAutomationInoIntentHandler(),
        new SetLlmInoIntentHandler(),
        new GenericLlmInoIntentHandler()
    ];

    public static IReadOnlyList<InoCapabilityRecord> CapabilityRecords { get; } =
        Default.SelectMany(handler => handler.Capabilities)
            .Concat([
                HandlerCapability(
                    "automation_create",
                    "Automation creation",
                    "Stage a new reaction/automation proposal through the self-evolution rail.",
                    ["automation_create", "automation", "reaction"],
                    ["when signal then react", "on event create summary"],
                    "automation"),
                HandlerCapability(
                    "uikit_gallery",
                    "UiKit gallery",
                    "Show the UI component gallery.",
                    ["uikit_gallery", "uikit", "ui", "component", "gallery"],
                    ["ui kit gallery", "show components"],
                    "ui")
            ])
            .ToArray();

    public static InoCapabilityRecord HandlerCapability(
        string id,
        string displayName,
        string description,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> examples,
        string tier) =>
        new(id, displayName, description, aliases, examples, tier, "InoIntentHandlers", "InoHandler", "System");
}

internal sealed class RelationGraphInoIntentHandler : IInoIntentHandler
{
    public IReadOnlyList<InoCapabilityRecord> Capabilities { get; } =
    [
        InoIntentHandlers.HandlerCapability(
            "relation_graph",
            "Relation graph",
            "Render a relation graph for object relationships.",
            ["relation_graph", "relation", "graph"],
            ["draw relation graph", "show object relation"],
            "ui")
    ];

    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "relation_graph" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId, cancellationToken);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        await neuron.HandleRelationGraphIntentAsync(request, workspaceId, cancellationToken);
        return true;
    }
}

internal sealed class SchemaVisualizationInoIntentHandler : IInoIntentHandler
{
    public IReadOnlyList<InoCapabilityRecord> Capabilities { get; } =
    [
        InoIntentHandlers.HandlerCapability(
            "schema_viz",
            "Database schema visualization",
            "Inspect and render SQLite database schemas.",
            ["schema_viz", "schema", "database", "sqlite"],
            ["show database schema", "visualize sqlite database"],
            "ui")
    ];

    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "schema_viz" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return neuron.TryHandleSchemaVisualizationIntentAsync(request, workspaceId, cancellationToken);
    }
}

internal sealed class GenericLlmInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        await neuron.HandleGenericIntentAsync(request, workspaceId, cancellationToken);
        return true;
    }
}

internal sealed class LlmSettingsInoIntentHandler : IInoIntentHandler
{
    public IReadOnlyList<InoCapabilityRecord> Capabilities { get; } =
    [
        InoIntentHandlers.HandlerCapability(
            "llm_settings",
            "LLM settings",
            "View and update the active LLM provider.",
            ["llm_settings", "llm", "model", "settings"],
            ["show llm settings", "change llm provider"],
            "settings")
    ];

    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "llm_settings" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId, cancellationToken);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        await neuron.FireAsync(new InoResponse(request.Prompt, "LLM / model settings:", []), cancellationToken);
        return true;
    }
}

internal sealed class ApproveProposalInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "approve" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId, cancellationToken);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        await neuron.FireAsync(new InoResponse(request.Prompt, "Approved via handler (rail).", []), cancellationToken);
        return true;
    }
}

internal sealed class RunAutomationInoIntentHandler : IInoIntentHandler
{
    public IReadOnlyList<InoCapabilityRecord> Capabilities { get; } =
    [
        InoIntentHandlers.HandlerCapability(
            "automation_run",
            "Automation run",
            "Run an approved automation.",
            ["automation_run", "automation"],
            ["run automation"],
            "automation")
    ];

    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "run_automation" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId, cancellationToken);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        var reply = "Running the requested automation (preview or activated). Check the Tasks surface for results.";
        await neuron.FireAsync(new InoResponse(request.Prompt, reply, []), cancellationToken);
        return true;
    }
}

internal sealed class SetLlmInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "set_llm" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId, cancellationToken);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        await neuron.FireAsync(new InoResponse(request.Prompt, "LLM set command handled.", []), cancellationToken);
        return true;
    }
}
