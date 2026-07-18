using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.Comms.Core.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class IsPhoneNumberValidAttribute() : ValidationAttribute("Phone number must be in E.164 format (e.g., +12345678901).")
{
    public override bool IsValid(object? value)
    {
        if (value is not string phoneNumber)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return true;
        }

        if (!phoneNumber.StartsWith('+'))
        {
            return false;
        }

        for (var i = 1; i < phoneNumber.Length; i++)
        {
            if (!char.IsDigit(phoneNumber[i]))
            {
                return false;
            }
        }

        if (phoneNumber.Length < 2 || !char.IsDigit(phoneNumber[1]) || phoneNumber[1] == '0')
        {
            return false;
        }

        return phoneNumber.Length is >= 3 and <= 16;
    }
}
