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
        if (!IsBitcoinPriceIntent(request.Prompt))
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

    private static bool IsBitcoinPriceIntent(string prompt) =>
        prompt.Contains("bitcoin", StringComparison.OrdinalIgnoreCase) &&
        prompt.Contains("price", StringComparison.OrdinalIgnoreCase);
}

internal sealed class RelationGraphInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        if (!IsTwoObjectRelationIntent(request.Prompt))
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

    private static bool IsTwoObjectRelationIntent(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return (p.Contains("draw") || p.Contains("show") || p.Contains("visualize")) &&
               p.Contains("relation") &&
               (p.Contains("2 objects") || p.Contains("two objects") || p.Contains("object"));
    }
}

internal sealed class SchemaVisualizationInoIntentHandler : IInoIntentHandler
{
    public Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        if (!IsSchemaVisualizationIntent(request.Prompt))
        {
            return Task.FromResult(false);
        }

        return neuron.TryHandleSchemaVisualizationIntentAsync(request, workspaceId);
    }

    private static bool IsSchemaVisualizationIntent(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("schema") ||
               p.Contains("visualize database") ||
               p.Contains("visualize db") ||
               p.Contains("show database") ||
               p.Contains("show db");
    }
}

internal sealed class GmailInoIntentHandler : IInoIntentHandler
{
    public async Task<bool> TryHandleAsync(InoNeuron neuron, InoRequest request, string workspaceId)
    {
        if (!InoConnectorIntents.IsGmail(request.Prompt))
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
        if (!InoConnectorIntents.IsSalesforce(request.Prompt))
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