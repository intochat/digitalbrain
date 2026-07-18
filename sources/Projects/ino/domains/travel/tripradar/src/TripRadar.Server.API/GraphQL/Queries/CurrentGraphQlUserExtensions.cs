using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using HotChocolate;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.API.GraphQL.Queries;

internal static class CurrentGraphQlUserExtensions
{
    [return: NotNull]
    public static string GetRequiredUsername(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.GetUsername() ?? throw new GraphQLException(ErrorBuilder.New().SetCode("UNAUTHORIZED").SetMessage("User identity was not found in token.").Build());
    }
}
