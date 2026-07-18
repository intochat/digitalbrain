using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Db.Models;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Db.Seeding;

public static partial class DbSeeder
{
    private static void SeedTiers(SetupDbContext ctx)
    {
        var expectedTiers = new List<Tiers>
        {
            new() { TierId = UserTierType.Basic.Id, Name = UserTierType.Basic.Name, TokensPerMonthLimit = 50m },
            new()
            {
                TierId = UserTierType.Essential.Id,
                Name = UserTierType.Essential.Name,
                TokensPerMonthLimit = 500m
            },
            new()
            {
                TierId = UserTierType.Advanced.Id,
                Name = UserTierType.Advanced.Name,
                TokensPerMonthLimit = 3000m
            }
        };

        var existingTiers = ctx.Tiers.ToDictionary(t => t.TierId);
        var tiersToAdd = new List<Tiers>();
        var hasChanges = false;

        foreach (var expectedTier in expectedTiers)
        {
            if (existingTiers.TryGetValue(expectedTier.TierId, out var existingTier))
            {
                if (existingTier.Name != expectedTier.Name ||
                    existingTier.TokensPerMonthLimit != expectedTier.TokensPerMonthLimit)
                {
                    existingTier.Name = expectedTier.Name;
                    existingTier.TokensPerMonthLimit = expectedTier.TokensPerMonthLimit;
                    hasChanges = true;
                }

                continue;
            }

            tiersToAdd.Add(expectedTier);
        }

        if (tiersToAdd.Count != 0)
        {
            ctx.Tiers.AddRange(tiersToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }

    private static void SeedBillingPeriods(SetupDbContext ctx)
    {
        var expectedBillingPeriods = new List<BillingPeriods>
        {
            new() { BillingPeriodId = BillingPeriodType.Monthly.Id, Name = BillingPeriodType.Monthly.Name },
            new() { BillingPeriodId = BillingPeriodType.Yearly.Id, Name = BillingPeriodType.Yearly.Name }
        };

        var existingPeriodIds = ctx.BillingPeriods.AsNoTracking().Select(bp => bp.BillingPeriodId).ToHashSet();
        var periodsToAdd = expectedBillingPeriods.Where(bp => !existingPeriodIds.Contains(bp.BillingPeriodId)).ToList();

        if (periodsToAdd.Any())
        {
            ctx.BillingPeriods.AddRange(periodsToAdd);
            ctx.SaveChanges();
        }
    }

    private static void SeedTimezones(SetupDbContext ctx)
    {
        var expectedTimezones = new List<Timezones>
        {
            new() { TimezoneCode = "UTC", TimezoneName = "UTC" },
            new() { TimezoneCode = "Pacific/Pago_Pago", TimezoneName = "American Samoa" },
            new() { TimezoneCode = "Pacific/Honolulu", TimezoneName = "Honolulu (HST)" },
            new() { TimezoneCode = "Pacific/Tahiti", TimezoneName = "Tahiti (TAHT)" },
            new() { TimezoneCode = "America/Anchorage", TimezoneName = "Anchorage (AKST)" },
            new() { TimezoneCode = "America/New_York", TimezoneName = "Eastern Time (ET)" },
            new() { TimezoneCode = "America/Chicago", TimezoneName = "Central Time (CT)" },
            new() { TimezoneCode = "America/Denver", TimezoneName = "Mountain Time (MT)" },
            new() { TimezoneCode = "America/Los_Angeles", TimezoneName = "Pacific Time (PT)" },
            new() { TimezoneCode = "America/Phoenix", TimezoneName = "Phoenix (MST)" },
            new() { TimezoneCode = "America/Toronto", TimezoneName = "Toronto (ET)" },
            new() { TimezoneCode = "America/Vancouver", TimezoneName = "Vancouver (PT)" },
            new() { TimezoneCode = "America/Winnipeg", TimezoneName = "Winnipeg (CT)" },
            new() { TimezoneCode = "America/Mexico_City", TimezoneName = "Mexico City (CT)" },
            new() { TimezoneCode = "America/Bogota", TimezoneName = "Bogota (COT)" },
            new() { TimezoneCode = "America/Lima", TimezoneName = "Lima (PET)" },
            new() { TimezoneCode = "America/Halifax", TimezoneName = "Halifax (AT)" },
            new() { TimezoneCode = "America/St_Johns", TimezoneName = "St. John's (NST)" },
            new() { TimezoneCode = "America/Santiago", TimezoneName = "Santiago (CLT)" },
            new() { TimezoneCode = "America/Sao_Paulo", TimezoneName = "Sao Paulo (BRT)" },
            new() { TimezoneCode = "America/Argentina/Buenos_Aires", TimezoneName = "Buenos Aires (ART)" },
            new() { TimezoneCode = "America/Caracas", TimezoneName = "Caracas (VET)" },
            new() { TimezoneCode = "Atlantic/South_Georgia", TimezoneName = "South Georgia (GST)" },
            new() { TimezoneCode = "Atlantic/Azores", TimezoneName = "Azores (AZOT)" },
            new() { TimezoneCode = "Atlantic/Reykjavik", TimezoneName = "Reykjavik (GMT)" },
            new() { TimezoneCode = "Europe/London", TimezoneName = "London (GMT)" },
            new() { TimezoneCode = "Europe/Dublin", TimezoneName = "Dublin (GMT)" },
            new() { TimezoneCode = "Europe/Lisbon", TimezoneName = "Lisbon (WET)" },
            new() { TimezoneCode = "Europe/Paris", TimezoneName = "Paris (CET)" },
            new() { TimezoneCode = "Europe/Berlin", TimezoneName = "Berlin (CET)" },
            new() { TimezoneCode = "Europe/Rome", TimezoneName = "Rome (CET)" },
            new() { TimezoneCode = "Europe/Madrid", TimezoneName = "Madrid (CET)" },
            new() { TimezoneCode = "Europe/Amsterdam", TimezoneName = "Amsterdam (CET)" },
            new() { TimezoneCode = "Europe/Brussels", TimezoneName = "Brussels (CET)" },
            new() { TimezoneCode = "Europe/Vienna", TimezoneName = "Vienna (CET)" },
            new() { TimezoneCode = "Europe/Prague", TimezoneName = "Prague (CET)" },
            new() { TimezoneCode = "Europe/Warsaw", TimezoneName = "Warsaw (CET)" },
            new() { TimezoneCode = "Europe/Zurich", TimezoneName = "Zurich (CET)" },
            new() { TimezoneCode = "Europe/Stockholm", TimezoneName = "Stockholm (CET)" },
            new() { TimezoneCode = "Europe/Oslo", TimezoneName = "Oslo (CET)" },
            new() { TimezoneCode = "Europe/Copenhagen", TimezoneName = "Copenhagen (CET)" },
            new() { TimezoneCode = "Europe/Helsinki", TimezoneName = "Helsinki (EET)" },
            new() { TimezoneCode = "Europe/Athens", TimezoneName = "Athens (EET)" },
            new() { TimezoneCode = "Europe/Bucharest", TimezoneName = "Bucharest (EET)" },
            new() { TimezoneCode = "Europe/Sofia", TimezoneName = "Sofia (EET)" },
            new() { TimezoneCode = "Europe/Istanbul", TimezoneName = "Istanbul (TRT)" },
            new() { TimezoneCode = "Europe/Kiev", TimezoneName = "Kyiv (EET)" },
            new() { TimezoneCode = "Africa/Cairo", TimezoneName = "Cairo (EET)" },
            new() { TimezoneCode = "Africa/Johannesburg", TimezoneName = "Johannesburg (SAST)" },
            new() { TimezoneCode = "Africa/Nairobi", TimezoneName = "Nairobi (EAT)" },
            new() { TimezoneCode = "Asia/Jerusalem", TimezoneName = "Jerusalem (IST)" },
            new() { TimezoneCode = "Asia/Riyadh", TimezoneName = "Riyadh (AST)" },
            new() { TimezoneCode = "Asia/Tokyo", TimezoneName = "Tokyo (JST)" },
            new() { TimezoneCode = "Asia/Seoul", TimezoneName = "Seoul (KST)" },
            new() { TimezoneCode = "Asia/Shanghai", TimezoneName = "Shanghai (CST)" },
            new() { TimezoneCode = "Asia/Singapore", TimezoneName = "Singapore (SGT)" },
            new() { TimezoneCode = "Asia/Kuala_Lumpur", TimezoneName = "Kuala Lumpur (MYT)" },
            new() { TimezoneCode = "Asia/Dubai", TimezoneName = "Dubai (GST)" },
            new() { TimezoneCode = "Asia/Baku", TimezoneName = "Baku (AZT)" },
            new() { TimezoneCode = "Asia/Tbilisi", TimezoneName = "Tbilisi (GET)" },
            new() { TimezoneCode = "Asia/Tehran", TimezoneName = "Tehran (IRST)" },
            new() { TimezoneCode = "Asia/Karachi", TimezoneName = "Karachi (PKT)" },
            new() { TimezoneCode = "Asia/Tashkent", TimezoneName = "Tashkent (UZT)" },
            new() { TimezoneCode = "Asia/Almaty", TimezoneName = "Almaty (ALMT)" },
            new() { TimezoneCode = "Asia/Kolkata", TimezoneName = "Kolkata (IST)" },
            new() { TimezoneCode = "Asia/Colombo", TimezoneName = "Colombo (IST)" },
            new() { TimezoneCode = "Asia/Kathmandu", TimezoneName = "Kathmandu (NPT)" },
            new() { TimezoneCode = "Asia/Dhaka", TimezoneName = "Dhaka (BST)" },
            new() { TimezoneCode = "Asia/Yangon", TimezoneName = "Yangon (MMT)" },
            new() { TimezoneCode = "Asia/Bangkok", TimezoneName = "Bangkok (ICT)" },
            new() { TimezoneCode = "Asia/Jakarta", TimezoneName = "Jakarta (WIB)" },
            new() { TimezoneCode = "Asia/Ho_Chi_Minh", TimezoneName = "Ho Chi Minh City (ICT)" },
            new() { TimezoneCode = "Asia/Hong_Kong", TimezoneName = "Hong Kong (HKT)" },
            new() { TimezoneCode = "Asia/Taipei", TimezoneName = "Taipei (CST)" },
            new() { TimezoneCode = "Asia/Manila", TimezoneName = "Manila (PHT)" },
            new() { TimezoneCode = "Australia/Perth", TimezoneName = "Perth (AWST)" },
            new() { TimezoneCode = "Australia/Brisbane", TimezoneName = "Brisbane (AEST)" },
            new() { TimezoneCode = "Australia/Sydney", TimezoneName = "Sydney (AEST)" },
            new() { TimezoneCode = "Australia/Melbourne", TimezoneName = "Melbourne (AEST)" },
            new() { TimezoneCode = "Australia/Adelaide", TimezoneName = "Adelaide (ACST)" },
            new() { TimezoneCode = "Australia/Darwin", TimezoneName = "Darwin (ACST)" },
            new() { TimezoneCode = "Pacific/Guam", TimezoneName = "Guam (ChST)" },
            new() { TimezoneCode = "Pacific/Noumea", TimezoneName = "Noumea (NCT)" },
            new() { TimezoneCode = "Pacific/Fiji", TimezoneName = "Fiji (FJT)" },
            new() { TimezoneCode = "Pacific/Auckland", TimezoneName = "Auckland (NZST)" },
            new() { TimezoneCode = "Pacific/Chatham", TimezoneName = "Chatham (CHAST)" },
            new() { TimezoneCode = "Pacific/Apia", TimezoneName = "Apia (WSST)" },
            new() { TimezoneCode = "Pacific/Kiritimati", TimezoneName = "Kiritimati (LINT)" }
        };

        var existingTimezones = ctx.Timezones.ToDictionary(t => t.TimezoneCode, StringComparer.Ordinal);
        var timezonesToAdd = new List<Timezones>();
        var hasChanges = false;

        foreach (var expectedTimezone in expectedTimezones)
        {
            if (existingTimezones.TryGetValue(expectedTimezone.TimezoneCode, out var existingTimezone))
            {
                if (!string.Equals(existingTimezone.TimezoneName, expectedTimezone.TimezoneName, StringComparison.Ordinal))
                {
                    existingTimezone.TimezoneName = expectedTimezone.TimezoneName;
                    hasChanges = true;
                }

                continue;
            }

            timezonesToAdd.Add(expectedTimezone);
        }

        if (timezonesToAdd.Count != 0)
        {
            ctx.Timezones.AddRange(timezonesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }

    private static void SeedPrices(IConfiguration configuration, SetupDbContext ctx)
    {
        var currencyUsdId = GetCurrencyIdByCode(ctx, DbConstants.SeedDefaults.DefaultCurrencyCode);

        var expectedPrices = new List<(int TierId, int BillingPeriodId, long Amount, string ConfigKey)>
        {
            (UserTierType.Basic.Id, BillingPeriodType.Monthly.Id, 0, "PaymentSettings:Stripe:Prices:BasicTierPriceId"),
            (UserTierType.Essential.Id, BillingPeriodType.Monthly.Id, 900, "PaymentSettings:Stripe:Prices:EssentialTierPriceId"),
            (UserTierType.Advanced.Id, BillingPeriodType.Monthly.Id, 2300, "PaymentSettings:Stripe:Prices:AdvancedTierPriceId"),
            (UserTierType.Basic.Id, BillingPeriodType.Yearly.Id, 0, "PaymentSettings:Stripe:Prices:BasicTierYearlyPriceId"),
            (UserTierType.Essential.Id, BillingPeriodType.Yearly.Id, 9000, "PaymentSettings:Stripe:Prices:EssentialTierYearlyPriceId"),
            (UserTierType.Advanced.Id, BillingPeriodType.Yearly.Id, 23000, "PaymentSettings:Stripe:Prices:AdvancedTierYearlyPriceId")
        };

        var existingPrices = ctx.Prices.ToDictionary(p => (p.TierId, p.BillingPeriodId));

        var pricesToAdd = new List<Prices>();
        var hasChanges = false;

        foreach (var (tierId, billingPeriodId, amount, configKey) in expectedPrices)
        {
            var stripeId = GetEncryptedStripeId(configuration, configKey, allowNull: true);
            var stripeIdHash = GetStripeIdHash(configuration, configKey, allowNull: true);

            if (existingPrices.TryGetValue((tierId, billingPeriodId), out var existingPrice))
            {
                var rowChanged = false;

                if (existingPrice.Amount != amount)
                {
                    existingPrice.Amount = amount;
                    rowChanged = true;
                }

                if (existingPrice.CurrencyId != currencyUsdId)
                {
                    existingPrice.CurrencyId = currencyUsdId;
                    rowChanged = true;
                }

                if (existingPrice.StripeId != stripeId)
                {
                    existingPrice.StripeId = stripeId;
                    rowChanged = true;
                }

                if (existingPrice.StripeIdHash != stripeIdHash)
                {
                    existingPrice.StripeIdHash = stripeIdHash;
                    rowChanged = true;
                }

                if (rowChanged)
                {
                    existingPrice.UpdatedAt = DateTime.UtcNow;
                    hasChanges = true;
                }
            }
            else
            {
                pricesToAdd.Add(new Prices
                {
                    TierId = tierId,
                    Amount = amount,
                    BillingPeriodId = billingPeriodId,
                    CurrencyId = currencyUsdId,
                    CreatedAt = DateTime.UtcNow,
                    StripeId = stripeId,
                    StripeIdHash = stripeIdHash
                });
            }
        }

        if (pricesToAdd.Any())
        {
            ctx.Prices.AddRange(pricesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }

    private static void SeedFeedbackCategories(SetupDbContext ctx)
    {
        var expectedCategories = new List<FeedbackCategories>
        {
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.General.Id, Name = FeedbackCategoryType.General.Name
            },
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.BugReport.Id,
                Name = FeedbackCategoryType.BugReport.Name
            },
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.FeatureRequest.Id,
                Name = FeedbackCategoryType.FeatureRequest.Name
            },
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.Performance.Id,
                Name = FeedbackCategoryType.Performance.Name
            },
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.UserInterface.Id,
                Name = FeedbackCategoryType.UserInterface.Name
            },
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.Documentation.Id,
                Name = FeedbackCategoryType.Documentation.Name
            },
            new()
            {
                FeedbackCategoryId = FeedbackCategoryType.SubscriptionCancellation.Id,
                Name = FeedbackCategoryType.SubscriptionCancellation.Name
            }
        };

        var existingCategoryIds = ctx.FeedbackCategories.AsNoTracking().Select(fc => fc.FeedbackCategoryId).ToHashSet();
        var categoriesToAdd = expectedCategories.Where(fc => !existingCategoryIds.Contains(fc.FeedbackCategoryId)).ToList();

        if (categoriesToAdd.Count != 0)
        {
            ctx.FeedbackCategories.AddRange(categoriesToAdd);
            ctx.SaveChanges();
        }
    }

    private static void SeedPreferenceCategories(SetupDbContext ctx)
    {
        var expectedCategories = PreferenceCategoryType.GetAllCategories()
            .Select(category => new PreferenceCategories
            {
                PreferenceCategoryId = category.Id,
                Name = category.Name,
                IsActive = true
            })
            .ToList();

        var existingCategories = ctx.PreferenceCategories.ToDictionary(category => category.PreferenceCategoryId);
        var categoriesToAdd = new List<PreferenceCategories>();
        var hasChanges = false;

        foreach (var expectedCategory in expectedCategories)
        {
            if (existingCategories.TryGetValue(expectedCategory.PreferenceCategoryId, out var existingCategory))
            {
                var changed = false;

                if (!string.Equals(existingCategory.Name, expectedCategory.Name, StringComparison.Ordinal))
                {
                    existingCategory.Name = expectedCategory.Name;
                    changed = true;
                }

                if (existingCategory.IsActive != expectedCategory.IsActive)
                {
                    existingCategory.IsActive = expectedCategory.IsActive;
                    changed = true;
                }

                if (changed)
                {
                    hasChanges = true;
                }

                continue;
            }

            categoriesToAdd.Add(expectedCategory);
        }

        if (categoriesToAdd.Count != 0)
        {
            ctx.PreferenceCategories.AddRange(categoriesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }

    private static void SeedServiceTypes(SetupDbContext ctx)
    {
        var expectedServiceTypes = ServiceType.GetAllServices()
            .Select(serviceType => new ServiceTypes
            {
                ServiceTypeId = serviceType.Id,
                Name = serviceType.Name,
                PreferenceCategoryId = PreferenceCategoryType.GetByServiceType(serviceType).Id
            })
            .ToList();

        var existingServiceTypes = ctx.ServiceTypes.ToDictionary(serviceType => serviceType.ServiceTypeId);
        var serviceTypesToAdd = new List<ServiceTypes>();
        var hasChanges = false;

        foreach (var expectedServiceType in expectedServiceTypes)
        {
            if (existingServiceTypes.TryGetValue(expectedServiceType.ServiceTypeId, out var existingServiceType))
            {
                var changed = false;

                if (!string.Equals(existingServiceType.Name, expectedServiceType.Name, StringComparison.Ordinal))
                {
                    existingServiceType.Name = expectedServiceType.Name;
                    changed = true;
                }

                if (existingServiceType.PreferenceCategoryId != expectedServiceType.PreferenceCategoryId)
                {
                    existingServiceType.PreferenceCategoryId = expectedServiceType.PreferenceCategoryId;
                    changed = true;
                }

                if (changed)
                {
                    hasChanges = true;
                }

                continue;
            }

            serviceTypesToAdd.Add(expectedServiceType);
        }

        if (serviceTypesToAdd.Count != 0)
        {
            ctx.ServiceTypes.AddRange(serviceTypesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }

    private static void SeedUsageEventSources(SetupDbContext ctx)
    {
        var expectedSources = new List<UsageEventSources>
        {
            new()
            {
                UsageEventSourceId = UsageEventSourceType.Api.Id,
                Name = UsageEventSourceType.Api.Name,
                Description = UsageEventSourceType.Api.Description,
                IsActive = true
            },
            new()
            {
                UsageEventSourceId = UsageEventSourceType.Scheduled.Id,
                Name = UsageEventSourceType.Scheduled.Name,
                Description = UsageEventSourceType.Scheduled.Description,
                IsActive = true
            },
            new()
            {
                UsageEventSourceId = UsageEventSourceType.Telegram.Id,
                Name = UsageEventSourceType.Telegram.Name,
                Description = UsageEventSourceType.Telegram.Description,
                IsActive = true
            },
            new()
            {
                UsageEventSourceId = UsageEventSourceType.Ai.Id,
                Name = UsageEventSourceType.Ai.Name,
                Description = UsageEventSourceType.Ai.Description,
                IsActive = true
            }
        };

        var existingSources = ctx.UsageEventSources.ToDictionary(source => source.UsageEventSourceId);
        var sourcesToAdd = new List<UsageEventSources>();
        var hasChanges = false;

        foreach (var expectedSource in expectedSources)
        {
            if (existingSources.TryGetValue(expectedSource.UsageEventSourceId, out var existingSource))
            {
                var sourceChanged = false;

                if (!string.Equals(existingSource.Name, expectedSource.Name, StringComparison.Ordinal))
                {
                    existingSource.Name = expectedSource.Name;
                    sourceChanged = true;
                }

                if (!string.Equals(existingSource.Description, expectedSource.Description, StringComparison.Ordinal))
                {
                    existingSource.Description = expectedSource.Description;
                    sourceChanged = true;
                }

                if (existingSource.IsActive != expectedSource.IsActive)
                {
                    existingSource.IsActive = expectedSource.IsActive;
                    sourceChanged = true;
                }

                if (sourceChanged)
                {
                    hasChanges = true;
                }

                continue;
            }

            sourcesToAdd.Add(expectedSource);
        }

        if (sourcesToAdd.Count != 0)
        {
            ctx.UsageEventSources.AddRange(sourcesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            ctx.SaveChanges();
        }
    }

    private static void SeedServiceTokenCosts(SetupDbContext ctx)
    {
        var expectedCosts = new List<ServiceTokenCosts>
        {
            new() { ServiceTypeId = ServiceType.Event.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.Flight.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.Hotel.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.LocalPlaces.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.Maps.Id, Cost = 2m },
            new() { ServiceTypeId = ServiceType.PlaceReview.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.FlightExplore.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.TripAdvisorSearch.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.TripAdvisorPlace.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.OpenTableReview.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.YouTubeSearch.Id, Cost = 2m },
            new() { ServiceTypeId = ServiceType.YelpSearch.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.YelpPlace.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.YelpReviews.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.YelpPlaceFullMenu.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.MapsDirections.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.MapsPlaceResults.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.GoogleLightSearch.Id, Cost = 1m },
            new() { ServiceTypeId = ServiceType.FlightPriceCalendar.Id, Cost = 1m }
        };

        var existingServiceTypeIds = ctx.ServiceTokenCosts.AsNoTracking().Select(stc => stc.ServiceTypeId).ToHashSet();
        var costsToAdd = expectedCosts.Where(stc => !existingServiceTypeIds.Contains(stc.ServiceTypeId)).ToList();

        if (costsToAdd.Count != 0)
        {
            ctx.ServiceTokenCosts.AddRange(costsToAdd);
            ctx.SaveChanges();
        }
    }

    private static void SeedOveragePricing(SetupDbContext ctx)
    {
        var currencyUsdId = GetCurrencyIdByCode(ctx, DbConstants.SeedDefaults.DefaultCurrencyCode);

        var expectedPricing = new List<OveragePricing>
        {
            new() { TierId = UserTierType.Essential.Id, PricePerToken = 0.001m, CurrencyId = currencyUsdId },
            new() { TierId = UserTierType.Advanced.Id, PricePerToken = 0.0008m, CurrencyId = currencyUsdId }
        };

        var existingTierIds = ctx.OveragePricing.AsNoTracking().Select(op => op.TierId).ToHashSet();
        var pricingToAdd = expectedPricing.Where(op => !existingTierIds.Contains(op.TierId)).ToList();

        if (pricingToAdd.Any())
        {
            ctx.OveragePricing.AddRange(pricingToAdd);
            ctx.SaveChanges();
        }
    }

    private static void SeedDiscountTypes(SetupDbContext ctx)
    {
        var expectedDiscountTypes = new List<DiscountTypes>
        {
            new()
            {
                DiscountTypeId = DiscountType.Percentage.Id,
                Name = DiscountType.Percentage.Name,
                Description = "Percentage discount (0-100%)",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new()
            {
                DiscountTypeId = DiscountType.FixedAmount.Id,
                Name = DiscountType.FixedAmount.Name,
                Description = "Fixed amount discount in USD",
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        var existingDiscountTypeIds = ctx.DiscountTypes.AsNoTracking().Select(dt => dt.DiscountTypeId).ToHashSet();
        var discountTypesToAdd = expectedDiscountTypes.Where(dt => !existingDiscountTypeIds.Contains(dt.DiscountTypeId)).ToList();

        if (discountTypesToAdd.Any())
        {
            ctx.DiscountTypes.AddRange(discountTypesToAdd);
            ctx.SaveChanges();
        }
    }

    private static void SeedPromoCodes(SetupDbContext ctx)
    {
        var expectedPromoCodes = new List<PromoCodes>
        {
            new()
            {
                Code = "SAVE20",
                Description = "20% off your first booking",
                DiscountTypeId = DiscountType.Percentage.Id,
                DiscountValue = 20m,
                MaxUsageCount = 500,
                CurrentUsageCount = 0,
                MaxUsagePerUser = 1,
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(9999, 6, 30, 23, 59, 59, DateTimeKind.Utc),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new()
            {
                Code = "EARLYBIRD",
                Description = "Early bird $50 discount",
                DiscountTypeId = DiscountType.FixedAmount.Id,
                DiscountValue = 50m,
                MaxUsageCount = 100,
                CurrentUsageCount = 0,
                MaxUsagePerUser = 1,
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(9999, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new()
            {
                Code = "VIPACCESS",
                Description = "VIP unlimited access 15% off",
                DiscountTypeId = DiscountType.Percentage.Id,
                DiscountValue = 15m,
                MaxUsageCount = null,
                CurrentUsageCount = 0,
                MaxUsagePerUser = 1,
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new()
            {
                Code = "FIRST10",
                Description = "First time user $10 off",
                DiscountTypeId = DiscountType.FixedAmount.Id,
                DiscountValue = 10m,
                MaxUsageCount = 5000,
                CurrentUsageCount = 0,
                MaxUsagePerUser = 1,
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        var existingPromoCodes = ctx.PromoCodes.AsNoTracking().Select(pc => pc.Code).ToHashSet();
        var promoCodesToAdd = expectedPromoCodes.Where(pc => !existingPromoCodes.Contains(pc.Code)).ToList();

        if (promoCodesToAdd.Any())
        {
            ctx.PromoCodes.AddRange(promoCodesToAdd);
            ctx.SaveChanges();
        }
    }
}

