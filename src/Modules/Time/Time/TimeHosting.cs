using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace DigitalBrain.Time;
public static class TimeHosting
{
    public static ISiloBuilder AddTime(this ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton(TimeProvider.System);
        return silo;
    }
}
