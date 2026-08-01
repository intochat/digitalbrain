using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TasksHarnessModule
{
    // Shared across all silos so issue/consume/peek agree in the multi-silo test host.
    internal static readonly MemoryUserActionCustody SharedCustody = new(TimeProvider.System);

    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        builder.Services.RemoveAll<IUserActionCustody>();
        builder.Services.AddSingleton<IUserActionCustody>(SharedCustody);
    }
}
