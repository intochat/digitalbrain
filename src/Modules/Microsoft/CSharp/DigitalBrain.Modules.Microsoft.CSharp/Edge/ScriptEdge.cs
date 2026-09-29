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

    // The doorbell is best-effort; a missed ring is caught by the next sweep.
    private static readonly TimeSpan DoorbellSweep = TimeSpan.FromSeconds(5);

    // Authorizes and registers the durable subscription before returning, so a refusal surfaces
    // before the stream starts. Delivery drains the file's persisted buffer; the file's own signals
    // are the doorbell that keeps a live stream prompt.
    public async Task<IAsyncEnumerable<string>> OpenSignalsAsync(string? token, string contract, string key, string signal, CancellationToken cancellationToken)
    {
        var (claims, owner) = await AuthorizeAsync(token).ConfigureAwait(false);
        if (grains.GetGrain(contracts.Find(contract), key) is not INeuron source) { throw new ArgumentException($"{contract} is not a neuron."); }
        Stamp(owner, claims);
        var file = grains.GetGrain<ICSharpFileEdge>(claims.File);
        await file.Subscribed(claims.Run, source.GetGrainId().ToString(), signal).ConfigureAwait(false);
        var doorbell = await brain.SubscribeAsync<Signal>(grains.GetGrain<ICSharpFile>(claims.File), cancellationToken).ConfigureAwait(false);
        return Draining(file, claims.Run, source.GetGrainId().ToString(), signal, doorbell, cancellationToken);
    }

    private static async IAsyncEnumerable<string> Draining(
        ICSharpFileEdge file, string run, string neuron, string signal,
        ISignalSubscription<Signal> doorbell, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (doorbell)
        {
            var rings = doorbell.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                var doorbellAlive = true;
                Task<bool>? ring = null;
                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var json in await file.DrainPending(run, neuron, signal).ConfigureAwait(false)) { yield return json; }
                    if (doorbellAlive)
                    {
                        ring ??= NextRingAsync(rings);
                        if (await Task.WhenAny(ring, Task.Delay(DoorbellSweep, cancellationToken)).ConfigureAwait(false) == ring)
                        {
                            doorbellAlive = await ring.ConfigureAwait(false);
                            ring = null;
                        }
                    }
                    else
                    {
                        await Task.Delay(DoorbellSweep, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally { await rings.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private static async Task<bool> NextRingAsync(IAsyncEnumerator<Signal> rings)
    {
        try { return await rings.MoveNextAsync().ConfigureAwait(false); }
        catch { return false; }
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
