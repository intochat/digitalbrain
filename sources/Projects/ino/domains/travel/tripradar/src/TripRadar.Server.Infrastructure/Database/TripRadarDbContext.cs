using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Events;
using TripRadar.Server.Domain.ReferenceData;
using FeedbackCategory = TripRadar.Server.Domain.ReferenceData.FeedbackCategory;
using PreferenceCategory = TripRadar.Server.Domain.ReferenceData.PreferenceCategory;
using ServiceType = TripRadar.Server.Domain.ReferenceData.ServiceType;
using TripQueryHistory = TripRadar.Server.Domain.Entities.TripQueryHistory;

namespace TripRadar.Server.Infrastructure.Database;

public class TripRadarDbContext(DbContextOptions<TripRadarDbContext> options) : DbContext(options)
{
    public DbSet<ScheduledFlightQuery> ScheduledFlightQueries { get; set; } = null!;

    public DbSet<ScheduledHotelQuery> ScheduledHotelQueries { get; set; } = null!;

    public DbSet<ScheduledLocalPlaceQuery> ScheduledLocalPlacesQueries { get; set; } = null!;

    public DbSet<ScheduledEventQuery> ScheduledEventQueries { get; set; } = null!;

    public DbSet<Airport> Airports { get; set; }

    public DbSet<Country> Countries { get; set; }

    public DbSet<GoogleDomain> Domains { get; set; }

    public DbSet<TripAdvisorDomain> TripAdvisorDomains { get; set; }

    public DbSet<OpenTableDomain> OpenTableDomains { get; set; }

    public DbSet<YelpDomain> YelpDomains { get; set; }

    public DbSet<YelpReviewLanguage> YelpReviewLanguages { get; set; }

    public DbSet<GoogleLrLanguage> GoogleLrLanguages { get; set; }

    public DbSet<Airline> Airlines { get; set; }

    public DbSet<User> Users { get; set; }

    public DbSet<UserProfile> UserProfile { get; set; }

    public DbSet<Tier> Tiers { get; set; }

    public DbSet<BillingPeriod> BillingPeriods { get; set; }

    public DbSet<UserMonthlyTokenCount> UserMonthlyTokenCounts { get; set; }

    public DbSet<ServiceTokenCost> ServiceTokenCosts { get; set; }

    public DbSet<PreferenceCategory> PreferenceCategories { get; set; }

    public DbSet<ServiceType> ServiceTypes { get; set; }

    public DbSet<UsageEventSource> UsageEventSources { get; set; } = null!;

    public DbSet<UsageEvent> UsageEvents { get; set; } = null!;

    public DbSet<ScheduledExecution> ScheduledExecutions { get; set; }

    public DbSet<Language> Languages { get; set; }

    public DbSet<Timezone> Timezones { get; set; }

    public DbSet<Currency> Currencies { get; set; }

    public DbSet<Location> Locations { get; set; }

    public DbSet<Price> Prices { get; set; } = null!;

    public DbSet<Feedback> Feedbacks { get; set; } = null!;

    public DbSet<FeedbackCategory> FeedbackCategories { get; set; } = null!;

    public DbSet<UserSubscription> UserSubscriptions { get; set; } = null!;

    public DbSet<OverageBillingRecord> OverageBillingRecords { get; set; } = null!;

    public DbSet<OveragePricing> OveragePricing { get; set; } = null!;

    public DbSet<PreferenceType> PreferenceTypes { get; set; } = null!;

    public DbSet<UserPreference> UserPreferences { get; set; } = null!;

    public DbSet<DiscountType> DiscountTypes { get; set; } = null!;

    public DbSet<PromoCode> PromoCodes { get; set; } = null!;

    public DbSet<PromoCodeUsage> PromoCodeUsages { get; set; } = null!;

    public DbSet<TripVault> TripVaults { get; set; } = null!;

    public DbSet<TripQueryHistory> TripQueryHistories { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DbConstants.SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripRadarDbContext).Assembly);
        IgnoreDomainEvents(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    private static void IgnoreDomainEvents(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(entityType => typeof(IHasDomainEvents).IsAssignableFrom(entityType.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Ignore(nameof(IHasDomainEvents.DomainEvents));
        }
    }
}
