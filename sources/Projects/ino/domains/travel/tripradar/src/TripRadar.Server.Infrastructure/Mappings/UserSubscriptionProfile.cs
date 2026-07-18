using AutoMapper;
using TripRadar.Server.Db.Models;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Mappings;

public class UserSubscriptionProfile : Profile
{
    public UserSubscriptionProfile()
    {
        CreateMap<UserSubscriptions, UserSubscription>();

        CreateMap<UserSubscription, UserSubscriptions>()
            .ForMember(dest => dest.User, opt => opt.Ignore());
    }
}
