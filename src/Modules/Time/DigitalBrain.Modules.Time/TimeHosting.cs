using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace DigitalBrain.Time;

public static class TimeHosting
{
    public static ISiloBuilder AddTime(this ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.AddSingleton<IConfigurationValidator, ReminderConfiguration>();
        return silo;
    }

    private sealed class ReminderConfiguration(IServiceProvider services) : IConfigurationValidator
    {
        public void ValidateConfiguration()
        {
            if (services.GetService<IReminderTable>() is null)
            { throw new InvalidOperationException("Time requires an Orleans reminder provider. Configure one before starting the silo."); }
        }
    }
}
