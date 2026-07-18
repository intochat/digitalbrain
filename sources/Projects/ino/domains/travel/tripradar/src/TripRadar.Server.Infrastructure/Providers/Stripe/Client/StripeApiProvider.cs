using Microsoft.Extensions.Options;
using Stripe;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Providers.Stripe.Helpers;
using TripRadar.Server.Infrastructure.Providers.Stripe.Models;
using TripRadar.Server.Infrastructure.Providers.Stripe.Settings;

namespace TripRadar.Server.Infrastructure.Providers.Stripe.Client;

public class StripeApiProvider : IStripeApiProvider
{
    private readonly StripeApiSettings _stripeSettings;

    public StripeApiProvider(IOptions<StripeApiSettings> stripeSettings)
    {
        _stripeSettings = stripeSettings.Value;
        StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
    }

    public async Task<StripeSubscriptionCheckoutResult> CreateSubscriptionCheckoutAsync(
        string customerId,
        string priceId,
        string? couponId,
        Dictionary<string, string>? subscriptionMetadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionService = new SubscriptionService();
            var options = new SubscriptionCreateOptions
            {
                Customer = customerId,
                CollectionMethod = "charge_automatically",
                PaymentBehavior = "default_incomplete",
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Price = priceId
                    }
                ],
                PaymentSettings = new SubscriptionPaymentSettingsOptions
                {
                    SaveDefaultPaymentMethod = "on_subscription",
                    PaymentMethodTypes = [StripeApiHelper.CardPaymentMethod]
                },
                Metadata = subscriptionMetadata,
                Expand = ["latest_invoice.payment_intent"]
            };

            if (!string.IsNullOrWhiteSpace(couponId))
                options.AddExtraParam("discounts[0][coupon]", couponId);

            var subscription = await subscriptionService.CreateAsync(options, cancellationToken: cancellationToken);
            var invoice = subscription.LatestInvoice;
            var paymentIntent = invoice?.PaymentIntent;
            var amountSubtotal = invoice?.Subtotal ?? 0;
            var amountTotal = invoice?.Total ?? 0;

            return new StripeSubscriptionCheckoutResult
            {
                ClientSecret = paymentIntent?.ClientSecret,
                AmountSubtotal = amountSubtotal,
                AmountDiscount = Math.Max(amountSubtotal - amountTotal, 0),
                AmountTotal = amountTotal,
                Currency = invoice?.Currency ?? string.Empty
            };
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to create Stripe subscription checkout for customer '{customerId}' with price '{priceId}': {ex.Message}", ex);
        }
    }

    public async Task<string> CreateCustomerAsync(string email, string? name = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerService = new CustomerService();
            var options = StripeApiHelper.CreateCustomerOptions(email, name, _stripeSettings);
            var customer = await customerService.CreateAsync(options, cancellationToken: cancellationToken);
            return customer.Id;
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to create Stripe customer for email '{email}': {ex.Message}", ex);
        }
    }

    public async Task<(string Status, string? CurrentPriceId, DateTime? CurrentPeriodEnd)> GetSubscriptionDetailsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(subscriptionId, cancellationToken: cancellationToken);
            var currentPriceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
            var currentPeriodEnd = subscription.CurrentPeriodEnd;
            return (subscription.Status, currentPriceId, currentPeriodEnd);
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to retrieve Stripe subscription details for '{subscriptionId}': {ex.Message}", ex);
        }
    }

    public async Task UpdateSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(subscriptionId, new SubscriptionGetOptions { Expand = ["items"] }, cancellationToken: cancellationToken);

            var subscriptionItemId = subscription.Items?.Data?.FirstOrDefault()?.Id;
            if (string.IsNullOrWhiteSpace(subscriptionItemId))
                throw new InternalErrorException($"Failed to update Stripe subscription '{subscriptionId}': subscription item not found.");

            var updateOptions = new SubscriptionUpdateOptions
            {
                ProrationBehavior = "none",
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Id = subscriptionItemId,
                        Price = priceId
                    }
                ]
            };

            await subscriptionService.UpdateAsync(subscriptionId, updateOptions, cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to update Stripe subscription '{subscriptionId}' price to '{priceId}': {ex.Message}", ex);
        }
    }

    public async Task<string> CreateSetupIntentAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var setupIntentService = new SetupIntentService();
            var options = new SetupIntentCreateOptions
            {
                Customer = customerId,
                PaymentMethodTypes = [StripeApiHelper.CardPaymentMethod],
                Usage = StripeApiHelper.DefaultUsage
            };
            var setupIntent = await setupIntentService.CreateAsync(options, cancellationToken: cancellationToken);
            return setupIntent.ClientSecret;
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to create Stripe setup intent for customer '{customerId}': {ex.Message}", ex);
        }
    }

    public async Task<(string RefundId, string PaymentIntentId, int Amount, string Currency, string Status, string Reason, DateTime Created, Dictionary<string, string>? Metadata)> CreateRefundAsync(string paymentIntentId, int? amount = null, string reason = StripeApiHelper.DefaultRefundReason, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var validatedReason = StripeApiHelper.ValidateAndNormalizeRefundReason(reason);
            var refundService = new RefundService();
            var options = StripeApiHelper.CreateRefundOptions(paymentIntentId, amount, validatedReason, metadata, _stripeSettings);
            var refund = await refundService.CreateAsync(options, cancellationToken: cancellationToken);
            return (refund.Id, refund.PaymentIntentId, (int)refund.Amount, refund.Currency, refund.Status, refund.Reason, refund.Created, refund.Metadata);
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to create Stripe refund for payment intent '{paymentIntentId}': {ex.Message}", ex);
        }
    }

    public async Task<string?> GetLatestPaymentIntentFromSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var invoiceService = new InvoiceService();
            var options = new InvoiceListOptions
            {
                Subscription = subscriptionId,
                Limit = 1,
                Status = StripeApiHelper.PaidInvoiceStatus
            };
            var invoices = await invoiceService.ListAsync(options, cancellationToken: cancellationToken);
            var latestInvoice = invoices.Data?.FirstOrDefault();
            return latestInvoice?.PaymentIntentId;
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to retrieve latest payment intent for subscription '{subscriptionId}': {ex.Message}", ex);
        }
    }

    public async Task<string> CreateInvoiceItemAsync(string customerId, int amountCents, string currency, string description, Dictionary<string, string>? metadata = null, string? subscriptionId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new InvoiceItemService();
            var options = new InvoiceItemCreateOptions
            {
                Customer = customerId,
                Amount = amountCents,
                Currency = currency,
                Description = description,
                Subscription = subscriptionId,
                Metadata = metadata
            };
            var requestOptions = string.IsNullOrWhiteSpace(idempotencyKey) ? null : new RequestOptions { IdempotencyKey = idempotencyKey };
            var item = await service.CreateAsync(options, requestOptions, cancellationToken: cancellationToken);
            return item.Id;
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to create invoice item for customer '{customerId}': {ex.Message}",
                ex);
        }
    }

    public async Task<string> CreateAndPayInvoiceAsync(string customerId, Dictionary<string, string>? metadata = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var invoiceService = new InvoiceService();
            var createOptions = new InvoiceCreateOptions
            {
                Customer = customerId,
                AutoAdvance = true,
                CollectionMethod = "charge_automatically",
                Metadata = metadata
            };

            var requestOptions = string.IsNullOrWhiteSpace(idempotencyKey) ? null : new RequestOptions { IdempotencyKey = idempotencyKey };
            var invoice = await invoiceService.CreateAsync(createOptions, requestOptions, cancellationToken: cancellationToken);
            var finalized = await invoiceService.FinalizeInvoiceAsync(invoice.Id, cancellationToken: cancellationToken);
            var paid = await invoiceService.PayAsync(finalized.Id, cancellationToken: cancellationToken);
            return paid.Id;
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to create/pay invoice for customer '{customerId}': {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionResponse?> GetSubscriptionByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionService = new SubscriptionService();
            var options = new SubscriptionListOptions
            {
                Customer = customerId,
                Limit = 1,
                Expand = ["data.default_payment_method", "data.items.data.price"]
            };

            var subscriptions = await subscriptionService.ListAsync(options, cancellationToken: cancellationToken);
            var subscription = subscriptions.Data?.FirstOrDefault();
            return subscription == null ? null : MapToSubscriptionResponse(subscription);
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException(
                $"Failed to retrieve subscription for customer '{customerId}': {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionResponse?> GetSubscriptionByIdAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(subscriptionId, new SubscriptionGetOptions
            {
                Expand = ["default_payment_method", "items.data.price"]
            }, cancellationToken: cancellationToken);

            return MapToSubscriptionResponse(subscription);
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to retrieve subscription '{subscriptionId}': {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionResponse> ToggleSubscriptionAsync(string subscriptionId, bool activate, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionService = new SubscriptionService();
            var options = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = !activate,
                Expand = ["default_payment_method", "items.data.price"]
            };

            var subscription = await subscriptionService.UpdateAsync(subscriptionId, options, cancellationToken: cancellationToken);
            return MapToSubscriptionResponse(subscription);
        }
        catch (StripeException ex)
        {
            var action = activate ? "activate" : "deactivate";
            throw new InternalErrorException($"Failed to {action} subscription '{subscriptionId}': {ex.Message}", ex);
        }
    }

    public async Task<PaymentMethodsListResponse> GetPaymentMethodsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var paymentMethodService = new PaymentMethodService();
            var options = new PaymentMethodListOptions
            {
                Customer = customerId,
                Type = StripeApiHelper.CardPaymentMethod
            };

            var paymentMethods = await paymentMethodService.ListAsync(options, cancellationToken: cancellationToken);

            // Get customer's default payment method and subscription status
            var customerService = new CustomerService();
            var customer = await customerService.GetAsync(customerId, new CustomerGetOptions
            {
                Expand = ["subscriptions"]
            }, cancellationToken: cancellationToken);

            var defaultPaymentMethodId = customer.InvoiceSettings?.DefaultPaymentMethodId;
            var hasActiveSubscription = customer.Subscriptions?.Data?
                .Any(s => s.Status == StripeApiHelper.ActiveSubscriptionStatus) ?? false;

            var mappedMethods = paymentMethods.Data
                .OrderByDescending(pm => pm.Created)
                .Select(pm => MapToPaymentMethodResponse(pm, pm.Id == defaultPaymentMethodId))
                .ToList();

            return new PaymentMethodsListResponse
            {
                PaymentMethods = mappedMethods,
                HasActiveSubscription = hasActiveSubscription,
                DefaultPaymentMethodId = defaultPaymentMethodId
            };
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to retrieve payment methods for customer '{customerId}': {ex.Message}", ex);
        }
    }

    public async Task<DetachPaymentMethodResponse> DetachPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get current payment methods and subscription status
            var currentMethods = await GetPaymentMethodsAsync(customerId, cancellationToken);

            // Validate: cannot remove last payment method if there's an active subscription
            if (currentMethods is { HasActiveSubscription: true, PaymentMethods.Count: <= 1 })
            {
                throw new InvalidRequestException("Cannot remove the last payment method while there is an active subscription.");
            }

            // Verify the payment method belongs to this customer
            var methodToRemove = currentMethods.PaymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
            if (methodToRemove == null)
            {
                throw new ObjectNotFoundException($"Payment method '{paymentMethodId}' not found.");
            }

            var paymentMethodService = new PaymentMethodService();
            await paymentMethodService.DetachAsync(paymentMethodId, cancellationToken: cancellationToken);

            string? newDefaultId = null;

            // If we removed the default, set a new one
            if (methodToRemove.IsDefault && currentMethods.PaymentMethods.Count > 1)
            {
                var newDefault = currentMethods.PaymentMethods
                    .Where(pm => pm.Id != paymentMethodId)
                    .OrderByDescending(pm => pm.CreatedAt)
                    .First();

                await SetDefaultPaymentMethodAsync(customerId, newDefault.Id, cancellationToken);
                newDefaultId = newDefault.Id;
            }

            return new DetachPaymentMethodResponse
            {
                Message = "Payment method removed successfully.",
                NewDefaultPaymentMethodId = newDefaultId,
                RemainingPaymentMethods = currentMethods.PaymentMethods.Count - 1
            };
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to detach payment method '{paymentMethodId}': {ex.Message}", ex);
        }
    }

    public async Task<PaymentMethodResponse> SetDefaultPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Update customer's default payment method
            var customerService = new CustomerService();
            await customerService.UpdateAsync(customerId, new CustomerUpdateOptions
            {
                InvoiceSettings = new CustomerInvoiceSettingsOptions
                {
                    DefaultPaymentMethod = paymentMethodId
                }
            }, cancellationToken: cancellationToken);

            // Also update subscription's default payment method if there's an active one
            var subscriptionService = new SubscriptionService();
            var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
            {
                Customer = customerId,
                Status = StripeApiHelper.ActiveSubscriptionStatus,
                Limit = 1
            }, cancellationToken: cancellationToken);

            var activeSubscription = subscriptions.Data?.FirstOrDefault();
            if (activeSubscription != null)
            {
                await subscriptionService.UpdateAsync(activeSubscription.Id, new SubscriptionUpdateOptions
                {
                    DefaultPaymentMethod = paymentMethodId
                }, cancellationToken: cancellationToken);
            }

            // Get and return the updated payment method
            var paymentMethodService = new PaymentMethodService();
            var paymentMethod = await paymentMethodService.GetAsync(paymentMethodId,
                cancellationToken: cancellationToken);

            return MapToPaymentMethodResponse(paymentMethod, isDefault: true);
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException(
                $"Failed to set default payment method '{paymentMethodId}': {ex.Message}", ex);
        }
    }

    public async Task<InvoicesListResponse> GetInvoicesAsync(string customerId, int limit = 20, string? startingAfter = null, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var invoiceService = new InvoiceService();
            var options = new InvoiceListOptions
            {
                Customer = customerId,
                Limit = Math.Min(limit, 100), // Cap at 100
                Expand =
                [
                    "data.charge",
                    "data.payment_intent",
                    "data.payment_intent.latest_charge",
                    "data.payment_intent.payment_method"
                ]
            };

            if (!string.IsNullOrEmpty(startingAfter))
                options.StartingAfter = startingAfter;

            if (!string.IsNullOrEmpty(status))
                options.Status = status;

            var invoices = await invoiceService.ListAsync(options, cancellationToken: cancellationToken);

            var mappedInvoices = invoices.Data
                .Select(MapToInvoiceResponse)
                .ToList();

            return new InvoicesListResponse
            {
                Invoices = mappedInvoices,
                HasMore = invoices.HasMore,
                LastInvoiceId = mappedInvoices.LastOrDefault()?.Id
            };
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to retrieve invoices for customer '{customerId}': {ex.Message}", ex);
        }
    }

    public async Task<UsageSummaryResponse> GetUsageSummaryAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get customer's active subscription
            var subscriptionService = new SubscriptionService();
            var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
            {
                Customer = customerId,
                Status = StripeApiHelper.ActiveSubscriptionStatus,
                Limit = 1,
                Expand = ["data.items.data.price"]
            }, cancellationToken: cancellationToken);

            var subscription = subscriptions.Data?.FirstOrDefault();

            if (subscription == null)
            {
                return new UsageSummaryResponse
                {
                    CurrentPeriod = new BillingPeriod
                    {
                        Start = DateTime.UtcNow,
                        End = DateTime.UtcNow,
                        DaysRemaining = 0
                    },
                    HasMeteredBilling = false
                };
            }

            var daysRemaining = (subscription.CurrentPeriodEnd - DateTime.UtcNow).Days;
            var usage = new Dictionary<string, UsageMetric>();

            var hasMeteredBilling = false;
            foreach (var item in (subscription.Items?.Data ?? []).Where(item => item.Price?.Recurring?.UsageType == StripeApiHelper.MeteredUsageType))
            {
                hasMeteredBilling = true;

                var usageRecordSummaryService = new UsageRecordSummaryService();
                var usageSummaries = await usageRecordSummaryService.ListAsync(item.Id,
                    new UsageRecordSummaryListOptions { Limit = 1 },
                    cancellationToken: cancellationToken);

                var summary = usageSummaries.Data?.FirstOrDefault();
                if (summary != null)
                {
                    var metricName = item.Price.Nickname ?? item.Price.Id;
                    usage[metricName] = new UsageMetric
                    {
                        Current = summary.TotalUsage,
                        Limit = -1, // Metered billing typically has no hard limit
                        Percentage = 0,
                        ResetDate = subscription.CurrentPeriodEnd
                    };
                }
            }

            return new UsageSummaryResponse
            {
                CurrentPeriod = new BillingPeriod
                {
                    Start = subscription.CurrentPeriodStart,
                    End = subscription.CurrentPeriodEnd,
                    DaysRemaining = Math.Max(0, daysRemaining)
                },
                Usage = usage,
                HasMeteredBilling = hasMeteredBilling
            };
        }
        catch (StripeException ex)
        {
            throw new InternalErrorException($"Failed to retrieve usage summary for customer '{customerId}': {ex.Message}", ex);
        }
    }

    private static SubscriptionResponse MapToSubscriptionResponse(Subscription subscription)
    {
        var item = subscription.Items?.Data?.FirstOrDefault();
        var price = item?.Price;

        return new SubscriptionResponse
        {
            Id = subscription.Id,
            Status = subscription.Status,
            CurrentPeriodStart = subscription.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CanceledAt = subscription.CanceledAt,
            PriceAmount = price?.UnitAmount ?? 0,
            Currency = subscription.Currency ?? "usd",
            NextBillingDate = subscription.CancelAtPeriodEnd ? null : subscription.CurrentPeriodEnd,
            TrialEnd = subscription.TrialEnd,
            DiscountPercent = subscription.Discount?.Coupon?.PercentOff,
            PriceId = price?.Id,
            ProductId = price?.ProductId,
            DefaultPaymentMethodId = subscription.DefaultPaymentMethodId
        };
    }

    private static PaymentMethodResponse MapToPaymentMethodResponse(PaymentMethod pm, bool isDefault)
    {
        return new PaymentMethodResponse
        {
            Id = pm.Id,
            Type = pm.Type,
            Card = new CardDetails
            {
                Brand = pm.Card?.Brand ?? "unknown",
                Last4 = pm.Card?.Last4 ?? "****",
                ExpMonth = (int)(pm.Card?.ExpMonth ?? 0),
                ExpYear = (int)(pm.Card?.ExpYear ?? 0),
                Country = pm.Card?.Country
            },
            BillingDetails = pm.BillingDetails != null
                ? new BillingDetails
                {
                    Name = pm.BillingDetails.Name,
                    Email = pm.BillingDetails.Email,
                    Country = pm.BillingDetails.Address?.Country,
                    PostalCode = pm.BillingDetails.Address?.PostalCode
                }
                : null,
            IsDefault = isDefault,
            CreatedAt = pm.Created
        };
    }

    private static InvoiceResponse MapToInvoiceResponse(Invoice invoice)
    {
        var paymentDetails = ResolveInvoicePaymentDetails(invoice);

        return new InvoiceResponse
        {
            Id = invoice.Id,
            Number = invoice.Number,
            Status = invoice.Status,
            AmountDue = invoice.AmountDue,
            AmountPaid = invoice.AmountPaid,
            Currency = invoice.Currency ?? "usd",
            CreatedAt = invoice.Created,
            DueDate = invoice.DueDate,
            PaidAt = invoice.StatusTransitions?.PaidAt,
            InvoicePdfUrl = invoice.InvoicePdf,
            HostedInvoiceUrl = invoice.HostedInvoiceUrl,
            Description = invoice.Description,
            SubscriptionId = invoice.SubscriptionId,
            CardBrand = paymentDetails.CardBrand,
            CardLast4 = paymentDetails.CardLast4,
            PaymentMethodType = paymentDetails.PaymentMethodType,
            ReceiptUrl = paymentDetails.ReceiptUrl
        };
    }

    private static (string? CardBrand, string? CardLast4, string? PaymentMethodType, string? ReceiptUrl) ResolveInvoicePaymentDetails(Invoice invoice)
    {
        var charge = invoice.PaymentIntent?.LatestCharge ?? invoice.Charge;
        var paymentMethod = invoice.PaymentIntent?.PaymentMethod;
        var paymentMethodCard = paymentMethod?.Card;
        var chargeCardDetails = charge?.PaymentMethodDetails?.Card;

        var cardBrand = paymentMethodCard?.Brand ?? chargeCardDetails?.Brand;
        var cardLast4 = paymentMethodCard?.Last4 ?? chargeCardDetails?.Last4;
        var paymentMethodType = paymentMethod?.Type ?? charge?.PaymentMethodDetails?.Type;

        return (
            NormalizeMetadataValue(cardBrand),
            NormalizeMetadataValue(cardLast4),
            NormalizeMetadataValue(paymentMethodType),
            NormalizeMetadataValue(charge?.ReceiptUrl)
        );
    }
    private static string? NormalizeMetadataValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

