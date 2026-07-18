using Ino.Core;
using Ino.Core.Brain;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Validates that the per-(userId, sessionId) <see cref="IInoNeuron"/>
/// activation matches the identity keys in <see cref="RequestContext"/>.
/// Throws <see cref="InoInstanceMismatchException"/> on cross-user leakage.
/// Non-IInoNeuron grains pass through. Permissive when context is empty —
/// the kernel-side gateway hop sets the keys; an empty context generally
/// means a system-internal cluster-singleton call (Discovery, ProposalLog).
/// </summary>
public sealed class InoInstanceContextFilter(
    ILogger<InoInstanceContextFilter> logger) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is not IInoNeuron)
        {
            await context.Invoke();
            return;
        }

        var grainKey = context.TargetContext.GrainId.Key.ToString() ?? string.Empty;
        var ctxUserId = RequestContext.Get(InoRequestContextKeys.UserId) as string;
        var ctxSessionId = RequestContext.Get(InoRequestContextKeys.SessionId) as string;

        if (ctxUserId is null && ctxSessionId is null)
        {
            logger.LogDebug(
                "InoNeuron call to {GrainKey} with empty RequestContext — permissive pass-through.",
                grainKey);
            await context.Invoke();
            return;
        }

        var expected = $"{ctxUserId}/{ctxSessionId}";
        if (!string.Equals(grainKey, expected, StringComparison.Ordinal))
            throw new InoInstanceMismatchException(grainKey, ctxUserId, ctxSessionId);

        await context.Invoke();
    }
}
