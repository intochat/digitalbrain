using Orleans.Hosting;

namespace Google;

public static class GoogleHosting
{
    public static ISiloBuilder AddGoogle(this ISiloBuilder silo)
    {
        _ = typeof(GmailNeuron);
        return silo;
    }
}
