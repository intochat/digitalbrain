#:project kernel/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj
#:project sdk/DigitalBrain.SDK/DigitalBrain.SDK.csproj
#:property TargetFramework=net11.0-windows
#:property UseWindowsForms=true
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:property PublishTrimmed=false

using DigitalBrain.Hosting;
using DigitalBrain.Hosting.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

CleanLingeringProcesses();

if (TrayHostingExtensions.TryForwardUrlAndExit(args)) return;

if (args.Contains("--accept-license"))
    Environment.SetEnvironmentVariable("DIGITALBRAIN_ACCEPT_LICENSE", "true");

SetIfMissing("ASPNETCORE_URLS",                    "http://localhost:18888");
SetIfMissing("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:18889");
SetIfMissing("ASPIRE_ALLOW_UNSECURED_TRANSPORT",   "true");

WireDcpToolPaths();

var inoPath = Path.Combine(AppContext.BaseDirectory, "digitalbrain.ino");
if (!File.Exists(inoPath))
    inoPath = Path.Combine(Directory.GetCurrentDirectory(), "digitalbrain.ino");

Console.WriteLine("digitalbrain v5  |  genesis: "
    + (File.Exists(inoPath) ? inoPath : "<default composition>"));

var builder = Aspire.Hosting.DistributedApplication.CreateBuilder(args);

builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(options =>
{
    options.StartupTimeout = TimeSpan.FromMinutes(5);
});

builder.AddDigitalBrain();
builder.AddTrayDaemonIfRequested(args);

foreach (var descriptor in builder.Services)
{
    if (descriptor.ServiceType.Name.Contains("SystemHookEmitter") || 
        descriptor.ImplementationType?.Name.Contains("SystemHookEmitter") == true ||
        descriptor.ServiceType.Name.Contains("SystemHook") ||
        descriptor.ImplementationType?.Name.Contains("SystemHook") == true)
    {
        Console.WriteLine($"[DIAGNOSTIC] Found descriptor: ServiceType={descriptor.ServiceType}, ImplementationType={descriptor.ImplementationType}, Lifetime={descriptor.Lifetime}");
    }
}

await builder.Build().RunAsync();

static void SetIfMissing(string key, string value)
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        Environment.SetEnvironmentVariable(key, value);
}

static void WireDcpToolPaths()
{
    var nuget = ResolveNugetRoot();
    if (nuget is null) return;

    var orchTools = Path.Combine(nuget, "aspire.hosting.orchestration.win-x64", "13.4.0-preview.1.26276.1", "tools");
    var dashTools = Path.Combine(nuget, "aspire.dashboard.sdk.win-x64",         "13.4.0-preview.1.26276.1", "tools");

    SetIfFileExists("DcpPublisher__CliPath",       Path.Combine(orchTools, "dcp.exe"));
    SetIfDirExists ("DcpPublisher__ExtensionPaths", Path.Combine(orchTools, "ext") + Path.DirectorySeparatorChar);
    SetIfFileExists("DcpPublisher__DashboardPath", Path.Combine(dashTools, "Aspire.Dashboard.exe"));
}

static void SetIfFileExists(string key, string path)
{
    if (File.Exists(path)) Environment.SetEnvironmentVariable(key, path);
}

static void SetIfDirExists(string key, string path)
{
    if (Directory.Exists(path.TrimEnd(Path.DirectorySeparatorChar)))
        Environment.SetEnvironmentVariable(key, path);
}

static string? ResolveNugetRoot()
{
    if (Directory.Exists(@"E:\nuget")) return @"E:\nuget";
    var def = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nuget", "packages");
    return Directory.Exists(def) ? def : null;
}

static void CleanLingeringProcesses()
{
    try
    {
        foreach (var processName in new[] { "dcp", "Aspire.Dashboard" })
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Safe cleanup fallback
                }
            }
        }
    }
    catch
    {
        // General safety fallback
    }
}

