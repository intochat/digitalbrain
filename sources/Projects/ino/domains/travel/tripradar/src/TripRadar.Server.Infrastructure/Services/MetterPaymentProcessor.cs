using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Database;
using TripRadar.Server.Infrastructure.Providers.Stripe.Client;
using System.Globalization;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Services;

public class MetterPaymentProcessor(
    TripRadarDbContext dbContext,
    IStripeApiProvider stripeApiProvider,
    ILogger<MetterPaymentProcessor> logger) : IMetterPaymentProcessor
{
    public async Task<int> ProcessMonthlyOverageChargesAsync(CancellationToken cancellationToken = default)
    {
        var previousBillingPeriod = DateTime.UtcNow.AddMonths(-1);
        var targetYear = previousBillingPeriod.Year;
        var targetMonth = previousBillingPeriod.Month;

        var pendingGroups = await dbContext.OverageBillingRecords
            .AsNoTracking()
            .Where(r => !r.IsBilled && r.Year == targetYear && r.Month == targetMonth)
            .GroupBy(r => new { r.UserId, r.CurrencyId, r.Year, r.Month })
            .Select(g => new
            {
                g.Key.UserId,
                g.Key.CurrencyId,
                g.Key.Year,
                g.Key.Month
            })
            .ToListAsync(cancellationToken);

        if (pendingGroups.Count == 0)
        {
            return 0;
        }

        var userIds = pendingGroups.Select(x => x.UserId).Distinct().ToList();
        var subscriptions = await dbContext.UserSubscriptions
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId)
                        && s.IsActive
                        && s.PayAsYouGoEnabled
                        && s.StripeCustomerId != null)
            .Select(s => new { s.UserId, s.StripeCustomerId, s.StripeSubscriptionId })
            .ToListAsync(cancellationToken);
        var subscriptionMap = subscriptions
            .Where(x => !string.IsNullOrWhiteSpace(x.StripeCustomerId))
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.First());

        var billedUsers = 0;

        foreach (var group in pendingGroups)
        {
            if (!subscriptionMap.TryGetValue(group.UserId, out var subscription))
            {
                logger.LogWarning(
                    "Skipping overage billing for user {UserId}: no active PAYG Stripe customer mapping",
                    group.UserId);
                continue;
            }

            var processingId = Guid.NewGuid().ToString("N");
            var lockedRows = await dbContext.OverageBillingRecords
                .Where(r => !r.IsBilled
                            && r.UserId == group.UserId
                            && r.Year == group.Year
                            && r.Month == group.Month
                            && r.CurrencyId == group.CurrencyId
                            && r.ProcessingId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.ProcessingId, processingId)
                    .SetProperty(r => r.ProcessingStartedAt, DateTime.UtcNow), cancellationToken);

            if (lockedRows == 0)
            {
                continue;
            }

            try
            {
                var lockedBatch = await dbContext.OverageBillingRecords
                    .AsNoTracking()
                    .Where(r => r.ProcessingId == processingId && !r.IsBilled)
                    .Select(r => new
                    {
                        r.TotalCharge,
                        CurrencyCode = r.Currency.CurrencyCode
                    })
                    .ToListAsync(cancellationToken);

                if (lockedBatch.Count == 0)
                {
                    continue;
                }

                var totalCharge = lockedBatch.Sum(x => x.TotalCharge);
                var amountInCents = (int)Math.Round(totalCharge * 100m, 0, MidpointRounding.AwayFromZero);
                if (amountInCents <= 0)
                {
                    await dbContext.OverageBillingRecords
                        .Where(r => r.ProcessingId == processingId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(r => r.ProcessingId, (string?)null)
                            .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null), cancellationToken);
                    continue;
                }

                var currencyCode = lockedBatch
                    .Select(x => x.CurrencyCode)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
                    ?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(currencyCode))
                {
                    logger.LogError(
                        "Skipping overage billing for user {UserId}: failed to resolve currency code for processing {ProcessingId}",
                        group.UserId, processingId);
                    await dbContext.OverageBillingRecords
                        .Where(r => r.ProcessingId == processingId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(r => r.ProcessingId, (string?)null)
                            .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null), cancellationToken);
                    continue;
                }

                var metadata = new Dictionary<string, string>
                {
                    [StripeConstants.Metadata.UserId] = group.UserId.ToString(CultureInfo.InvariantCulture),
                    [StripeConstants.Metadata.Year] = group.Year.ToString(CultureInfo.InvariantCulture),
                    [StripeConstants.Metadata.Month] = group.Month.ToString(CultureInfo.InvariantCulture),
                    [StripeConstants.Metadata.Source] = "overage_monthly_billing",
                    [StripeConstants.Metadata.ProcessingId] = processingId
                };

                var description =
                    $"TripRadar overage charges for {group.Year}-{group.Month.ToString("D2", CultureInfo.InvariantCulture)}";
                var idempotencySuffix =
                    $"{group.UserId}:{group.Year}:{group.Month}:{group.CurrencyId}";

                await stripeApiProvider.CreateInvoiceItemAsync(
                    subscription.StripeCustomerId!,
                    amountInCents,
                    currencyCode,
                    description,
                    metadata,
                    subscription.StripeSubscriptionId,
                    $"overage-item:{idempotencySuffix}",
                    cancellationToken);

                var invoiceId = await stripeApiProvider.CreateAndPayInvoiceAsync(
                    subscription.StripeCustomerId!,
                    metadata,
                    $"overage-invoice:{idempotencySuffix}",
                    cancellationToken);

                await dbContext.OverageBillingRecords
                    .Where(r => r.ProcessingId == processingId && !r.IsBilled)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(r => r.IsBilled, true)
                        .SetProperty(r => r.BilledAt, DateTime.UtcNow)
                        .SetProperty(r => r.StripeInvoiceId, invoiceId)
                        .SetProperty(r => r.ProcessingId, (string?)null)
                        .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null), cancellationToken);

                billedUsers++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to process monthly overage billing for user {UserId} ({Year}-{Month}) with processing {ProcessingId}",
                    group.UserId, group.Year, group.Month, processingId);

                await dbContext.OverageBillingRecords
                    .Where(r => r.ProcessingId == processingId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(r => r.ProcessingId, (string?)null)
                        .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null), cancellationToken);
            }
        }

        return billedUsers;
    }

    public async Task<int> ClearStaleProcessingAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        // Enforce a minimum safety margin to prevent clearing locks for running processes
        if (maxAge < TimeSpan.FromMinutes(5))
        {
            logger.LogWarning("ClearStaleProcessingAsync called with unsafe maxAge {MaxAge}. Enforcing minimum 5 minutes.", maxAge);
            maxAge = TimeSpan.FromMinutes(5);
        }

        var cutoff = DateTime.UtcNow.Subtract(maxAge);

        var affectedRows = await dbContext.OverageBillingRecords
            .Where(r => r.ProcessingId != null && r.ProcessingStartedAt < cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.ProcessingId, (string?)null)
                .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null), cancellationToken);

        if (affectedRows > 0)
        {
            logger.LogWarning("Cleared {Count} stale overage billing processing locks older than {Cutoff}", affectedRows, cutoff);
        }

        return affectedRows;
    }
}
