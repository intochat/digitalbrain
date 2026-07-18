using AutoMapper;
using System.Globalization;
using TripRadar.Server.API.Contracts.Enums;
using TripRadar.Server.API.Contracts.Models;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Delete;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.API.Contracts.Responses.Update;
using TripRadar.Server.Application.UseCases.Payments.Commands.CreateRefund;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.API.Mappings;

public sealed class PaymentProfile : Profile
{
    public PaymentProfile()
    {
        CreateMap<Payment, PriceResponse>()
            .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount))
            .ForMember(dest => dest.Currency, opt => opt.MapFrom(src => src.CurrencyCode))
            .ForMember(dest => dest.TierName, opt => opt.MapFrom(src => src.TierName))
            .ForMember(dest => dest.BillingPeriodName, opt => opt.MapFrom(src => src.BillingPeriodName))
            .ForMember(dest => dest.TokensPerMonthLimit, opt => opt.MapFrom(src => src.TokensPerMonthLimit));

        CreateMap<RefundReasonType, RefundType>()
            .ConvertUsing(src => ConvertRefundReason(src));

        CreateMap<RefundRequest, CreateRefundCommand>()
            .ConstructUsing((src, context) => new CreateRefundCommand(
                string.Empty,
                context.Mapper.Map<RefundType>(src.ReasonType),
                src.Metadata));

        CreateMap<RefundResult, CreateRefundResponse>();

        CreateMap<UserSubscriptionDTO, GetUserSubscriptionResponse>()
            .ForMember(dest => dest.BillingPeriod, opt => opt.MapFrom(src => CapitalizeFirst(src.BillingPeriod)))
            .ForMember(dest => dest.PriceAmount, opt => opt.MapFrom(src => ConvertCentsToDollars(src.PriceAmount)))
            .ForMember(
                dest => dest.PendingTierType,
                opt => opt.MapFrom(src => string.IsNullOrWhiteSpace(src.PendingTierType) ? null : CapitalizeFirst(src.PendingTierType)))
            .ForMember(dest => dest.PendingTierEffectiveDate, opt => opt.MapFrom(src => src.PendingTierEffectiveDate));

        CreateMap<DeletePaymentMethodByCardResponseDTO, DeletePaymentMethodResponse>();

        CreateMap<UpdateDefaultPaymentMethodDto, UpdateDefaultPaymentMethodResponse>();
        CreateMap<ToggleSubscriptionDTO, ToggleSubscriptionResponse>();

        CreateMap<StripeBillingPeriodInfo, BillingPeriodResponse>();
        CreateMap<StripeUsageMetricInfo, UsageMetricResponse>();

        CreateMap<StripeInvoiceInfo, InvoiceResponse>()
            .ForMember(dest => dest.Cursor, opt => opt.MapFrom(src => CursorExtensions.EncodeCursor(src.Id)))
            .ForMember(dest => dest.Number, opt => opt.MapFrom(src => src.Number))
            .ForMember(dest => dest.AmountDue, opt => opt.MapFrom(src => ConvertCentsToDollars((int)src.AmountDue)))
            .ForMember(dest => dest.AmountPaid, opt => opt.MapFrom(src => ConvertCentsToDollars((int)src.AmountPaid)))
            .ForMember(dest => dest.DueDate, opt => opt.MapFrom(src => src.DueDate))
            .ForMember(dest => dest.PaidAt, opt => opt.MapFrom(src => src.PaidAt))
            .ForMember(dest => dest.InvoicePdfUrl, opt => opt.MapFrom(src => src.InvoicePdfUrl))
            .ForMember(dest => dest.HostedInvoiceUrl, opt => opt.MapFrom(src => src.HostedInvoiceUrl))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
            .ForMember(dest => dest.SubscriptionId, opt => opt.MapFrom(src => src.SubscriptionId))
            .ForMember(dest => dest.CardBrand, opt => opt.MapFrom(src => src.CardBrand))
            .ForMember(dest => dest.CardLast4, opt => opt.MapFrom(src => src.CardLast4))
            .ForMember(dest => dest.PaymentMethodType, opt => opt.MapFrom(src => src.PaymentMethodType))
            .ForMember(dest => dest.ReceiptUrl, opt => opt.MapFrom(src => src.ReceiptUrl));

        CreateMap<InvoicesDTO, GetInvoicesResponse>()
            .ForMember(dest => dest.StartingAfter, opt => opt.MapFrom(src => src.StartingAfter))
            .ForMember(dest => dest.HasMore, opt => opt.MapFrom(src => src.HasMore))
            .ForMember(dest => dest.NextCursor, opt => opt.MapFrom(src => src.NextCursor));
    }

    private static string CapitalizeFirst(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return char.ToUpper(trimmed[0], CultureInfo.InvariantCulture) + trimmed[1..];
    }

    private static decimal ConvertCentsToDollars(int amountInCents)
    {
        return amountInCents / 100m;
    }

    private static RefundType ConvertRefundReason(RefundReasonType apiReasonType)
    {
        return apiReasonType switch
        {
            RefundReasonType.RequestedByCustomer => RefundType.RequestedByCustomer,
            RefundReasonType.Duplicate => RefundType.Duplicate,
            RefundReasonType.Fraudulent => RefundType.Fraudulent,
            RefundReasonType.SubscriptionCanceled => RefundType.SubscriptionCanceled,
            RefundReasonType.ServiceNotDelivered => RefundType.ServiceNotDelivered,
            _ => throw new ArgumentOutOfRangeException(nameof(apiReasonType), apiReasonType, "Invalid refund type")
        };
    }
}
