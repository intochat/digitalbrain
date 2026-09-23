using DigitalBrain.Apps;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Broker;

// The one broker gateway. Every third-party call travels through it: the caller is re-stamped as an
// app at the trusted app-proxy edge, the manifest data classes are checked per call, egress is
// applied per data class, the single call filter decides, and only then is the publisher endpoint
// invoked. Platform meters and per-app observations are recorded on every call.
public sealed class BrokerGateway(
    ICallFilter callFilter,
    IMeterSink meterSink,
    IEgressPolicy egressPolicy,
    IAppObservationStore observations,
    IRemoteAppTransport transport) : IRemoteAppGateway
{
    public async ValueTask<RemoteCallResult> InvokeAsync(RemoteCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var manifest = request.Manifest;
        if (manifest.Kind != AppKind.Remote)
        {
            return Deny(request, CallDenial.AppDisabled, "The broker only serves remote apps.", null);
        }
        if (string.IsNullOrWhiteSpace(manifest.RemoteEndpoint))
        {
            return Deny(request, CallDenial.AppDisabled, "The remote app declares no endpoint.", null);
        }

        var declaredDataClasses = manifest.Permissions.Select(permission => permission.SemanticTypeId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var dataClass in request.DataClasses)
        {
            if (!declaredDataClasses.Contains(dataClass))
            {
                return Deny(request, CallDenial.MissingGrant,
                    $"The call uses data class '{dataClass}', which the manifest does not declare.", null);
            }
        }

        var egress = egressPolicy.Evaluate(manifest, request.DataClasses, request.EgressHosts);
        if (!egress.Allowed)
        {
            return Deny(request, CallDenial.UntrustedCaller, egress.Explanation ?? "Egress denied.", null);
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

        var response = await transport.InvokeAsync(manifest, request, cancellationToken).ConfigureAwait(false);

        await RecordAsync(manifest, request, appCaller, response, cancellationToken).ConfigureAwait(false);

        return new RemoteCallResult
        {
            Allowed = true,
            AppId = manifest.Id,
            Operation = request.Operation,
            Output = response.Output,
            EgressHosts = request.EgressHosts,
            Diff = observations.Diff(manifest),
        };
    }

    private static RemoteCallResult Deny(RemoteCallRequest request, CallDenial denial, string explanation, DeclaredObservedDiff? diff) => new()
    {
        Allowed = false,
        Denial = denial,
        Explanation = explanation,
        AppId = request.Manifest.Id,
        Operation = request.Operation,
        Diff = diff,
    };

    private async ValueTask RecordAsync(
        AppManifest manifest,
        RemoteCallRequest request,
        CallerContext appCaller,
        RemoteAppResponse response,
        CancellationToken cancellationToken)
    {
        var observedDataClasses = request.DataClasses
            .Concat(response.UsedDataClasses)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        observations.Record(manifest.Id, observedDataClasses, response.UsedMeters);

        await meterSink.RecordAsync(new MeterEvent
        {
            IntentId = request.IntentId ?? request.Caller.IntentId ?? $"remote:{manifest.Id}:{request.Operation}",
            MeterId = BrokerMeters.RemoteCall,
            Step = request.Operation,
            WorkspaceId = appCaller.WorkspaceId,
            Quantity = 1,
            Unit = BrokerMeters.RemoteCallUnit,
            Source = MeterSource.CallFilter,
            AppId = manifest.Id,
            OccurredAt = DateTimeOffset.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
    }
}
