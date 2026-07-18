using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Events.Queries.GetEvents;

public class GetEventsQueryValidator : AbstractValidator<GetEventsQuery>
{
    public GetEventsQueryValidator()
    {
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.");

        RuleFor(x => x.GetEventRequestDto.SearchQuery.Q)
            .NotEmpty()
            .WithMessage("Search query. is required.");
    }
}
