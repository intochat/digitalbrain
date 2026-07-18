using System.Reflection;

namespace TripRadar.Server.Application.Contracts.Requests;

public static class RequestPrivacyMode
{
    private static readonly string[] PrivacyPropertyNames = ["ZeroTrace", "NoTraceMode"];

    public static bool IsAnonymous(object? requestPayload)
    {
        if (requestPayload is null)
        {
            return false;
        }

        var payloadType = requestPayload.GetType();
        foreach (var propertyName in PrivacyPropertyNames)
        {
            var property = payloadType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanRead)
            {
                continue;
            }

            if (property.GetValue(requestPayload) is true)
            {
                return true;
            }
        }

        return false;
    }
}
