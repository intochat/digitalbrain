using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

internal static class SessionNeuron
{
    public static WebApplication UseSessionNeuron(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var previous = RequestContext.Get(NeuronRequestKeys.Caller);
            RequestContext.Set(NeuronRequestKeys.Caller, For(app.Configuration).ToString());
            try
            {
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                if (previous is null)
                {
                    RequestContext.Remove(NeuronRequestKeys.Caller);
                }
                else
                {
                    RequestContext.Set(NeuronRequestKeys.Caller, previous);
                }
            }
        });
        return app;
    }

    public static NeuronId For(IConfiguration configuration)
        => NeuronId.Plain(configuration[BasicAuthGate.UsernameConfigurationKey] is { Length: > 0 } login ? login : BasicAuthGate.DefaultLogin);
}
