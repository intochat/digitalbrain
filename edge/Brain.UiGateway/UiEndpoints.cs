using Brain.Contracts;

namespace Brain.UiGateway;

public static class UiEndpoints
{
    public const string DevCallerKey = "local-owner|actor/ui-dev|session/dev";

    public static Task<NeuronReceipt> InvokeAsync(
        IClusterClient client,
        string callerKey,
        string address,
        string contract,
        string inputJson,
        string commandId,
        long? expectedRevision) =>
        client.GetGrain<INeuron>(address).InvokeAsync(new NeuronInvocation(contract, inputJson, commandId, callerKey, expectedRevision));

    public static Task<NeuronSnapshot> ReadAsync(IClusterClient client, string address, string projection) =>
        client.GetGrain<INeuron>(address).ReadAsync(projection);

    public static Task<NeuronDescription> DescribeAsync(IClusterClient client, string address) =>
        client.GetGrain<INeuron>(address).DescribeAsync();

    public static object ToErrorPayload(BrainException exception)
    {
        var prefix = exception.Code + ": ";
        var detail = exception.Message.StartsWith(prefix, StringComparison.Ordinal)
            ? exception.Message[prefix.Length..]
            : exception.Message;
        return new { code = exception.Code, detail };
    }
}
