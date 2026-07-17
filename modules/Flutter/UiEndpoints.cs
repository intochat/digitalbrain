using System.Security.Claims;
using Brain.Contracts;

namespace Brain.Modules.Flutter;

public static class UiEndpoints
{
    public static async Task<NeuronReceipt> InvokeAsync(
        IClusterClient client,
        ClaimsPrincipal principal,
        FlutterGatewayPolicy policy,
        string address,
        string contract,
        string inputJson,
        string commandId,
        long? expectedRevision)
    {
        var session = FlutterGatewaySession.FromPrincipal(principal);
        var target = policy.AuthorizeMutation(session, address, contract, inputJson, commandId);
        return await client.GetGrain<INeuron>(target.ToGrainKey()).InvokeAsync(
            new NeuronInvocation(contract, inputJson, commandId, session.CallerKey, expectedRevision));
    }

    public static Task<NeuronSnapshot> ReadAsync(
        IClusterClient client,
        ClaimsPrincipal principal,
        FlutterGatewayPolicy policy,
        string address,
        string projection)
    {
        var session = FlutterGatewaySession.FromPrincipal(principal);
        var target = policy.AuthorizeTarget(session, address);
        return client.GetGrain<INeuron>(target.ToGrainKey()).ReadAsync(projection);
    }

    public static Task<NeuronDescription> DescribeAsync(
        IClusterClient client,
        ClaimsPrincipal principal,
        FlutterGatewayPolicy policy,
        string address)
    {
        var session = FlutterGatewaySession.FromPrincipal(principal);
        var target = policy.AuthorizeTarget(session, address);
        return client.GetGrain<INeuron>(target.ToGrainKey()).DescribeAsync();
    }

    public static object ToErrorPayload(BrainException exception)
    {
        var prefix = exception.Code + ": ";
        var detail = exception.Message.StartsWith(prefix, StringComparison.Ordinal)
            ? exception.Message[prefix.Length..]
            : exception.Message;
        return new { code = exception.Code, detail };
    }
}
