using Orleans.Hosting;

namespace Brain.Kernel.Connections;

public static class ConnectionHosting
{
    public static ISiloBuilder AddBrainConnection(this ISiloBuilder silo) =>
        silo.AddBrainKind("connection", sp => new ConnectionKind(sp));
}
