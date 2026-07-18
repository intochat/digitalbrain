using Hangfire;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Jobs.API.Filters;
using TripRadar.ServiceDefaults;

namespace TripRadar.Server.Jobs.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static void ConfigureJobsApp(this WebApplication app)
    {
        app.MapDefaultEndpoints();
        app.MapHealthChecks("/health");
        app.UseHttpsRedirection();

        app.UseHangfireDashboard("/hangfire",
            new DashboardOptions
            {
                Authorization = [new HangfireDashboardAuthorizationFilter(app.Configuration)],
                IsReadOnlyFunc =
                    _ => !app.Environment.IsDevelopment() &&
                         !app.Configuration.GetValue<bool>("Hangfire:IsFullAccessModeEnabled"),
                DashboardTitle = "TripRadar Jobs Dashboard"
            });

        RegisterRecurringJobs(app);
    }

    private static void RegisterRecurringJobs(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RecurringJobs");

        try
        {
            RecurringJob.AddOrUpdate<IResetTokensJob>(
                "allocate-monthly-tokens",
                job => job.ExecuteAsync(CancellationToken.None),
                Cron.Daily,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

            RecurringJob.AddOrUpdate<IMetterBillingJobs>(
                "clear-stale-overage-processing",
                job => job.ClearStaleProcessingAsync(CancellationToken.None),
                Cron.Hourly,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

            RecurringJob.AddOrUpdate<IMetterBillingJobs>(
                "process-monthly-overage-billing",
                job => job.ProcessMonthlyOverageChargesAsync(CancellationToken.None),
                Cron.Daily,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

            logger.LogInformation("Recurring jobs registered successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register recurring jobs at startup. Jobs will be retried later.");
        }
    }
}
