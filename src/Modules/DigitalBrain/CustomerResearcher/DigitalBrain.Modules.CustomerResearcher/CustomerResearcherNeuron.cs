using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.CustomerResearcher;

[GenerateSerializer, Alias("customer-researcher.state")]
public sealed record CustomerResearcherState;

[Reentrant, GrainType("apps.customer-researcher")]
internal sealed class CustomerResearcherNeuron(
    [PersistentState("apps.customer-researcher", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CustomerResearcherState> state,
    IServiceProvider services) : Neuron<CustomerResearcherState>(state), ICustomerResearcher
{
    private string Key => this.GetPrimaryKeyString();
    private string Workspace => Key.Split("/applications/")[0];
    private IPlaywright Browser => GrainFactory.GetGrain<IPlaywright>(Key + "/browser");
    private CancellationTokenSource? _run;
    private long _generation;
    public async Task Activate()
    {
        await CustomerResearcherSurface.Compose(GrainFactory, Key);
    }
    private Task Status(string text) => GrainFactory.GetGrain<IText>(UiParts.NameOf(Key, "status")).Set(text);

    public async Task<ResearchWindow> OpenWindow()
    {
        var id = Guid.NewGuid().ToString("N");
        var key = CustomerResearcherSurface.Key(Workspace) + "/" + id;
        await GrainFactory.GetGrain<ICustomerResearcher>(key).Activate();
        var surface = new UiChildRef(UIVocabulary.SurfaceType, UiParts.NameOf(key, "surface"));
        await GrainFactory.GetGrain<IWorkspace>(Workspace).EnsureOpenAsync(id, "Customer Researcher", WindowReference.For(surface), CancellationToken.None);
        return new(id, "Customer Researcher", surface);
    }

    public async Task Stop()
    {
        ++_generation;
        _run?.Cancel();
        await Status("Stopped");
    }

    public async Task Research(string query)
    {
        query = query.Trim();
        if (query.Length is < 1 or > 500) { await Status("Enter a company name (at most 500 characters)."); return; }
        _run?.Cancel();
        using var run = new CancellationTokenSource();
        _run = run;
        var generation = ++_generation;
        // A retry after activation/reconnect updates the same window/query record.
        var researchId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Key + "\n" + query)));
        bool Current() => generation == _generation && !run.IsCancellationRequested;
        try
        {
            await Status("Researching…");
            var browser = await Browser.Read();
            if (!browser.Ready) { throw new BrowserNotConnectedException(); }
            if (!Current()) { return; }
            var result = await services.GetRequiredService<ICompanyResearchAgent>().Research(query, Browser,
                message => Current() ? Status(message) : Task.CompletedTask, run.Token);
            if (!Current()) { return; }
            var currentBrowser = await Browser.Read();
            if (!Current()) { return; }
            if (!currentBrowser.Ready || currentBrowser.SessionId != browser.SessionId) { throw new BrowserNotConnectedException(); }
            if (result.Company is null) { await Status(result.Status); return; }
            await Status("Saving…");
            if (!Current()) { return; }
            await services.GetRequiredService<ICompanyResearchStore>().Save(Workspace, researchId, result.Company, run.Token);
            if (Current()) { await Status("Saved: " + result.Company.CompanyName); }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (BrowserNotConnectedException error) { if (Current()) { await Status(error.Message); } }
        catch (OperationCanceledException) { if (Current()) { await Status("Browser disconnected. Retry when connected."); } }
        catch (Exception error)
        {
            services.GetRequiredService<ILogger<CustomerResearcherNeuron>>().LogWarning(error, "Company research failed");
            if (Current()) { await Status("Research or save failed. Retry to safely update the same record."); }
        }
        finally { if (ReferenceEquals(_run, run)) { _run = null; } }
    }

    public async Task HandleUiEvent(Signal signal)
    {
        switch (signal)
        {
            case ButtonClicked clicked when clicked.Name == Key + "/research":
                var query = (await GrainFactory.GetGrain<ITextField>(Key + "/company").Read()).Value;
                // A one-way call releases the binding immediately; reentrancy admits Stop/disconnect.
                await this.AsReference<ICustomerResearcher>().Research(query);
                break;
            case ButtonClicked clicked when clicked.Name == Key + "/stop": await Stop(); break;
        }
    }

}
