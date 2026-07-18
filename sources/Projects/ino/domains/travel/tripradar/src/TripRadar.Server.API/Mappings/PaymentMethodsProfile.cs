using AutoMapper;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.API.Mappings;

public sealed class PaymentMethodsProfile : Profile
{
    public PaymentMethodsProfile()
    {
        CreateMap<PaymentMethodsDTO, GetPaymentMethodsResponse>();
        CreateMap<PaymentMethodItemDTO, PaymentMethodDto>();
        CreateMap<CardDTO, CardDetailsDto>();
        CreateMap<BillingDTO, BillingDetailsDto>();
        CreateMap<AddressDTO, BillingAddressDto>();
    }
}

