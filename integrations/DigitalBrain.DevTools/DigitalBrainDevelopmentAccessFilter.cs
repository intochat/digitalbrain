using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.DevTools;

internal sealed record DigitalBrainDevelopmentAccessMetadata(
    bool LoopbackOnly,
    bool TokenRequired);

internal sealed class DigitalBrainDevelopmentAccessFilter(
    bool allowRemoteAccess,
    string? authToken) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        var hasForwardedAddress =
            context.HttpContext.Request.Headers.ContainsKey("Forwarded") ||
            context.HttpContext.Request.Headers.ContainsKey("X-Forwarded-For") ||
            context.HttpContext.Request.Headers.ContainsKey("X-Real-IP");
        var isLoopback =
            remoteAddress is not null &&
            IPAddress.IsLoopback(remoteAddress) &&
            !hasForwardedAddress;
        if (!allowRemoteAccess && !isLoopback)
            return ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));

        if (!string.IsNullOrWhiteSpace(authToken) &&
            !HasValidToken(context.HttpContext.Request))
            return ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status401Unauthorized));

        return next(context);
    }

    private bool HasValidToken(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(authToken))
            return false;

        var authorization = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suppliedToken = authorization[bearerPrefix.Length..];
        var expectedBytes = Encoding.UTF8.GetBytes(authToken);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
