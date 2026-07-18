using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Events.Queries.GetEvents;

public class GetEventsQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetEventsQuery>
{
    public Task<Error?> ValidateAsync(GetEventsQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateLocationAsync(
            request.GetEventRequestDto.GeographicLocation?.Location,
            cancellationToken);
    }
}
