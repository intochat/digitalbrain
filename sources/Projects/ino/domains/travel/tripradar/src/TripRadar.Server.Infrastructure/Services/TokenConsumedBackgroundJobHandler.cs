using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Events;

namespace TripRadar.Server.Infrastructure.Services;

public class TokenConsumedBackgroundJobHandler(IBackgroundJobService backgroundJobService) : IDomainEventHandler<TokenConsumedDomainEvent>
{
    public void Handle(TokenConsumedDomainEvent domainEvent)
    {
        if (Equals(domainEvent.Type, TokenConsumptionType.Tier))
        {
            backgroundJobService.EnqueueTierTokenDeduction(domainEvent.Username, domainEvent.ServiceType.Id);
            return;
        }

        if (Equals(domainEvent.Type, TokenConsumptionType.Overage) && domainEvent.TokenCost.HasValue)
        {
            backgroundJobService.EnqueueOverageTokenDeduction(domainEvent.Username, domainEvent.ServiceType.Id, domainEvent.TokenCost.Value);
        }
    }
}
