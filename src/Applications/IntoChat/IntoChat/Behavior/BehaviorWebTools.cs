using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.AI.Web;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace IntoChat;

[McpServerToolType]
public sealed class BehaviorWebTools(PlaywrightWebAgent web)
{
    [McpServerTool(Name = "lookup_company"), Description("Research a company's public postal address and contact email in an isolated Playwright browser. Supply companyName and optionally its official website. Returns structured JSON with sources, evidence, and nulls for unverified fields. Can also be used in a program as agent mode:web operation:company.")]
    public Task<string> LookupCompany(string companyName, string? website = null, CancellationToken cancellationToken = default)
        => Guard(() => web.LookupCompanyAsync(companyName, website, cancellationToken));

    [McpServerTool(Name = "browse_web"), Description("Use a Playwright web agent to read public pages, follow links and extract structured data for a task. Optional startUrl. Returns JSON and actual visited source URLs. No logins or form submissions. Behaviors use agent mode:web with prompt and optional startUrl.")]
    public Task<string> Browse(string task, string? startUrl = null, CancellationToken cancellationToken = default)
        => Guard(() => web.ResearchAsync(task, startUrl, cancellationToken));

    private static async Task<string> Guard(Func<Task<JsonElement>> operation)
    {
        try { return (await operation()).GetRawText(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or TimeoutException or JsonException or Microsoft.Playwright.PlaywrightException)
        {
            throw new McpException(error.Message);
        }
    }
}
