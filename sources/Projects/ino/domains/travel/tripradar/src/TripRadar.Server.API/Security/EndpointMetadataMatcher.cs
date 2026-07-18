using Microsoft.AspNetCore.Authorization;

namespace TripRadar.Server.API.Security;

internal static class EndpointMetadataMatcher
{
    public static bool AllowsAnonymous(IEnumerable<object> endpointMetadata) => endpointMetadata.Any(item => item is AllowAnonymousAttribute);

    public static bool IsInternal(IEnumerable<object> endpointMetadata) =>
        endpointMetadata.Any(static item =>
        {
            var itemType = item.GetType();
            return string.Equals(itemType.Namespace, "TripRadar.Server.Comms.Core.Attributes", StringComparison.Ordinal) &&
                   (string.Equals(itemType.Name, "Internal", StringComparison.Ordinal) ||
                    string.Equals(itemType.Name, "InternalAttribute", StringComparison.Ordinal));
        });
}
