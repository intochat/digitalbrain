using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Ino.Core.Hosting.Brain;

public static class InoBrainStreamingExtensions
{
    /// Registers the incoming-grain-call filters and the in-process
    /// <see cref="BrainPulseHub"/> that backs both <see cref="IBrainPulseSink"/>
    /// (publisher) and the subscriber API consumed by WatchBrainActivity.
    /// Must be called once per silo that hosts the kernel gRPC gateway.
    public static ISiloBuilder UseInoBrainStream(this ISiloBuilder silo)
    {
        silo.AddIncomingGrainCallFilter<InoInstanceContextFilter>();
        silo.AddIncomingGrainCallFilter<BrainTraceFilter>();
        silo.Services.AddSingleton<BrainPulseHub>();
        silo.Services.AddSingleton<IBrainPulseSink>(sp => sp.GetRequiredService<BrainPulseHub>());
        return silo;
    }
}
