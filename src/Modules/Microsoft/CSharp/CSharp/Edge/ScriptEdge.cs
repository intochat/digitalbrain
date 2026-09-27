using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Client;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Microsoft.CSharp;

// Everything a script does to the brain passes here: the run token must still speak for its file,
// only neuron contracts from installed contract assemblies are callable, and each call carries the
// file owner's caller context re-stamped as an app behind the app proxy.
internal sealed class ScriptEdge(RunTokens tokens, ScriptContracts contracts, IGrainFactory grains, IDigitalBrain brain)
{
    private static readonly HashSet<string> ObserverMethods = [nameof(INeuron.Watch), nameof(INeuron.Unwatch)];

    public async Task<JsonElement?> InvokeAsync(string? token, ScriptInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var (claims, owner) = await AuthorizeAsync(token).ConfigureAwait(false);
        var contract = contracts.Find(invocation.Contract);
        var method = contract.GetMethods().Concat(contract.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            .Where(candidate => candidate.Name == invocation.Method && !ObserverMethods.Contains(candidate.Name))
            .SingleOrDefault(candidate => candidate.GetParameters().Count(IsArgument) == invocation.Arguments.Length)
            ?? throw new ArgumentException($"{contract.Name} has no method {invocation.Method} taking {invocation.Arguments.Length} arguments.");
        var parameters = method.GetParameters();
        var values = new object?[parameters.Length];
        for (int index = 0, argument = 0; index < parameters.Length; index++)
        {
            values[index] = IsArgument(parameters[index])
                ? invocation.Arguments[argument++].Deserialize(parameters[index].ParameterType, ScriptEdgeProtocol.Json)
                : cancellationToken;
        }
        var neuron = grains.GetGrain(contract, invocation.Key);
        Stamp(owner, claims);
        object? returned;
        try { returned = method.Invoke(neuron, values); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
        if (returned is not Task task) { throw new NotSupportedException($"{contract.Name}.{method.Name} does not return a Task."); }
        await task.ConfigureAwait(false);
        if (!method.ReturnType.IsGenericType) { return null; }
        var result = method.ReturnType.GetProperty(nameof(Task<object>.Result))!.GetValue(task);
        return JsonSerializer.SerializeToElement(result, method.ReturnType.GetGenericArguments()[0], ScriptEdgeProtocol.Json);
    }

    // Authorizes and subscribes before returning, so a refusal surfaces before the stream starts.
    public async Task<IAsyncEnumerable<string>> OpenSignalsAsync(string? token, string contract, string key, string signal, CancellationToken cancellationToken)
    {
        var (claims, owner) = await AuthorizeAsync(token).ConfigureAwait(false);
        if (grains.GetGrain(contracts.Find(contract), key) is not INeuron source) { throw new ArgumentException($"{contract} is not a neuron."); }
        Stamp(owner, claims);
        var subscription = await brain.SubscribeAsync<Signal>(source, cancellationToken).ConfigureAwait(false);
        return Matching(subscription, signal, cancellationToken);
    }

    private static async IAsyncEnumerable<string> Matching(ISignalSubscription<Signal> subscription, string signal, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (subscription)
        {
            await foreach (var published in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (published.GetType().Name == signal) { yield return JsonSerializer.Serialize(published, published.GetType(), ScriptEdgeProtocol.Json); }
            }
        }
    }

    private async Task<(RunTokenClaims Claims, CallerContext? Owner)> AuthorizeAsync(string? token)
    {
        var claims = tokens.Validate(token) ?? throw new UnauthorizedAccessException("The run token is missing, invalid or expired.");
        var authorization = await grains.GetGrain<ICSharpFileEdge>(claims.File).Authorize(claims.Run).ConfigureAwait(false);
        return authorization.Active ? (claims, authorization.Owner) : throw new UnauthorizedAccessException("This run no longer speaks for its file.");
    }

    // A file started without a caller (tests, the platform) calls without a stamp, as grain code does.
    private static void Stamp(CallerContext? owner, RunTokenClaims claims)
    {
        if (owner is not null) { CallerContextStamper.Stamp(owner with { Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy, AppId = claims.File }); }
    }

    private static bool IsArgument(ParameterInfo parameter) => parameter.ParameterType != typeof(CancellationToken);
}
