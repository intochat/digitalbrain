using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Product.Salesforce;

internal static class SalesforceMutationFingerprint
{
    internal static string Compute(string mutationId, string accountId, string description)
    {
        var canonical = new StringBuilder();
        Append(canonical, mutationId);
        Append(canonical, accountId);
        Append(canonical, description);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append(StringBuilder canonical, string value)
    {
        canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        canonical.Append(':');
        canonical.Append(value);
        canonical.Append('|');
    }
}
