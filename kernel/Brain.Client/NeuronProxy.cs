using Brain.Contracts;
using System.Reflection;
using System.Text.Json;

namespace Brain.Client;

public class NeuronProxy : DispatchProxy
{
    private static readonly MethodInfo InvokeContractAsyncMethod =
        typeof(NeuronProxy).GetMethod(nameof(InvokeContractAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo InvokeReplyContractAsyncMethod =
        typeof(NeuronProxy).GetMethod(nameof(InvokeReplyContractAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    private IClusterClient _client = null!;
    private string _addressKey = null!;
    private string _callerKey = null!;

    public static T Create<T>(IClusterClient client, string addressKey, string callerKey) where T : class, INeuronContract
    {
        var proxy = Create<T, NeuronProxy>();
        var typedProxy = (NeuronProxy)(object)proxy;
        typedProxy._client = client;
        typedProxy._addressKey = addressKey;
        typedProxy._callerKey = callerKey;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new NotSupportedException("NeuronProxy requires a target method.");
        if (args is not [var argument] ||
            targetMethod.ReturnType is not { IsGenericType: true } returnType ||
            returnType.GetGenericTypeDefinition() != typeof(Task<>))
            throw new NotSupportedException($"{targetMethod.Name} must be shaped Task<TResult> Method(TArg argument).");

        var contract = targetMethod.GetCustomAttribute<NeuronContractAttribute>()
            ?? throw new NotSupportedException($"{targetMethod.Name} is missing [NeuronContract].");

        var resultType = returnType.GetGenericArguments()[0];
        var method = resultType.IsGenericType &&
            resultType.GetGenericTypeDefinition() == typeof(NeuronReply<>)
                ? InvokeReplyContractAsyncMethod.MakeGenericMethod(resultType.GetGenericArguments()[0])
                : InvokeContractAsyncMethod.MakeGenericMethod(resultType);
        return method.Invoke(this, [contract.Contract, argument]);
    }

    private async Task<TResult> InvokeContractAsync<TResult>(string contract, object? argument)
    {
        var receipt = await InvokeAsync(contract, argument);
        return JsonSerializer.Deserialize<TResult>(receipt.OutputJson, JsonOptions)!;
    }

    private async Task<NeuronReply<TResult>> InvokeReplyContractAsync<TResult>(string contract, object? argument)
    {
        var receipt = await InvokeAsync(contract, argument);
        var value = JsonSerializer.Deserialize<TResult>(receipt.OutputJson, JsonOptions)!;
        return new NeuronReply<TResult>(value, receipt.Revision, receipt.EffectKey);
    }

    private async Task<NeuronReceipt> InvokeAsync(string contract, object? argument)
    {
        var inputJson = argument is IRawJson rawJson
            ? rawJson.Json
            : JsonSerializer.Serialize(argument, JsonOptions);
        var invocation = new NeuronInvocation(contract, inputJson, Guid.NewGuid().ToString("N"), _callerKey);
        return await _client.GetGrain<INeuron>(_addressKey).InvokeAsync(invocation);
    }
}
