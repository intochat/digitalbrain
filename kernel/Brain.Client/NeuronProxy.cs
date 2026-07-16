using Brain.Contracts;
using System.Reflection;
using System.Text.Json;

namespace Brain.Client;

public class NeuronProxy : DispatchProxy
{
    private static readonly MethodInfo InvokeContractAsyncMethod =
        typeof(NeuronProxy).GetMethod(nameof(InvokeContractAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
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
        return InvokeContractAsyncMethod.MakeGenericMethod(resultType).Invoke(this, [contract.Contract, argument]);
    }

    private async Task<TResult> InvokeContractAsync<TResult>(string contract, object? argument)
    {
        var invocation = new NeuronInvocation(contract, JsonSerializer.Serialize(argument, JsonOptions), Guid.NewGuid().ToString("N"), _callerKey);
        var receipt = await _client.GetGrain<INeuron>(_addressKey).InvokeAsync(invocation);
        return JsonSerializer.Deserialize<TResult>(receipt.OutputJson, JsonOptions)!;
    }
}
