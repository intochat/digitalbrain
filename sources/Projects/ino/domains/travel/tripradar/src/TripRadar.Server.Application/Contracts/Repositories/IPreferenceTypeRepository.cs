using TripRadar.Server.Domain.Enums;
using PreferenceType = TripRadar.Server.Domain.Entities.PreferenceType;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IPreferenceTypeRepository
{
    Task<List<PreferenceType>> GetActiveByServiceTypeAsync(ServiceType serviceType, CancellationToken ct = default);
    Task<List<PreferenceType>> GetActiveByServiceTypesAndNameAsync(IReadOnlyCollection<ServiceType> serviceTypes, string preferenceTypeName, CancellationToken ct = default);
    Task<List<PreferenceType>> GetAllActiveAsync(CancellationToken ct = default);
}
