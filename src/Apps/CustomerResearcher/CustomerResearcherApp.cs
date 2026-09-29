using DigitalBrain.AI;
using DigitalBrain.Flutter;
using DigitalBrain.Microsoft.Playwright;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Apps.CustomerResearcher;

public sealed class CustomerResearcherApp : IApplication
{
    public static string Key(string workspace) => workspace + "/applications/customer-researcher";
    public void Configure(IAppBuilder app) => app
        .ConfigureServices(services => {
            services.TryAddSingleton<ICompanyResearchAgent, CompanyResearchAgent>();
            services.TryAddSingleton<ICompanyResearchStore, CompanyResearchStore>();
        })
        .RequireModule<AppsModule>().RequireModule<AIModule>().RequireModule<FlutterModule>()
        .RequireModule<PlaywrightModule>().RequireModule<PostgresModule>()
        .Ui(ui => ui.Surface("Customer Researcher",
            ui.Layout("main", "column", [64, 0],
                ui.Layout("toolbar", "row", [0, 110, 80, 280],
                    ui.TextField("company", "Company name, location or website", submitButton: "research").OnEvent<ICustomerResearcher>(),
                    ui.Button("research", "Research", "research").OnEvent<ICustomerResearcher>(),
                    ui.Button("stop", "Stop", "stop").OnEvent<ICustomerResearcher>(),
                    ui.Text("status", "Waiting for browser…")),
                ui.WebBrowser("browser").OnEvent<ICustomerResearcher>())))
        .OnStart(start => start.Grains.GetGrain<ICustomerResearcher>(start.Key).Activate());
}
