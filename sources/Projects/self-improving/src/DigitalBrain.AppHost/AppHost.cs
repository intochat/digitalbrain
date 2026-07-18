using DigitalBrain.Hosting.Microsoft.Aspire;
using System.Security.Cryptography;

// Generate (or honor pre-set) the real Aspire browser token and build the full dashboard URL including ?t=TOKEN.
// This ensures DIGITALBRAIN_DASHBOARD_URL / ASPIRE_DASHBOARD_URL passed to kernels, flutter, IAspire.GetDashboardUrlAsync, WorldConnectionInfo etc. contain the actual token so the whole URL can be copied and opened directly.
// Aspire honors ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN and will print a matching "Login to the dashboard at .../login?t=..." line.
// Placed before the first read of ASPIRE_DASHBOARD_URL and before CreateBuilder so the token is in effect for the dashboard this AppHost starts (root or child via launcher).
// Support aspire start (default picks .csproj AppHost) or from src/DigitalBrain.AppHost dir: chdir to root so brain.ino + os/ seeds resolve for AddDefaultDigitalBrainTopology / manifest.
if (!File.Exists("brain.ino") && File.Exists("../../brain.ino"))
{
    Directory.SetCurrentDirectory("../../");
}
else if (!File.Exists("brain.ino") && File.Exists("final/brain.ino"))
{
    Directory.SetCurrentDirectory("final");
}

var rawDashboardBase = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_URL") ?? "http://localhost:18888";
var dashboardBrowserToken = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN");
if (string.IsNullOrWhiteSpace(dashboardBrowserToken))
{
    dashboardBrowserToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN", dashboardBrowserToken);
}
// Always normalize to clean origin (scheme+host+port) then append /login?t= so we never duplicate paths or tokens even if outer env had a partial URL.
var dashboardBaseUri = new Uri(rawDashboardBase.Contains("://") ? rawDashboardBase : "http://" + rawDashboardBase);
var dashboardOrigin = dashboardBaseUri.GetLeftPart(UriPartial.Authority);
var fullDashboardUrlWithToken = dashboardOrigin + "/login?t=" + dashboardBrowserToken;
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_URL", fullDashboardUrlWithToken);
Environment.SetEnvironmentVariable("DIGITALBRAIN_DASHBOARD_URL", fullDashboardUrlWithToken);

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDefaultDigitalBrainTopology();

builder.Build().Run();
