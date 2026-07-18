using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class InvoiceItemCreatedHandler(ILogger<InvoiceItemCreatedHandler> logger) : NoOpStripeEventHandler<InvoiceItem>(StripeConstants.WebhookEvents.InvoiceItem.Created, logger);
