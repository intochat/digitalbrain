using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Ino;

internal interface IInoIntentHandler
{
    Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId);
}

internal static class InoIntentHandlers
{
    public static IReadOnlyList<IInoIntentHandler> Default { get; } =
    [
        new BitcoinPriceInoIntentHandler(),
        new RelationGraphInoIntentHandler(),
        new SchemaVisualizationInoIntentHandler(),
        new GmailInoIntentHandler(),
        new SalesforceInoIntentHandler(),
        new GenericLlmInoIntentHandler()
    ];
}

internal sealed class BitcoinPriceInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "bitcoin_price" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        await neuron.HandleBitcoinPriceIntentAsync(request, workspaceId);
        return true;
    }
}

internal sealed class RelationGraphInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "relation_graph" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return HandleAsync(neuron, request, workspaceId);
    }

    private static async Task<bool> HandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        await neuron.HandleRelationGraphIntentAsync(request, workspaceId);
        return true;
    }
}

internal sealed class SchemaVisualizationInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        var cls = InoIntentClassifier.Classify(request.Prompt);
        if (cls.Intent != "schema_viz" || cls.Confidence < 0.7)
        {
            return Task.FromResult(false);
        }

        return neuron.TryHandleSchemaVisualizationIntentAsync(request, workspaceId);
    }
}

internal sealed class GmailInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        // Uses classifier (keyword fast path + future LLM). Replaced direct Regex in InoConnectorIntents.
        var classification = InoIntentClassifier.Classify(request.Prompt);
        if (classification.Intent != "gmail" || classification.Confidence < 0.55)
        {
            return false;
        }

        await neuron.HandleGmailIntentAsync(request);
        return true;
    }
}

internal sealed class SalesforceInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        var classification = InoIntentClassifier.Classify(request.Prompt);
        if (classification.Intent != "salesforce" || classification.Confidence < 0.55)
        {
            return false;
        }

        await neuron.HandleSalesforceIntentAsync(request);
        return true;
    }
}

internal sealed class GenericLlmInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        await neuron.HandleGenericIntentAsync(request, workspaceId);
        return true;
    }
}