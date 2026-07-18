using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Extensions;
using TripRadar.MiniApp.Client.Infrastructure.Services.Localization;
using TripRadar.MiniApp.Client.Infrastructure.Services.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<TripRadar.MiniApp.Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLocalization();

builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<IAuthTokenProvider>(sp => sp.GetRequiredService<AuthState>());
builder.Services.AddMiniAppClientInfrastructure(new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddScoped<MiniAppConfigService>();
builder.Services.AddScoped<FlightSearchState>();
builder.Services.AddScoped<HotelSearchState>();
builder.Services.AddScoped<TrackingState>();
builder.Services.AddScoped<TopBarState>();
builder.Services.AddScoped<UserPreferencesState>();
builder.Services.AddScoped<FlightPrefetchService>();
builder.Services.AddScoped<BundlePrefetchService>();
builder.Services.AddScoped<CountryNameLocalizer>();
builder.Services.AddSingleton<FlightTranslationProvider>();
builder.Services.AddScoped<CityNameLocalizer>();
builder.Services.AddScoped<AirportNameLocalizer>();

var host = builder.Build();

var js = host.Services.GetRequiredService<IJSRuntime>();
var storedCulture = await js.InvokeAsync<string?>("sessionStore.get", "app_culture");
if (!string.IsNullOrEmpty(storedCulture))
{
    var culture = new CultureInfo(storedCulture);
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}

await host.RunAsync();