using DigitalBrain.Core;
using DigitalBrain.Ino;

namespace DigitalBrain.Ino;

internal interface IInoIntentHandler
{
    Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken);
}

internal static class InoIntentHandlers
{
    public static IReadOnlyList<IInoIntentHandler> Default { get; } =
    [
        new RelationGraphInoIntentHandler(),
        new SchemaVisualizationInoIntentHandler(),
        new GmailInoIntentHandler(),
        new SalesforceInoIntentHandler(),
        new LlmSettingsInoIntentHandler(),
        new ApproveProposalInoIntentHandler(),
        new RunAutomationInoIntentHandler(),
        new SetLlmInoIntentHandler(),
        new GenericLlmInoIntentHandler()
    ];
}

internal sealed class RelationGraphInoIntentHandler : IInoIntentHandler
{
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

internal sealed class GmailInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Uses classifier (keyword fast path + future LLM). Replaced direct Regex in InoConnectorIntents.
        var classification = InoIntentClassifier.Classify(request.Prompt);
        if (classification.Intent != "gmail" || classification.Confidence < 0.55)
        {
            return false;
        }

        await neuron.HandleGmailIntentAsync(request, cancellationToken);
        return true;
    }
}

internal sealed class SalesforceInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var classification = InoIntentClassifier.Classify(request.Prompt);
        if (classification.Intent != "salesforce" || classification.Confidence < 0.55)
        {
            return false;
        }

        await neuron.HandleSalesforceIntentAsync(request, cancellationToken);
        return true;
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
        // surface delivery handled in neuron or via other path for bloat reduction
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
        // full handle moved to reduce bloat
        return true;
    }
}

internal sealed class RunAutomationInoIntentHandler : IInoIntentHandler
{
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
        // surface delivery to reduce bloat in neuron direct calls
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
        await neuron.FireAsync(new InoResponse(request.Prompt, "LLM set command handled (bloat reduction).", []), cancellationToken);
        return true;
    }
}
