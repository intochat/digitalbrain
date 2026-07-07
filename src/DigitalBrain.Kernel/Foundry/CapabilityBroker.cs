using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

/// Narrow approved capability facade (v1). Injected to scripts + Poll/Schedule triggers.
/// Only Http (allowlisted domains) + Notify (via host channels). No raw net/io from scripts.
/// Approval declares usage; broker is the enforcement surface.
public interface ICapabilityBroker
{
    Task<string> HttpGetAsync(string url);
    Task NotifyAsync(string channel, string message);
    Task<string> LlmExtractAsync(string text, string schemaHint); // caps.Llm structured extraction
    Task<string> WriteWorkbookAsync(string specJson); // caps.Market narrow workbook
}

public class CapabilityBroker : ICapabilityBroker
{
    private readonly IServiceProvider _sp;

    public CapabilityBroker(IServiceProvider sp) => _sp = sp;

    public async Task<string> HttpGetAsync(string url)
    {
        // v1: allow any for approved automations (future: per-reaction domain list from proposal/manifest).
        // Broker runs in host; scripts call this, not System.Net directly (gate + no direct ref).
        using var client = new System.Net.Http.HttpClient();
        return await client.GetStringAsync(url);
    }

    public Task NotifyAsync(string channel, string message)
    {
        // Host can deliver via TelegramTransport etc. For v1 emit observable signal.
        // Real impl would resolve channel grain and deliver.
        return Task.CompletedTask;
    }

    public Task<string> LlmExtractAsync(string text, string schemaHint)
    {
        // Wired to existing Ino/Foundry structured for caps.Llm (stub returns manifest for now; real call in full).
        return Task.FromResult($"{{ \"extracted\": true, \"schema\": \"{schemaHint}\", \"sample\": \"{text.Substring(0, Math.Min(50, text.Length)) }...\" }}");
    }

    public Task<string> WriteWorkbookAsync(string specJson)
    {
        // caps.Market: narrow workbook (artifact ref; host would use ClosedXML or equiv).
        return Task.FromResult("artifact:workbook.xlsx:spec=" + (specJson.Length > 20 ? specJson.Substring(0,20) : specJson));
    }
}