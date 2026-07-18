using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Db.Models;
using TripRadar.Server.Db.Seeding;

namespace TripRadar.Server.Db;

public class SetupDbContext(IConfiguration configuration) : DbContext
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public DbSet<ScheduledFlightQueries> ScheduledFlightQueries { get; set; }
    public DbSet<ScheduledHotelQueries> ScheduledHotelQueries { get; set; }
    public DbSet<ScheduledEventQueries> ScheduledEventQueries { get; set; }
    public DbSet<ScheduledLocalPlacesQueries> ScheduledLocalPlacesQueries { get; set; }
    public DbSet<Airports> Airports { get; set; }
    public DbSet<Countries> Countries { get; set; }
    public DbSet<Domains> Domains { get; set; }
    public DbSet<TripAdvisorDomains> TripAdvisorDomains { get; set; }
    public DbSet<OpenTableDomains> OpenTableDomains { get; set; }
    public DbSet<YelpDomains> YelpDomains { get; set; }
    public DbSet<YelpReviewLanguages> YelpReviewLanguages { get; set; }
    public DbSet<GoogleLrLanguages> GoogleLrLanguages { get; set; }
    public DbSet<Airlines> Airlines { get; set; }
    public DbSet<Tiers> Tiers { get; set; }
    public DbSet<BillingPeriods> BillingPeriods { get; set; }
    public DbSet<UserMonthlyTokenCounts> UserMonthlyTokenCounts { get; set; }
    public DbSet<PreferenceCategories> PreferenceCategories { get; set; }
    public DbSet<ServiceTypes> ServiceTypes { get; set; }
    public DbSet<UsageEventSources> UsageEventSources { get; set; }
    public DbSet<UsageEvents> UsageEvents { get; set; }
    public DbSet<ServiceTokenCosts> ServiceTokenCosts { get; set; }
    public DbSet<Users> Users { get; set; }
    public DbSet<UserProfiles> UserProfile { get; set; }
    public DbSet<Languages> Languages { get; set; }
    public DbSet<Timezones> Timezones { get; set; }
    public DbSet<Currencies> Currencies { get; set; }
    public DbSet<Locations> Locations { get; set; }
    public DbSet<Prices> Prices { get; set; }
    public DbSet<Feedbacks> Feedbacks { get; set; } = null!;
    public DbSet<FeedbackCategories> FeedbackCategories { get; set; } = null!;
    public DbSet<UserSubscriptions> UserSubscriptions { get; set; } = null!;
    public DbSet<OveragePricing> OveragePricing { get; set; } = null!;
    public DbSet<UserPreferences> UserPreferences { get; set; } = null!;
    public DbSet<PreferenceTypes> PreferenceTypes { get; set; } = null!;
    public DbSet<OverageBillingRecords> OverageBillingRecords { get; set; } = null!;
    public DbSet<DiscountTypes> DiscountTypes { get; set; } = null!;
    public DbSet<PromoCodes> PromoCodes { get; set; } = null!;
    public DbSet<PromoCodeUsages> PromoCodeUsages { get; set; } = null!;
    public DbSet<TripVaults> TripVaults { get; set; } = null!;
    public DbSet<TripQueryHistories> TripQueryHistories { get; set; } = null!;

    public async Task SeedAsync() => await DbSeeder.SeedAsync(_configuration, this);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DbConstants.SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SetupDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        var connectionString = _configuration.GetConnectionString("db");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string 'db' is missing. Please check your configuration.");
        }

        optionsBuilder.UseNpgsql(connectionString, options =>
            {
                options.MigrationsAssembly("TripRadar.Server.Db");
                options.MigrationsHistoryTable("__EFMigrationsHistory", DbConstants.SchemaName);
            }
        );
    }
}
