using DigitalBrain.Apps;
using DigitalBrain.Broker.Sandbox;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Broker;

// The one gateway for `process` apps. It is disabled by feature flag until the G-8 penetration
// test closes. When enabled it verifies the package before activation, checks the manifest data
// classes, denies egress outright (the sandbox has no network), re-stamps the caller as an app,
// asks the single call filter, runs the container and records the platform meter and observation.
public sealed class ProcessAppGateway(
    IProcessPackageVerifier packageVerifier,
    ISandboxRuntime sandboxRuntime,
    ICallFilter callFilter,
    IMeterSink meterSink,
    IAppObservationStore observations,
    IOptions<SandboxOptions> options) : IProcessAppGateway
{
    public async ValueTask<ProcessCallResult> InvokeAsync(
        ProcessCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return Deny(request, CallDenial.AppDisabled,
                "Process apps are disabled until the sandbox penetration test (G-8) closes.", null);
        }

        var package = request.Package;
        var manifest = package.Manifest;
        if (manifest.Kind != AppKind.Process)
        {
            return Deny(request, CallDenial.AppDisabled, "The process gateway only serves process apps.", null);
        }

        var verification = packageVerifier.Verify(package);
        if (!verification.Allowed)
        {
            return Deny(request, CallDenial.UntrustedCaller, string.Join(" ", verification.Reasons), null);
        }

        if (request.EgressHosts.Count > 0)
        {
            return Deny(request, CallDenial.UntrustedCaller,
                "A process app has no network; no egress host is reachable.", null);
        }

        var declaredDataClasses = manifest.Permissions
            .Select(permission => permission.SemanticTypeId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var dataClass in request.DataClasses)
        {
            if (!declaredDataClasses.Contains(dataClass))
            {
                return Deny(request, CallDenial.MissingGrant,
                    $"The call uses data class '{dataClass}', which the manifest does not declare.", null);
            }
        }

        var appCaller = CallerContextStamper.Stamp(request.Caller with
        {
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
            AppId = manifest.Id,
        });

        var decision = await callFilter.AuthorizeAsync(new CallRequest
        {
            Caller = appCaller,
            TargetNeuron = manifest.Id,
            Operation = request.Operation,
            SemanticTypeIds = request.DataClasses,
            EstimatedCompute = request.EstimatedCompute,
        }, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            return Deny(request, decision.Denial ?? CallDenial.UntrustedCaller,
                decision.Explanation ?? "Denied by the call filter.", null);
        }

        var spec = SandboxSpecFactory.Create(package, settings, request.Operation);
        var run = await sandboxRuntime.RunAsync(new SandboxRunRequest
        {
            Spec = spec,
            Operation = request.Operation,
        }, cancellationToken).ConfigureAwait(false);

        await RecordAsync(manifest, request, appCaller, run, cancellationToken).ConfigureAwait(false);

        if (!run.Succeeded)
        {
            return new ProcessCallResult
            {
                Allowed = true,
                Succeeded = false,
                Explanation = run.Failure ?? "The sandbox run failed.",
                AppId = manifest.Id,
                Operation = request.Operation,
                Diff = observations.Diff(manifest),
            };
        }

        return new ProcessCallResult
        {
            Allowed = true,
            Succeeded = true,
            AppId = manifest.Id,
            Operation = request.Operation,
            Output = run.Output,
            Diff = observations.Diff(manifest),
        };
    }

    private static ProcessCallResult Deny(
        ProcessCallRequest request, CallDenial denial, string explanation, DeclaredObservedDiff? diff) => new()
    {
        Allowed = false,
        Succeeded = false,
        Denial = denial,
        Explanation = explanation,
        AppId = request.Package.Manifest.Id,
        Operation = request.Operation,
        Diff = diff,
    };

    private async ValueTask RecordAsync(
        AppManifest manifest,
        ProcessCallRequest request,
        CallerContext appCaller,
        SandboxRunResult run,
        CancellationToken cancellationToken)
    {
        var observedDataClasses = request.DataClasses
            .Concat(run.UsedDataClasses)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        observations.Record(manifest.Id, observedDataClasses, run.UsedMeters);

        await meterSink.RecordAsync(new MeterEvent
        {
            IntentId = request.IntentId ?? request.Caller.IntentId ?? $"process:{manifest.Id}:{request.Operation}",
            MeterId = ProcessAppMeters.SandboxRun,
            Step = request.Operation,
            WorkspaceId = appCaller.WorkspaceId,
            Quantity = 1,
            Unit = ProcessAppMeters.SandboxRunUnit,
            Source = MeterSource.CallFilter,
            AppId = manifest.Id,
            OccurredAt = DateTimeOffset.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
    }
}