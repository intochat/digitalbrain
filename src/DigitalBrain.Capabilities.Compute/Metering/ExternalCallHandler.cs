using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Capabilities.Compute;

public sealed partial class ExternalCallHandler(ICostMeter meter, ModulePriceListRegistry registry, IExternalCallContextAccessor context,
    TimeProvider? clock = null, ILogger<ExternalCallHandler>? logger = null)
    : DelegatingHandler
{
    private static readonly TimeSpan AccountingTimeout = TimeSpan.FromSeconds(10);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.Options.TryGetValue(ExternalCallHttp.Metadata, out var metadata))
        { throw new InvalidOperationException("Enrolled HTTP requests require SetExternalCall metadata."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RequireTraceId());
        var operation = registry.GetOperation(metadata.Operation);
        request.Headers.Remove("x-intochatos-funding");
        var handle = await meter.BeginExternalCallAsync(new()
        {
            Operation = operation.Name,
            Quantity = 1,
            Unit = operation.Unit,
            Funding = metadata.Funding,
            Subject = metadata.Subject,
            InvocationId = metadata.InvocationId,
            CostId = Guid.NewGuid().ToString("N"),
        }, cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await meter.MarkExternalCallDispatchStartedAsync(handle, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            // This executor has not invoked transport, even if the durable intent was written.
            await Complete(handle, new(ExternalCallOutcomeKind.CancelledBeforeDispatch));
            throw;
        }

        HttpResponseMessage response;
        try { response = await base.SendAsync(request, cancellationToken); }
        catch (Exception exception)
        {
            await Complete(handle, new(exception is OperationCanceledException
                ? ExternalCallOutcomeKind.CancelledAfterDispatch : ExternalCallOutcomeKind.TransportFailed));
            throw;
        }

        var completion = await Complete(handle, new(response.IsSuccessStatusCode
            ? ExternalCallOutcomeKind.Succeeded : ExternalCallOutcomeKind.HttpFailed, (int)response.StatusCode, ProviderRequestId(response)));
        response.RequestMessage ??= request;
        response.RequestMessage.Options.Set(ExternalCallHttp.Accounting, completion);
        return response;
    }

    private async Task<ExternalCallCompletion> Complete(ExternalCallHandle handle, ExternalCallOutcome outcome)
    {
        using var accounting = new CancellationTokenSource(AccountingTimeout, clock ?? TimeProvider.System);
        try { return await meter.CompleteExternalCallAsync(handle, outcome, accounting.Token).AsTask().WaitAsync(accounting.Token); }
        catch (Exception exception)
        {
            // The durable reservation remains repairable; never resend an upstream call to repair accounting.
            // Exception messages can contain transport details supplied by custom meters.
            LogAccountingPending(logger ?? NullLogger<ExternalCallHandler>.Instance, exception.GetType().Name);
            return new(handle, ExternalAccountingStatus.Pending);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "External call accounting remains pending after {FailureType}.")]
    private static partial void LogAccountingPending(ILogger logger, string failureType);

    private static string? ProviderRequestId(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("x-request-id", out var values)) { return null; }
        var id = values.Take(2).ToArray();
        return id.Length == 1 && id[0].Length is > 0 and <= 128
            && id[0].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') ? id[0] : null;
    }
}
