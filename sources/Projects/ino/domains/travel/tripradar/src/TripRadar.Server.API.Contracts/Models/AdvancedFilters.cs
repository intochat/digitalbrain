using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Models;

public class AdvancedFilters : IValidatableObject
{
    public StopsType? Stops { get; set; }

    public string? ExcludeAirlines { get; set; }

    public string? IncludeAirlines { get; set; }

    public int? Bags { get; set; }

    public int? MaxPrice { get; set; }

    public string? OutboundTimes { get; set; }

    public string? ReturnTimes { get; set; }

    public int? Emissions { get; set; }

    public string? LayoverDuration { get; set; }

    public string? ExcludeConns { get; set; }

    public int? MaxDuration { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(ExcludeAirlines) && !string.IsNullOrWhiteSpace(IncludeAirlines))
        {
            yield return new ValidationResult("ExcludeAirlines and IncludeAirlines cannot both be set.", [nameof(ExcludeAirlines), nameof(IncludeAirlines)]);
        }

        if (!string.IsNullOrWhiteSpace(OutboundTimes) && !ValidateTimeRanges(OutboundTimes))
        {
            yield return new ValidationResult("OutboundTimes format is invalid. Expected format: 'hour1,hour2' or 'hour1,hour2,hour3,hour4' with hours 0-23.");
        }

        if (!string.IsNullOrWhiteSpace(ReturnTimes) && !ValidateTimeRanges(ReturnTimes))
        {
            yield return new ValidationResult("ReturnTimes format is invalid. Expected format: 'hour1,hour2' or 'hour1,hour2,hour3,hour4' with hours 0-23.");
        }

        if (!string.IsNullOrWhiteSpace(LayoverDuration) && !Regex.IsMatch(LayoverDuration, @"^\d+,\d+$"))
        {
            yield return new ValidationResult("LayoverDuration must be in 'min,max' format.");
        }

        if (!string.IsNullOrWhiteSpace(ExcludeConns))
        {
            foreach (var code in ExcludeConns.Split(",", StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedCode = code.Trim();
                if (!Regex.IsMatch(trimmedCode, @"^[A-Z]{3}$"))
                {
                    yield return new ValidationResult($"Invalid airport code in ExcludeConns: '{trimmedCode}'. Must be exactly 3 uppercase letters.");
                }
            }
        }

        if (Emissions.HasValue && Emissions != 1)
        {
            yield return new ValidationResult("Emissions must be either null or 1.");
        }
    }

    private static bool ValidateTimeRanges(string input)
    {
        var parts = input.Split(",", StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 && parts.Length != 4)
            return false;

        return parts.All(p => int.TryParse(p, out var hour) && hour is >= 0 and <= 23);
    }
}
