using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain;

public static class DigitalBrainClientExtensions
{
    public static IClientBuilder AddDigitalBrainClient(this IClientBuilder clientBuilder)
    {
        clientBuilder.Services.AddSingleton<BrainOwnerContext>();
        clientBuilder.Services.AddSingleton<DigitalBrainSessionFactory>();
        clientBuilder.Services.AddScoped(static services =>
        {
            var owner = services.GetRequiredService<BrainOwnerContext>().Current
                ?? throw new BrainException(
                    NeuronFailureKind.AuthenticationRequired,
                    "An authenticated owner is required to construct DigitalBrainClient.");
            return new DigitalBrainClient(services.GetRequiredService<IClusterClient>(), owner);
        });
        clientBuilder.AddOutgoingGrainCallFilter<BrainOwnerOutgoingCallFilter>();
        return clientBuilder;
    }
}
