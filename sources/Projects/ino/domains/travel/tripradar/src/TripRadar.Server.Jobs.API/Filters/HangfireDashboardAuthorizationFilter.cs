using Hangfire.Dashboard;
using TripRadar.Server.Comms.Core.Helpers;
using System.Net;

namespace TripRadar.Server.Jobs.API.Filters;

public class HangfireDashboardAuthorizationFilter(IConfiguration configuration) : IDashboardAuthorizationFilter
{
    private const string DashboardKeyHeaderName = "X-Hangfire-Auth";

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
            return false;

        var configuredKey = configuration.GetValue<string>("HANGFIRE_DASHBOARD_KEY")
                            ?? configuration.GetValue<string>("Hangfire:Dashboard:ApiKey");
        if (!string.IsNullOrWhiteSpace(configuredKey) &&
            httpContext.Request.Headers.TryGetValue(DashboardKeyHeaderName, out var providedKey) &&
            ComparerHelper.Compare(configuredKey, providedKey.ToString()))
        {
            return true;
        }

        return IsLocalRequest(httpContext);
    }

    private static bool IsLocalRequest(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
            return false;

        if (IPAddress.IsLoopback(remoteIp))
            return true;

        var localIp = httpContext.Connection.LocalIpAddress;
        return localIp is not null && remoteIp.Equals(localIp);
    }
}
