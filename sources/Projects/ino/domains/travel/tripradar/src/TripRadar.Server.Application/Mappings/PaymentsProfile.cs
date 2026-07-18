using AutoMapper;
using System.Globalization;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Application.Mappings;

public sealed class PaymentsProfile : Profile
{
    public PaymentsProfile()
    {
        CreateMap<StripeSubscriptionInfo, UserSubscriptionDTO>()
            .ForMember(dest => dest.TierType, opt => opt.Ignore())
            .ForMember(dest => dest.NextInvoiceDate, opt => opt.Ignore())
            .ForMember(dest => dest.PendingTierType, opt => opt.Ignore())
            .ForMember(dest => dest.PendingTierEffectiveDate, opt => opt.Ignore());

        CreateMap<StripePaymentMethodInfo, PaymentMethodItemDTO>()
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => CapitalizeFirst(src.Type)))
            .ForMember(dest => dest.Card, opt => opt.MapFrom(src => src))
            .ForMember(dest => dest.BillingDetails, opt => opt.MapFrom(src => src))
            .ForMember(dest => dest.IsDefault, opt => opt.Ignore());

        CreateMap<StripePaymentMethodInfo, CardDTO>()
            .ForMember(dest => dest.Brand, opt => opt.MapFrom(src => CapitalizeFirst(src.Brand)));

        CreateMap<StripePaymentMethodInfo, BillingDTO?>()
            .ConvertUsing<StripePaymentMethodInfoToBillingDtoConverter>();
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

    private sealed class StripePaymentMethodInfoToBillingDtoConverter : ITypeConverter<StripePaymentMethodInfo, BillingDTO?>
    {
        public BillingDTO? Convert(StripePaymentMethodInfo source, BillingDTO? destination, ResolutionContext context)
        {
            if (source.Name is null && source.Email is null && source.AddressCountry is null && source.AddressPostalCode is null)
            {
                return null;
            }

            return new BillingDTO
            {
                Name = source.Name,
                Email = source.Email,
                Address = source.AddressCountry is not null || source.AddressPostalCode is not null
                    ? new AddressDTO
                    {
                        Country = source.AddressCountry,
                        PostalCode = source.AddressPostalCode
                    }
                    : null
            };
        }
    }
}
