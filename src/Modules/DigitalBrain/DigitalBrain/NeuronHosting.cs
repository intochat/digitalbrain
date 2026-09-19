using DigitalBrain.Contracts;

namespace DigitalBrain.Core;

public static class NeuronHosting
{
    public static ISiloBuilder AddNeuronBroadcast(this ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.AddBroadcastChannel(NeuronBroadcast.Provider);
        return silo;
    }

    public static IClientBuilder AddNeuronBroadcast(this IClientBuilder client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.AddBroadcastChannel(NeuronBroadcast.Provider);
        return client;
    }
}
