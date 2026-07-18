namespace TripRadar.Server.Infrastructure.Contracts;

public interface IPreferenceMappingService
{
    void ApplyPreferences<TRequest>(TRequest request, Dictionary<string, object> preferences) where TRequest : class;
}
