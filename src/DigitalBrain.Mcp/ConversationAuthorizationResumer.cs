using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Logging;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public interface IExternalAuthorizationProbe
{
    Task<ExternalAuthorizationResolution> ResolveAsync(
        CommandEnvelope command,
        ExternalAuthorizationContinuation authorization,
        CancellationToken cancellationToken = default);
}

public sealed class OrleansExternalAuthorizationProbe(IClusterClient cluster) : IExternalAuthorizationProbe
{
    public Task<ExternalAuthorizationResolution> ResolveAsync(
        CommandEnvelope command,
        ExternalAuthorizationContinuation authorization,
        CancellationToken cancellationToken = default)
    {
        var owner = RequestScope.Id(command.Context);
        return authorization.Provider switch
        {
            "google" => cluster.GetGrain<IGmailReadToolGrain>(owner)
                .ResolveAuthorizationAsync(cancellationToken),
            "salesforce" => cluster.GetGrain<ISalesforceReadToolGrain>(owner)
                .ResolveAuthorizationAsync(cancellationToken),
            _ => Task.FromResult(new ExternalAuthorizationResolution(
                ExternalAuthorizationResolutionState.Failed,
                "authorization-provider-unsupported"))
        };
    }
}

public sealed class ConversationAuthorizationResumer(
    ConversationStateClient conversations,
    McpInoCommandHandler handler,
    IExternalAuthorizationProbe probe,
    TimeProvider timeProvider,
    ILogger<ConversationAuthorizationResumer> logger)
{
    public async Task<bool> ResumeIfReadyAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await conversations.ReadAsync(context, cancellationToken).ConfigureAwait(false);
        context = context with { ConversationId = snapshot.ConversationId };
        var operation = snapshot.CurrentOperation;
        if (operation?.State != InoConversationStates.AwaitingAuthorization ||
            operation.Authorization is not { } authorization)
            return false;
        if (authorization.ExpiresAt <= timeProvider.GetUtcNow())
        {
            await conversations.FailAsync(
                context,
                operation.CommandId,
                "Authorization wasn’t completed in time. Send the request again when you’re ready.",
                retryable: true,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        var command = new CommandEnvelope(
            McpInoCommandHandler.CommandType,
            2,
            operation.CommandId,
            context,
            JsonSerializer.SerializeToElement(new { prompt = operation.Prompt }));
        ExternalAuthorizationResolution resolution;
        using (var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            probeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                resolution = await probe.ResolveAsync(command, authorization, probeTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Authorization readiness probe failed with {ExceptionType}.",
                    exception.GetType().Name);
                return false;
            }
        }
        if (resolution.State == ExternalAuthorizationResolutionState.Waiting) return false;
        if (!await conversations.TryClaimAuthorizationAsync(
                context,
                operation.CommandId,
                authorization.AttemptId,
                cancellationToken).ConfigureAwait(false))
            return false;

        using var executionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionTimeout.CancelAfter(TimeSpan.FromMinutes(2));
        await handler.ExecuteAsync(
            new CommandExecutionAttempt(command, authorization, resolution),
            executionTimeout.Token).ConfigureAwait(false);
        return true;
    }
}
