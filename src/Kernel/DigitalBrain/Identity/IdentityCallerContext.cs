using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Orleans.Runtime;

namespace DigitalBrain.Identity;

[GenerateSerializer, Alias("db.v2.identity.caller")]
public sealed record IdentityCaller([property: Id(0)] string UserId);

/// <summary>Only trusted HTTP authentication establishes this context. Orleans remains a private cluster.</summary>
public static class IdentityCallerContext
{
    private const string Key = "DigitalBrain.Identity.Caller";
    public static IdentityCaller? Current => RequestContext.Get(Key) as IdentityCaller;

    public static IApplicationBuilder UseIdentityCallerContext(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var previous = RequestContext.Get(Key);
            RequestContext.Remove(Key);
            if (context.Items[IdentityAuthentication.AuthenticatedIdentity] is AuthenticatedIdentity identity)
            {
                RequestContext.Set(Key, new IdentityCaller(identity.UserId));
            }
            try { await next(context).ConfigureAwait(false); }
            finally
            {
                if (previous is null) { RequestContext.Remove(Key); }
                else { RequestContext.Set(Key, previous); }
            }
        });
}

/// <summary>Until neuron resources carry workspace ownership, application sessions require the owner.</summary>
internal sealed class IdentityNeuronAuthorizationFilter(IdentityService identities) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron && IdentityCallerContext.Current is { } caller)
        {
            var account = await identities.GetAccountAsync(caller.UserId).ConfigureAwait(true);
            if (account is not { Disabled: false, IsOwner: true })
            {
                throw new UnauthorizedAccessException("This neuron is restricted to the workspace owner.");
            }
        }
        await context.Invoke().ConfigureAwait(true);
    }
}
