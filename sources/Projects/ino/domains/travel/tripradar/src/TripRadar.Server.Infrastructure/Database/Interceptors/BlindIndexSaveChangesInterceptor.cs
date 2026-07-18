using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Database.Interceptors;

/// <summary>
/// EF Core interceptor that handles blind index updates.
/// This decouples the business logic from the DbContext, following separation of concerns.
/// </summary>
public class BlindIndexSaveChangesInterceptor(IBlindIndexService blindIndexService) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            UpdateUserProfileBlindIndexes(eventData.Context);
            UpdatePriceBlindIndexes(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            UpdateUserProfileBlindIndexes(eventData.Context);
            UpdatePriceBlindIndexes(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateUserProfileBlindIndexes(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<UserProfile>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added || entry.Property(p => p.Username).IsModified)
            {
                var usernameHash = blindIndexService.ComputeHash(entry.Entity.Username);
                entry.Entity.UpdateUsernameHash(usernameHash);
            }

            if (entry.State != EntityState.Added && !entry.Property(p => p.Email).IsModified)
            {
                continue;
            }

            var emailHash = blindIndexService.ComputeHash(entry.Entity.Email);
            if (!string.IsNullOrEmpty(emailHash))
            {
                entry.Entity.UpdateEmailHash(emailHash);
            }
        }
    }

    private void UpdatePriceBlindIndexes(DbContext context)
    {
        var entries = context.ChangeTracker.Entries<Price>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added || entry.Property(price => price.StripeId).IsModified)
            {
                entry.Entity.UpdateStripeIdHash(blindIndexService.ComputeHash(entry.Entity.StripeId));
            }
        }
    }
}
