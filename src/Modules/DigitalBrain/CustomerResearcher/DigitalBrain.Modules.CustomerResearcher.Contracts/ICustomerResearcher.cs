using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using Orleans.Concurrency;

namespace DigitalBrain.CustomerResearcher;

[Alias("apps.customer-researcher"), Orleans.Metadata.DefaultGrainType("apps.customer-researcher")]
public interface ICustomerResearcher : INeuron, IUiEventHandler
{
    Task Activate();
    Task<ResearchWindow> OpenWindow();
    [OneWay] Task Research(string query);
    Task Stop();
}

public static class CustomerResearcherKeys
{
    public static string For(string workspace) => workspace + "/applications/customer-researcher";
}

[GenerateSerializer, Alias("customer-researcher.window")]
public sealed record ResearchWindow([property: Id(0)] string Id, [property: Id(1)] string Title, [property: Id(2)] UiChildRef Surface);
