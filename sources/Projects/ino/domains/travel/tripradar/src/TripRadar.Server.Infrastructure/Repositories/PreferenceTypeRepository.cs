using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Database;
using PreferenceType = TripRadar.Server.Domain.Entities.PreferenceType;

namespace TripRadar.Server.Infrastructure.Repositories;

public class PreferenceTypeRepository(TripRadarDbContext context) : IPreferenceTypeRepository
{
    public async Task<List<PreferenceType>> GetActiveByServiceTypeAsync(ServiceType serviceType, CancellationToken ct = default) =>
        await context.PreferenceTypes
            .AsNoTracking()
            .Where(pt => pt.ServiceTypeId == serviceType.Id && pt.IsActive)
            .Include(i => i.ServiceType)
            .ThenInclude(type => type.PreferenceCategory)
            .ToListAsync(ct);

    public async Task<List<PreferenceType>> GetActiveByServiceTypesAndNameAsync(IReadOnlyCollection<ServiceType> serviceTypes, string preferenceTypeName, CancellationToken ct = default)
    {
        if (serviceTypes.Count == 0)
        {
            return [];
        }

        var serviceTypeIds = serviceTypes.Select(serviceType => serviceType.Id).ToArray();

        return await context.PreferenceTypes
            .AsNoTracking()
            .Where(pt => pt.IsActive
                && pt.Name == preferenceTypeName
                && serviceTypeIds.Contains(pt.ServiceTypeId))
            .Include(i => i.ServiceType)
            .ThenInclude(serviceType => serviceType.PreferenceCategory)
            .ToListAsync(ct);
    }

    public async Task<List<PreferenceType>> GetAllActiveAsync(CancellationToken ct = default) =>
        await context.PreferenceTypes
            .AsNoTracking()
            .Where(pt => pt.IsActive)
            .Include(i => i.ServiceType)
            .ThenInclude(serviceType => serviceType.PreferenceCategory)
            .ToListAsync(ct);
}
