using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

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
