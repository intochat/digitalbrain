using FluentValidation;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.Common.Providers;
using TripRadar.Server.Application.UseCases.Common.Validators;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Events.Commands.CreateScheduledEventQuery;

public class CreateScheduledEventQueryCommandValidator : BaseScheduledQueryValidator<CreateScheduledEventQueryCommand>
{
    public CreateScheduledEventQueryCommandValidator(IScheduledExecutionValidityService scheduledExecutionValidityService)
    {
        var columnHierarchyProvider = new EventColumnHierarchyProvider();

        RuleFor(x => x.SearchQuery)
            .NotEmpty()
            .WithMessage("Search query is required");

        RuleFor(x => x.SelectedColumns)
            .Must(columns => columns != null && columns.All(col => columnHierarchyProvider.IsValidColumn(col.Name)))
            .WithMessage("One or more invalid selected columns specified")
            .Must(columns =>
                columns != null && columns.All(col => columnHierarchyProvider.GetRootColumn(col.Name) != null))
            .WithMessage("Invalid column hierarchy specified");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            code => !string.IsNullOrEmpty(code) && code.Length == 2,
            "Country code must be 2 characters",
            "gl");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            code => !string.IsNullOrEmpty(code) && code.Length == 2,
            "Language code must be 2 characters",
            "hl");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            uule => !string.IsNullOrEmpty(uule),
            "UULE parameter must not be empty when provided",
            "uule");

        AddJsonParamRule<string[]>(
            x => x.AdditionalParametersJson,
            htichips => htichips.All(h => h.StartsWith("date:") || h.StartsWith("event_type:")),
            "Invalid htichip format. Must start with 'date:' or 'event_type:'",
            "htichips");

        RuleFor(x => x)
            .Custom((command, context) =>
            {
                var startDate = scheduledExecutionValidityService.ExtractEventStartDate(command.AdditionalParametersJson);
                var endDate = scheduledExecutionValidityService.ExtractEventEndDate(command.AdditionalParametersJson);

                if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
                {
                    context.AddFailure(nameof(CreateScheduledEventQueryCommand.AdditionalParametersJson), "Event end date must be on or after the start date.");
                }

                if (startDate.HasValue && command.NextExecutionTime.HasValue && !scheduledExecutionValidityService.IsExecutableAtNextRun(
                        ScheduledExecutionSearchType.Events,
                        command.NextExecutionTime.Value,
                        startDate))
                {
                    context.AddFailure(nameof(CreateScheduledEventQueryCommand.NextExecutionTime), "Next execution time must be on or before event start date.");
                }
            });
    }
}
