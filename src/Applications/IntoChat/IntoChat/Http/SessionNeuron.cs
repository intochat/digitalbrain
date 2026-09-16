using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Runtime;

namespace IntoChat;

internal static class SessionNeuron
{
    public static WebApplication UseSessionNeuron(this WebApplication app)
    {
        var options = IntoChatConfiguration.ResolveAuthOptions(app);
        app.Use(async (context, next) =>
        {
            var previous = RequestContext.Get(NeuronRequestKeys.Caller);
            RequestContext.Set(NeuronRequestKeys.Caller, For(options).ToString());
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
        => For(configuration.GetSection(BasicAuthOptions.SectionName).Get<BasicAuthOptions>() ?? new());

    public static NeuronId For(BasicAuthOptions options)
        => NeuronId.Plain(options.Username is { Length: > 0 } login ? login : BasicAuthGate.DefaultLogin);
}
