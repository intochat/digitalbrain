using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Sdk.Experiences;

using Orleans;

// T2 connectors pattern: modeled exactly on GmailConnectorNeuron + GoogleAuthConnectorNeuron.
// [GrainType("telegram-bot")] + launcher map entry enables activation via RunExperience / install / direct grain.
// Stub for now: grant handling + DI seam for future http (api.telegram.org/bot{token}/sendMessage etc).
// Emits Tg* synapses (to be added in later delta with ser) or demo surfaces via rules.
// All expressed as neurons/synapses; no ITelegramAgent anti-patterns.
// Declarative .ino/.yaml (new) provide the UI contract; this is the L1 behavior owner for real calls + vault.
[GrainType("telegram-bot")]
public sealed class TelegramConnectorNeuron : Neuron,
    IHandle<BeginTelegramConnect>,
    IHandle<GrantDecision>,
    IHandle<GrantRevoked>
{
    private readonly IServiceProvider _services;
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

    public TelegramConnectorNeuron(IServiceProvider services)
    {
        _services = services;
    }

    public async Task HandleAsync(BeginTelegramConnect request, CancellationToken cancellationToken)
    {
        // Real TG bot API wiring (per plan): uses DI IHttpClientFactory seam (like GmailConnector) + grant for token.
        // On Begin (from declarative button), POST sendMessage with web_app markup pointing to the Flutter TG WebApp url (reuses existing Mail floating + IAsync stream).
        // Tolerant for CI/demo (no real token/chat_id): always emit intent + telemetry; real http only if granted.
        var webAppUrl = "http://localhost:8080/flutter?tg=1&exp=gmail-senders-chart&mode=floating";
        var payload = new
        {
            chat_id = "demo_chat", // in real from user context or state
            text = "Open Mail as TG WebApp (draggable floating + streamed senders).",
            reply_markup = new
            {
                inline_keyboard = new[]
                {
                    new[]
                    {
                        new { text = "Open Mail", web_app = new { url = webAppUrl } }
                    }
                }
            }
        };

        string? apiStatus = null;
        if (_allowed.Contains("TelegramBotToken"))
        {
            try
            {
                var factory = _services.GetService<IHttpClientFactory>();
                using var http = factory?.CreateClient() ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var token = "demo_bot_token"; // real: from CredentialVaultNeuron (brain-scoped, granted)
                var api = $"https://api.telegram.org/bot{token}/sendMessage";
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync(api, content, cancellationToken);
                apiStatus = resp.StatusCode.ToString();
            }
            catch (Exception ex)
            {
                apiStatus = "error:" + ex.Message;
            }
        }

        await Emit(new NeuronTelemetry(Self, "TelegramBotApiCall", new Dictionary<string, string>
        {
            ["web_app_url"] = webAppUrl,
            ["status"] = apiStatus ?? "demo_no_grant",
            ["payload_size"] = JsonSerializer.Serialize(payload).Length.ToString()
        }));

        // The declarative surface (from rules + grain Run) already provides the clickable link for immediate testing in flutter/TG WebApp.
        // This http call demonstrates the "bot sends the WebApp button to TG chat" under the hood.
    }

    public Task HandleAsync(GrantDecision decision, CancellationToken cancellationToken)
    {
        if (string.Equals(decision.BundleId, "telegram-bot", StringComparison.OrdinalIgnoreCase) && decision.Allowed)
        {
            _allowed.Add("TelegramBotToken");
        }
        return Task.CompletedTask;
    }

    public Task HandleAsync(GrantRevoked revoked, CancellationToken cancellationToken)
    {
        if (string.Equals(revoked.BundleId, "telegram-bot", StringComparison.OrdinalIgnoreCase))
        {
            _allowed.Clear();
        }
        return Task.CompletedTask;
    }
}
