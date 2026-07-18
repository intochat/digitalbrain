using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.Comms.Core.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class IsPasswordValidAttribute() : ValidationAttribute("Password must contain at least one lowercase letter, one uppercase letter, one digit, and one special character.")
{
    public override bool IsValid(object? value)
    {
        if (value is not string password)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return false;
        }

        var hasLowercase = false;
        var hasUppercase = false;
        var hasDigit = false;
        var hasSpecial = false;

        foreach (var c in password)
        {
            if (char.IsLower(c))
            {
                hasLowercase = true;
            }
            else if (char.IsUpper(c))
            {
                hasUppercase = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else if (!char.IsLetterOrDigit(c))
            {
                hasSpecial = true;
            }
        }

        return hasLowercase && hasUppercase && hasDigit && hasSpecial;
    }
}
