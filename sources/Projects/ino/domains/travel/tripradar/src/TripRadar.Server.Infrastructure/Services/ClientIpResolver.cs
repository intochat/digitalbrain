using Microsoft.AspNetCore.Http;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Services;

public class ClientIpResolver(IHttpContextAccessor httpContextAccessor) : IClientIpResolver
{
    public string? GetClientIpAddress() => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
