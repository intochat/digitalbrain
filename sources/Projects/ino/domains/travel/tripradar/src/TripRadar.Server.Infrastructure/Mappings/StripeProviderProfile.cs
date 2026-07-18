using AutoMapper;
using System.Globalization;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Infrastructure.Providers.Stripe.Models;

namespace TripRadar.Server.Infrastructure.Mappings;

public sealed class StripeProviderProfile : Profile
{
    public StripeProviderProfile()
    {
        CreateMap<SubscriptionResponse, StripeSubscriptionInfo>()
            .ConvertUsing<SubscriptionResponseToStripeSubscriptionInfoConverter>();

        CreateMap<PaymentMethodResponse, StripePaymentMethodInfo>()
            .ConvertUsing<PaymentMethodResponseToStripePaymentMethodInfoConverter>();

        CreateMap<UsageSummaryResponse, StripeUsageSummaryInfo>()
            .ConvertUsing<UsageSummaryResponseToStripeUsageSummaryInfoConverter>();

        CreateMap<InvoiceResponse, StripeInvoiceInfo>()
            .ConvertUsing<InvoiceResponseToStripeInvoiceInfoConverter>();
    }

    private sealed class SubscriptionResponseToStripeSubscriptionInfoConverter : ITypeConverter<SubscriptionResponse, StripeSubscriptionInfo>
    {
        public StripeSubscriptionInfo Convert(SubscriptionResponse source, StripeSubscriptionInfo destination, ResolutionContext context)
        {
            return new StripeSubscriptionInfo
            {
                Id = source.Id,
                Status = source.Status,
                CurrentPeriodStart = source.CurrentPeriodStart,
                CurrentPeriodEnd = source.CurrentPeriodEnd,
                CancelAtPeriodEnd = source.CancelAtPeriodEnd,
                CanceledAt = source.CanceledAt,
                PriceAmount = (int)source.PriceAmount,
                Currency = source.Currency,
                PriceId = source.PriceId,
                TrialEnd = source.TrialEnd,
                DiscountPercent = source.DiscountPercent.HasValue ? (int?)Math.Round(source.DiscountPercent.Value) : null,
                DefaultPaymentMethodId = source.DefaultPaymentMethodId
            };
        }
    }

    private sealed class PaymentMethodResponseToStripePaymentMethodInfoConverter : ITypeConverter<PaymentMethodResponse, StripePaymentMethodInfo>
    {
        public StripePaymentMethodInfo Convert(PaymentMethodResponse source, StripePaymentMethodInfo destination, ResolutionContext context)
        {
            return new StripePaymentMethodInfo
            {
                Id = source.Id,
                Type = source.Type,
                Brand = source.Card?.Brand ?? "unknown",
                Last4 = source.Card?.Last4 ?? "0000",
                ExpMonth = source.Card?.ExpMonth ?? 0,
                ExpYear = source.Card?.ExpYear ?? 0,
                Country = source.Card?.Country,
                Name = source.BillingDetails?.Name,
                Email = source.BillingDetails?.Email,
                AddressCountry = source.BillingDetails?.Country,
                AddressPostalCode = source.BillingDetails?.PostalCode,
                CreatedAt = source.CreatedAt
            };
        }
    }

    private sealed class UsageSummaryResponseToStripeUsageSummaryInfoConverter : ITypeConverter<UsageSummaryResponse, StripeUsageSummaryInfo>
    {
        public StripeUsageSummaryInfo Convert(UsageSummaryResponse source, StripeUsageSummaryInfo destination, ResolutionContext context)
        {
            var summary = new StripeUsageSummaryInfo
            {
                HasMeteredBilling = source.HasMeteredBilling,
                CurrentPeriod = MapBillingPeriod(source.CurrentPeriod),
                Usage = new Dictionary<string, StripeUsageMetricInfo>()
            };

            foreach (var (key, metric) in source.Usage)
            {
                summary.Usage[key] = MapUsageMetric(metric);
            }

            return summary;
        }

        private static StripeBillingPeriodInfo MapBillingPeriod(BillingPeriod? period)
        {
            if (period is null)
            {
                return new StripeBillingPeriodInfo();
            }

            return new StripeBillingPeriodInfo
            {
                Start = period.Start,
                End = period.End,
                DaysRemaining = period.DaysRemaining
            };
        }

        private static StripeUsageMetricInfo MapUsageMetric(UsageMetric? metric)
        {
            if (metric is null)
            {
                return new StripeUsageMetricInfo();
            }

            var used = TryGetDecimal(metric, "Used", "Usage", "Quantity", "Count", "Value", "Total", "TokensUsed", "Tokens");
            var limit = TryGetDecimal(metric, "Limit", "Quota", "Max", "Cap");
            var unit = TryGetString(metric, "Unit", "Units");

            return new StripeUsageMetricInfo
            {
                Used = used,
                Limit = limit,
                Unit = unit
            };
        }

        private static decimal? TryGetDecimal(object source, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var prop = source.GetType().GetProperty(name);
                if (prop is null)
                {
                    continue;
                }

                var value = prop.GetValue(source);
                switch (value)
                {
                    case null:
                        continue;
                    case decimal dec:
                        return dec;
                    case int i:
                        return i;
                    case long l:
                        return l;
                    case double d:
                        return (decimal)d;
                    case float f:
                        return (decimal)f;
                    case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                        return parsed;
                }
            }

            return null;
        }

        private static string? TryGetString(object source, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var prop = source.GetType().GetProperty(name);

                var value = prop?.GetValue(source);
                if (value is string s)
                {
                    return s;
                }
            }

            return null;
        }
    }

    private sealed class InvoiceResponseToStripeInvoiceInfoConverter : ITypeConverter<InvoiceResponse, StripeInvoiceInfo>
    {
        public StripeInvoiceInfo Convert(InvoiceResponse source, StripeInvoiceInfo destination, ResolutionContext context)
        {
            return new StripeInvoiceInfo
            {
                Id = source.Id,
                Number = source.Number,
                Status = source.Status,
                Currency = source.Currency,
                CreatedAt = source.CreatedAt,
                AmountDue = source.AmountDue,
                AmountPaid = source.AmountPaid,
                DueDate = source.DueDate,
                PaidAt = source.PaidAt,
                InvoicePdfUrl = source.InvoicePdfUrl,
                HostedInvoiceUrl = source.HostedInvoiceUrl,
                Description = source.Description,
                SubscriptionId = source.SubscriptionId,
                CardBrand = source.CardBrand,
                CardLast4 = source.CardLast4,
                PaymentMethodType = source.PaymentMethodType,
                ReceiptUrl = source.ReceiptUrl
            };
        }
    }
}
