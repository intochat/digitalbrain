using Microsoft.AspNetCore.Authorization;
using TripRadar.Server.API.Controllers;

namespace TripRadar.Server.Tests.Users;

public class InternalControllerTests
{
    [Fact]
    public void InternalController_IsInternalAndDoesNotAllowAnonymous()
    {
        var controllerType = typeof(InternalController);

        controllerType.GetCustomAttributes(inherit: true)
            .Should().NotContain(attribute => attribute is AllowAnonymousAttribute);
        controllerType.GetCustomAttributes(inherit: true)
            .Should().Contain(attribute => IsInternalAttribute(attribute.GetType()));
    }

    private static bool IsInternalAttribute(Type attributeType)
    {
        return string.Equals(attributeType.Namespace, "TripRadar.Server.Comms.Core.Attributes", StringComparison.Ordinal)
               && (string.Equals(attributeType.Name, "Internal", StringComparison.Ordinal)
                   || string.Equals(attributeType.Name, "InternalAttribute", StringComparison.Ordinal));
    }
}