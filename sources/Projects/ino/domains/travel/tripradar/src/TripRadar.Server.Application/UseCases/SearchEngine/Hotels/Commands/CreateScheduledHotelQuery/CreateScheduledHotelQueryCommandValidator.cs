using FluentValidation;
using TripRadar.Server.Application.Constants.Hotels;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.Common.Providers;
using TripRadar.Server.Application.UseCases.Common.Validators;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Commands.CreateScheduledHotelQuery;

public class CreateScheduledHotelQueryCommandValidator : BaseScheduledQueryValidator<CreateScheduledHotelQueryCommand>
{
    public CreateScheduledHotelQueryCommandValidator(IScheduledExecutionValidityService scheduledExecutionValidityService)
    {
        var columnHierarchyProvider = new HotelColumnHierarchyProvider();

        RuleFor(x => x.Location)
            .NotEmpty()
            .WithMessage("Location is required");

        RuleFor(x => x.CheckInDate)
            .NotEmpty()
            .WithMessage("Check-in date is required")
            .Must(date => date.Date >= DateTime.UtcNow.Date)
            .WithMessage("Check-in date must not be in the past");

        RuleFor(x => x.CheckOutDate)
            .NotEmpty()
            .WithMessage("Check-out date is required")
            .Must((command, checkOutDate) => checkOutDate > command.CheckInDate)
            .WithMessage("Check-out date must be after check-in date");

        RuleFor(x => x)
            .Must(command => !command.NextExecutionTime.HasValue || scheduledExecutionValidityService.IsExecutableAtNextRun(
                ScheduledExecutionSearchType.Hotels,
                command.NextExecutionTime.Value,
                command.CheckInDate))
            .WithMessage("Next execution time must be on or before check-in date.");

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
            code => !string.IsNullOrEmpty(code) && code.Length == 3,
            "Currency code must be 3 characters",
            "currency");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            count => count is >= 1 and <= 20,
            "Adults must be between 1 and 20",
            "adults");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            count => count is >= 0 and <= 10,
            "Children must be between 0 and 10",
            "children");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            ages => ages.Split(',').All(age => int.TryParse(age, out var parsedAge) && parsedAge is >= 0 and <= 17),
            "Children ages must be between 0 and 17",
            "children_ages");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            pt => HotelQueryConstants.HotelPropertyTypes.Concat(HotelQueryConstants.VacationRentalPropertyTypes)
                .Contains(pt),
            "Invalid property type",
            "property_type");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            amenities => amenities.Split(',').All(a =>
                HotelQueryConstants.CommonAmenities.Contains(a) ||
                HotelQueryConstants.VacationRentalAmenities.Contains(a)),
            "Invalid amenities",
            "amenities");

        AddJsonParamRule<decimal?>(
            x => x.AdditionalParametersJson,
            price => price is null or > 0,
            "Max price must be positive",
            "max_price");

        AddJsonParamRule<decimal?>(
            x => x.AdditionalParametersJson,
            rating => rating is null or >= 0 and <= 5,
            "Rating must be between 0 and 5",
            "min_rating");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            sb => HotelQueryConstants.SortBy.Contains(sb),
            "Invalid sort by option",
            "sort_by");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            rt => HotelQueryConstants.RoomTypes.Contains(rt),
            "Invalid room type",
            "room_type");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            hc => HotelQueryConstants.HotelClasses.Contains(hc),
            "Invalid hotel class",
            "hotel_class");

        AddJsonParamRule<bool?>(
            x => x.AdditionalParametersJson,
            _ => true,
            "Free cancellation must be a boolean value",
            "free_cancellation");

        AddJsonParamRule<bool?>(
            x => x.AdditionalParametersJson,
            _ => true,
            "Special offers must be a boolean value",
            "special_offers");

        AddJsonParamRule<bool?>(
            x => x.AdditionalParametersJson,
            _ => true,
            "Eco certified must be a boolean value",
            "eco_certified");
    }
}
