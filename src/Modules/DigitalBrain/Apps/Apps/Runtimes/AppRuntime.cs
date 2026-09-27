using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

// How an app that is configuration on top of neurons answers an invocation, for example a group chat
// app asking IGroupChat. The host registers one per runtime name; "csharp" apps answer from their script instead.
public interface IAppRuntime
{
    string Name { get; }
    Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken);
}

public sealed record AppRuntimeRequest(
    string AppKey,
    Guid InvocationId,
    string Operation,
    string Input,
    PackageContent Content,
    IReadOnlyDictionary<string, string> Settings);

// Answers run here, off the app neuron, so a slow runtime such as a group chat never blocks the app's
// other calls. The result comes back through IApp.Respond exactly like a script's answer.
[Alias("apps.runtime-worker"), Orleans.Metadata.DefaultGrainType("apps.runtime-worker")]
public interface IAppRuntimeWorker : IGrainWithIntegerKey
{
    [OneWay] Task Answer(string appKey, PackageRevisionRef revision, AppInvocation invocation);
}

[GrainType("apps.runtime-worker"), StatelessWorker, Reentrant]
internal sealed class AppRuntimeWorker(IEnumerable<IAppRuntime> runtimes) : Grain, IAppRuntimeWorker
{
    public async Task Answer(string appKey, PackageRevisionRef revisionRef, AppInvocation invocation)
    {
        var app = GrainFactory.GetGrain<IApp>(appKey);
        string? output = null;
        string? error = null;
        try
        {
            var snapshot = await app.Read();
            var revision = await GrainFactory.GetGrain<IPackage>(revisionRef.Package.ToString()).ReadRevision(revisionRef.Revision);
            var runtimeName = revision.Content.Manifest.RuntimeName;
            var runtime = runtimes.FirstOrDefault(candidate => candidate.Name == runtimeName)
                ?? throw new InvalidOperationException($"This brain has no '{runtimeName}' app runtime.");
            output = await runtime.Answer(new(appKey, invocation.Id, invocation.Operation, invocation.Input, revision.Content, snapshot.Settings), CancellationToken.None);
        }
        catch (Exception failure)
        {
            error = failure.Message;
        }
        // Nobody awaits a one-way call, so a rejected answer must still complete the invocation.
        try { await app.Respond(new AppResponse(invocation.Id, output, error)); }
        catch (ArgumentException rejected) { await app.Respond(new AppResponse(invocation.Id, null, rejected.Message)); }
    }
}

public static class AppRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddAppRuntime<TRuntime>(this IServiceCollection services) where TRuntime : class, IAppRuntime
        => services.AddSingleton<IAppRuntime, TRuntime>();
}
