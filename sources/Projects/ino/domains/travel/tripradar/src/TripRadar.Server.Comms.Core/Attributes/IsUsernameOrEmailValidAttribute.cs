using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace TripRadar.Server.Comms.Core.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public partial class IsUsernameOrEmailValidAttribute() : ValidationAttribute("Username or email is not valid.")
{
    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    public override bool IsValid(object? value)
    {
        if (value is not string input || string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return input.Contains('@') ? EmailRegex().IsMatch(input) : input.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string input || string.IsNullOrWhiteSpace(input))
        {
            return new ValidationResult("Username or email is required.");
        }

        if (input.Contains('@'))
        {
            if (!EmailRegex().IsMatch(input))
            {
                return new ValidationResult("Invalid email address format.");
            }
        }
        else
        {
            if (!input.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return new ValidationResult("Username can only contain letters, numbers, and underscores.");
            }
        }

        return ValidationResult.Success;
    }
}
