using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.API.Filters;
using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Requests.Delete;
using TripRadar.Server.API.Contracts.Requests.Update;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Delete;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.API.Contracts.Responses.Update;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.UseCases.Payments.Commands.DeletePaymentMethodByCard;
using TripRadar.Server.Application.UseCases.Payments.Commands.ToggleSubscription;
using TripRadar.Server.Application.UseCases.Payments.Commands.UpdateDefaultPaymentMethod;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetAllPrices;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetInvoices;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetOverageUsage;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetPaymentMethods;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetUserSubscription;
using TripRadar.Server.Application.UseCases.Payments.Commands.CancelSubscription;
using TripRadar.Server.Application.UseCases.Payments.Commands.CreateRefund;
using TripRadar.Server.Application.UseCases.Payments.Commands.CreateSetupIntent;
using TripRadar.Server.Application.UseCases.Payments.Commands.CreateSubscription;
using TripRadar.Server.Application.UseCases.Payments.Commands.DowngradeSubscription;
using TripRadar.Server.Application.UseCases.Payments.Commands.UpdatePayAsYouGo;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Infrastructure.Contracts.Handlers;

namespace TripRadar.Server.API.Controllers;

[Route("api/v{version:apiVersion}/payments")]
[RequireUsername]
public class PaymentController(IMediator mediator, IStripeWebhookHandler webhookHandler, IMapper mapper, ILogger<PaymentController> logger) : BaseController
{
    private const int MaxStripeWebhookBodyBytes = 100 * 1024;

    [Authorize]
    [HttpPost("subscription-checkouts")]
    [ProducesResponseType(typeof(CreateSubscriptionCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSubscriptionCheckout([FromBody] CreateSubscriptionCheckoutRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateSubscriptionCheckoutCommand(GetUsername(), (int)request.TargetTierType, (int)request.BillingPeriodType, request.PromoCode), ct);
        return result.IsFailure ? result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.SameTierUpgrade.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.TierPriceNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.PromoCodeNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.PromoCodeExpired.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.PromoCodeInactive.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.PromoCodeNotStarted.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.PromoCodeUsageLimitExceeded.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.PromoCodeAlreadyUsedByUser.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.StripeAuthenticationFailed.Code => StatusCode(StatusCodes.Status502BadGateway, result.Error),
                _ when result.Error.Code == Errors.StripeApiConnectionFailed.Code => StatusCode(StatusCodes.Status503ServiceUnavailable, result.Error),
                _ when result.Error.Code == Errors.StripeInvalidRequestFailed.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.StripeCheckoutSessionCreationFailed.Code => StatusCode(StatusCodes.Status502BadGateway, result.Error),
                _ => BadRequest(result.Error)
            } :
            Ok(new CreateSubscriptionCheckoutResponse
            {
                ClientSecret = result.Value.ClientSecret,
                Currency = result.Value.Currency,
                AmountSubtotal = result.Value.AmountSubtotal,
                AmountDiscount = result.Value.AmountDiscount,
                AmountTotal = result.Value.AmountTotal,
                PromoCode = result.Value.PromoCode
            });
    }

    [Authorize]
    [HttpDelete("subscriptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelSubscriptionRequest? request = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CancelSubscriptionCommand(GetUsername(), request?.CancellationReason), ct);
        return result.IsFailure ? result.Error.Code switch { _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error), _ => BadRequest(result.Error) } : Ok(new { Message = "Subscription cancellation is scheduled for the end of the current billing period. You will receive a confirmation email shortly." });
    }

    [Authorize]
    [HttpPatch("subscriptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DowngradeSubscription([FromBody] DowngradeTierRequest request, CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new DowngradeSubscriptionCommand(GetUsername(), (int)request.TargetTierType, (int)request.BillingPeriodType), cancellationToken);
        return result.IsFailure ? result.Error.Code switch { _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error), _ => BadRequest(result.Error) } : Ok(new { Message = "Subscription downgrade is scheduled for the next billing cycle." });
    }

    [Authorize]
    [HttpPost("setup-intents")]
    [ProducesResponseType(typeof(CreateSetupIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSetupIntent(CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateSetupIntentCommand(GetUsername()), ct);
        return result.IsFailure ? (result.Error.Code switch { _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error), _ => BadRequest(result.Error) }) : Ok(new CreateSetupIntentResponse { ClientSecret = result.Value });
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleWebhook(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Request.ContentLength is > MaxStripeWebhookBodyBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            var (isTooLarge, json) = await RequestBodyReaderHelper.TryReadAsStringAsync(Request, MaxStripeWebhookBodyBytes, cancellationToken);
            if (isTooLarge)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            Request.Headers.TryGetValue("Stripe-Signature", out var stripeSignature);
            var success = await webhookHandler.HandleWebhookAsync(json, stripeSignature, cancellationToken);

            if (!success)
                return BadRequest("Webhook processing failed");

            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe webhook processing failed unexpectedly.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [Authorize]
    [HttpPost("refunds")]
    [ProducesResponseType(typeof(CreateRefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateRefund([FromBody] RefundRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(mapper.Map<CreateRefundCommand>(request) with { Username = GetUsername() }, ct);
        return result.IsFailure ? result.Error.Code switch { _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error), _ => BadRequest(result.Error) } : Ok(mapper.Map<CreateRefundResponse>(result.Value));
    }

    [AllowAnonymous]
    [HttpGet("prices")]
    [ProducesResponseType(typeof(PricesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPrices(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPricesQuery(), ct);
        return result.IsFailure ? BadRequest(result.Error) : Ok(new PricesResponse { Prices = mapper.Map<List<PriceResponse>>(result.Value) });
    }

    [Authorize]
    [HttpGet("overage-usages")]
    [ProducesResponseType(typeof(OverageUsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOverageUsage(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetOverageUsageQuery(GetUsername()), ct);
        return result.IsFailure ? NotFound(result.Error) : Ok(mapper.Map<OverageUsageResponse>(result.Value));
    }

    [Authorize]
    [HttpPatch("metered-events")]
    [ProducesResponseType(typeof(UpdatePayAsYouGoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePayAsYouGo([FromBody] UpdateMeteredBillingRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdatePayAsYouGoCommand(GetUsername(), request.Enabled), ct);

        if (result.IsFailure)
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ => BadRequest(result.Error)
            };

        return Ok(new UpdatePayAsYouGoResponse { Enabled = request.Enabled });
    }

    [Authorize]
    [HttpGet("subscriptions")]
    [ProducesResponseType(typeof(GetUserSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserSubscription(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUserSubscriptionQuery(GetUsername()), ct);
        if (result.IsFailure)
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.SubscriptionNotFound.Code => NotFound(result.Error),
                _ => BadRequest(result.Error)
            };

        return Ok(mapper.Map<GetUserSubscriptionResponse>(result.Value));
    }

    [Authorize]
    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(GetPaymentMethodsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPaymentMethodsQuery(GetUsername()), ct);
        if (result.IsFailure)
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ => BadRequest(result.Error)
            };

        return Ok(mapper.Map<GetPaymentMethodsResponse>(result.Value));
    }

    [Authorize]
    [HttpPatch("payment-methods")]
    [ProducesResponseType(typeof(UpdateDefaultPaymentMethodResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDefaultPaymentMethod([FromBody] UpdateDefaultPaymentMethodRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateDefaultPaymentMethodCommand(GetUsername(), request.Brand, request.Last4, request.ExpMonth, request.ExpYear, request.SetAsDefault), ct);
        if (result.IsFailure)
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.PaymentMethodNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.PaymentMethodAmbiguous.Code => Conflict(result.Error),
                _ => BadRequest(result.Error)
            };

        return Ok(mapper.Map<UpdateDefaultPaymentMethodResponse>(result.Value));
    }

    [Authorize]
    [HttpPost("subscriptions/toggle")]
    [ProducesResponseType(typeof(ToggleSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleSubscription([FromBody] ToggleSubscriptionRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ToggleSubscriptionCommand(GetUsername(), request.Activate), ct);
        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.SubscriptionNotFound.Code => NotFound(result.Error),
                _ => BadRequest(result.Error)
            };
        }

        return Ok(mapper.Map<ToggleSubscriptionResponse>(result.Value));
    }

    [Authorize]
    [HttpGet("invoices")]
    [ProducesResponseType(typeof(GetInvoicesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoices([FromQuery, Range(1, 100)] int limit = 20, [FromQuery] string? startingAfter = null, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetInvoicesQuery(GetUsername(), limit, startingAfter, status), ct);
        if (result.IsFailure)
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ => BadRequest(result.Error)
            };

        return Ok(mapper.Map<GetInvoicesResponse>(result.Value));
    }

    [Authorize]
    [HttpDelete("payment-methods")]
    [ProducesResponseType(typeof(DeletePaymentMethodResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePaymentMethodByCard([FromBody] DeletePaymentMethodByCardRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new DeletePaymentMethodByCardCommand(GetUsername(), request.Brand, request.Last4, request.ExpMonth, request.ExpYear), ct);
        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                _ when result.Error.Code == Errors.UserNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.PaymentMethodNotFound.Code => NotFound(result.Error),
                _ when result.Error.Code == Errors.PaymentMethodAmbiguous.Code => Conflict(result.Error),
                _ when result.Error.Code == Errors.CannotRemoveLastPaymentMethod.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.HasUnpaidInvoices.Code => BadRequest(result.Error),
                _ when result.Error.Code == Errors.PaymentMethodInUse.Code => Conflict(result.Error),
                _ => BadRequest(result.Error)
            };
        }

        return Ok(mapper.Map<DeletePaymentMethodResponse>(result.Value));
    }
}
