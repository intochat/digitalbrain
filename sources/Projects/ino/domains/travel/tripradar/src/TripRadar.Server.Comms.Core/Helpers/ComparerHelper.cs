using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace TripRadar.Server.Comms.Core.Helpers;

public static class ComparerHelper
{
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool Compare(string expected, string? actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);

        var maxLength = Math.Max(expectedBytes.Length, actualBytes.Length);
        var expectedPadded = maxLength <= 256 ? stackalloc byte[maxLength] : new byte[maxLength];
        var actualPadded = maxLength <= 256 ? stackalloc byte[maxLength] : new byte[maxLength];

        expectedBytes.CopyTo(expectedPadded);
        actualBytes.CopyTo(actualPadded);

        return CryptographicOperations.FixedTimeEquals(expectedPadded, actualPadded) && expectedBytes.Length == actualBytes.Length;
    }
}
