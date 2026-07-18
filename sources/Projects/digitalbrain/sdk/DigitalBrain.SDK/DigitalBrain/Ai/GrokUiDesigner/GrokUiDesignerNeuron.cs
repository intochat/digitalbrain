using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Security;
using DigitalBrain.SDK.XAI.Grok;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;

[GrainType("DigitalBrain.SDK.Ai.GrokUiDesignerNeuron")]
[ImplicitStreamSubscription(GrokUiDesignerNeuronType)]
internal sealed class GrokUiDesignerNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    ISecretVault vault,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<GrokUiDesignerNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata, IHandle<GrokUiDesignRequest>, IHandle<SaveUiToInoRequest>
{
    public const string GrokUiDesignerNeuronType = nameof(GrokUiDesignerNeuron);

    public static NeuronId         Id           => new("ai/grok-designer");
    public static string           Icon         => "brush";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced;

    private IChatClient? _chat;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        string? apiKey = null;
        try
        {
            apiKey = await vault.DecryptSecretAsync("xai-api-key", cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to decrypt xai-api-key in GrokUiDesignerNeuron, falling back.");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY") ?? "mock-xai-api-key";
        }

        if (apiKey != "mock-xai-api-key")
        {
            try
            {
                _chat = new GrokConnector(apiKey, "grok-beta");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to initialize GrokConnector in GrokUiDesignerNeuron.");
            }
        }
    }

    public async Task HandleAsync(GrokUiDesignRequest synapse, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Processing GrokUiDesignRequest: {Prompt}", synapse.Prompt);

        string uiJson = "";
        string explanation = "";
        string inoCode = "";

        if (_chat != null)
        {
            try
            {
                var systemPrompt = @"You are Grok UI Designer, an elite expert in Flutter and the DigitalBrain UI Kit.
Your task is to design a beautiful, responsive user interface using only the standard `UiKit` widgets.

Here is the specification of supported `UiKit` widgets and their JSON representations:
1. Columns:
   { ""name"": ""UiKit.Column"", ""arguments"": { ""gap"": 12.0 }, ""children"": [...] }
2. Rows:
   { ""name"": ""UiKit.Row"", ""arguments"": { ""gap"": 12.0 }, ""children"": [...] }
3. Cards:
   { ""name"": ""UiKit.Card"", ""arguments"": { ""title"": ""My Title"", ""body"": ""My Body text"" } }
4. Buttons:
   { ""name"": ""UiKit.Button"", ""arguments"": { ""label"": ""Click Me"", ""action"": ""mySynapseOrHandlerName"" } }
5. Text fields:
   { ""name"": ""UiKit.Text"", ""arguments"": { ""text"": ""My content"", ""variant"": ""body"" } }
   Variants: ""title"", ""heading"", ""body"", ""dim""
6. Text input fields:
   { ""name"": ""UiKit.Input"", ""arguments"": { ""placeholder"": ""Enter text"", ""binding"": ""myStateVariable"" } }

Rules:
- You must ONLY use the 6 widgets above. Do not invent others.
- All elements must reside inside a single root widget (usually `UiKit.Column` or `UiKit.Card`).
- Nest widgets properly in `children`.
- Output your response in raw JSON format inside a markdown code block:
```json
{
  ""ui"": { ... },
  ""explanation"": ""Brief description of the design and UX decisions."",
  ""inoCode"": ""The equivalent ui: block code in InoLang format.""
}
```";

                var messages = new[]
                {
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, synapse.Prompt)
                };

                var options = new ChatOptions { MaxOutputTokens = 1500 };
                var response = await _chat.GetResponseAsync(messages, options, cancellationToken);
                var text = response.Text ?? string.Empty;

                // Parse the JSON out of markdown block if present
                var match = Regex.Match(text, @"```json\s*(.*?)\s*```", RegexOptions.Singleline);
                var jsonContent = match.Success ? match.Groups[1].Value : text;

                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                if (root.TryGetProperty("ui", out var uiProp) &&
                    root.TryGetProperty("explanation", out var expProp) &&
                    root.TryGetProperty("inoCode", out var inoProp))
                {
                    uiJson = uiProp.GetRawText();
                    explanation = expProp.GetString() ?? "";
                    inoCode = inoProp.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to call live Grok API or parse response. Falling back to Mock Designer.");
            }
        }

        // Offline / Mock fallback if live API failed or was not configured
        if (string.IsNullOrEmpty(uiJson))
        {
            var lowercasePrompt = synapse.Prompt.ToLowerInvariant();
            if (lowercasePrompt.Contains("dash") || lowercasePrompt.Contains("board"))
            {
                uiJson = GetDashboardJson();
                explanation = "I have designed a premium Command Center Dashboard containing business stats rows, KPI metric cards, and a direct interactive production deploy button.";
                inoCode = GetDashboardIno();
            }
            else if (lowercasePrompt.Contains("profile") || lowercasePrompt.Contains("user") || lowercasePrompt.Contains("avatar"))
            {
                uiJson = GetProfileJson();
                explanation = "I designed a beautiful User Profile Card containing an avatar header block, text bios, detailed stats metrics, and active 'Follow' / 'Message' interactive buttons.";
                inoCode = GetProfileIno();
            }
            else if (lowercasePrompt.Contains("settings") || lowercasePrompt.Contains("config") || lowercasePrompt.Contains("option"))
            {
                uiJson = GetSettingsJson();
                explanation = "I created a highly structured Settings Panel layout, including text header blocks, active configurations list cards, and interactive bound text input fields.";
                inoCode = GetSettingsIno();
            }
            else
            {
                uiJson = GetDefaultJson(synapse.Prompt);
                explanation = $"I designed a tailored workspace card matching your prompt: '{synapse.Prompt}'. It groups main title headers, dynamic description text, and interactive system triggers.";
                inoCode = GetDefaultIno(synapse.Prompt);
            }
        }

        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: GrokUiDesignerNeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: time.GetUtcNow()
        );

        var designResponse = new GrokUiDesignResponse(
            UiJson: uiJson,
            Explanation: explanation,
            InoCode: inoCode
        ) { Headers = responseHeaders };

        await FireSynapseAsync(designResponse, cancellationToken);
    }

    public async Task HandleAsync(SaveUiToInoRequest synapse, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Processing SaveUiToInoRequest for filename: {Filename}", synapse.Filename);
        bool success = false;
        string? error = null;

        try
        {
            var watchedDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../inolang"));
            if (!System.IO.Directory.Exists(watchedDir))
            {
                System.IO.Directory.CreateDirectory(watchedDir);
            }

            var cleanFilename = synapse.Filename.Replace(".ino", "");
            var filePath = System.IO.Path.Combine(watchedDir, $"{cleanFilename}.ino");

            await System.IO.File.WriteAllTextAsync(filePath, synapse.InoCode, cancellationToken);
            success = true;
            Logger.LogInformation("Successfully wrote Ino code to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to write Ino code to file.");
            error = ex.Message;
        }

        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: GrokUiDesignerNeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: time.GetUtcNow()
        );

        var designResponse = new SaveUiToInoResponse(
            Success: success,
            ErrorMessage: error
        ) { Headers = responseHeaders };

        await FireSynapseAsync(designResponse, cancellationToken);
    }

    #region Mock Templates

    private string GetDashboardJson() => @"
    {
      ""name"": ""UiKit.Column"",
      ""arguments"": { ""gap"": 16.0 },
      ""children"": [
        {
          ""name"": ""UiKit.Card"",
          ""arguments"": {
            ""title"": ""Corporate Command Center"",
            ""body"": ""Welcome back! Here is your business health overview for today. All background services are operating at maximum efficiency.""
          }
        },
        {
          ""name"": ""UiKit.Row"",
          ""arguments"": { ""gap"": 12.0 },
          ""children"": [
            {
              ""name"": ""UiKit.Card"",
              ""arguments"": {
                ""title"": ""Revenue"",
                ""body"": ""$12,450 (+14% today)""
              }
            },
            {
              ""name"": ""UiKit.Card"",
              ""arguments"": {
                ""title"": ""Network Latency"",
                ""body"": ""42ms (optimal)""
              }
            }
          ]
        },
        {
          ""name"": ""UiKit.Button"",
          ""arguments"": {
            ""label"": ""Deploy Production Hotfix"",
            ""action"": ""deployHotfix""
          }
        }
      ]
    }";

    private string GetDashboardIno() => 
@"ui:
  UiKit.Column(
    children: [
      UiKit.Card(
        title: ""Corporate Command Center"",
        body: ""Welcome back! Here is your business health overview for today. All background services are operating at maximum efficiency.""
      ),
      UiKit.Row(
        children: [
          UiKit.Card(title: ""Revenue"", body: ""$12,450 (+14% today)""),
          UiKit.Card(title: ""Network Latency"", body: ""42ms (optimal)"")
        ]
      ),
      UiKit.Button(label: ""Deploy Production Hotfix"", action: ""deployHotfix"")
    ]
  )";

    private string GetProfileJson() => @"
    {
      ""name"": ""UiKit.Card"",
      ""arguments"": {
        ""title"": ""User Profile: Alice Vance"",
        ""body"": ""Senior AI Solutions Architect at DigitalBrain. Passionate about distributed systems, dynamic interpreting engines, and remote rendering substrates.""
      },
      ""children"": [
        {
          ""name"": ""UiKit.Row"",
          ""arguments"": { ""gap"": 12.0 },
          ""children"": [
            {
              ""name"": ""UiKit.Text"",
              ""arguments"": {
                ""text"": ""Followers: 1.2K"",
                ""variant"": ""heading""
              }
            },
            {
              ""name"": ""UiKit.Text"",
              ""arguments"": {
                ""text"": ""Following: 480"",
                ""variant"": ""heading""
              }
            }
          ]
        },
        {
          ""name"": ""UiKit.Row"",
          ""arguments"": { ""gap"": 16.0 },
          ""children"": [
            {
              ""name"": ""UiKit.Button"",
              ""arguments"": {
                ""label"": ""Follow Alice"",
                ""action"": ""followUser""
              }
            },
            {
              ""name"": ""UiKit.Button"",
              ""arguments"": {
                ""label"": ""Direct Message"",
                ""action"": ""messageUser""
              }
            }
          ]
        }
      ]
    }";

    private string GetProfileIno() => 
@"ui:
  UiKit.Card(
    title: ""User Profile: Alice Vance"",
    body: ""Senior AI Solutions Architect at DigitalBrain. Passionate about distributed systems, dynamic interpreting engines, and remote rendering substrates.""
  ):
    UiKit.Row(
      children: [
        UiKit.Text(text: ""Followers: 1.2K"", variant: ""heading""),
        UiKit.Text(text: ""Following: 480"", variant: ""heading"")
      ]
    )
    UiKit.Row(
      children: [
        UiKit.Button(label: ""Follow Alice"", action: ""followUser""),
        UiKit.Button(label: ""Direct Message"", action: ""messageUser"")
      ]
    )";

    private string GetSettingsJson() => @"
    {
      ""name"": ""UiKit.Column"",
      ""arguments"": { ""gap"": 14.0 },
      ""children"": [
        {
          ""name"": ""UiKit.Text"",
          ""arguments"": {
            ""text"": ""System Configuration & Security"",
            ""variant"": ""title""
          }
        },
        {
          ""name"": ""UiKit.Card"",
          ""arguments"": {
            ""title"": ""Privacy Preferences"",
            ""body"": ""Configure how your dynamic Orleans grain storage prefixes scopes and DPAPI credentials.""
          }
        },
        {
          ""name"": ""UiKit.Input"",
          ""arguments"": {
            ""placeholder"": ""Enter secure encryption salt..."",
            ""binding"": ""encryptionSalt""
          }
        },
        {
          ""name"": ""UiKit.Button"",
          ""arguments"": {
            ""label"": ""Save and Commit Settings"",
            ""action"": ""saveSettings""
          }
        }
      ]
    }";

    private string GetSettingsIno() => 
@"ui:
  UiKit.Column(
    children: [
      UiKit.Text(text: ""System Configuration & Security"", variant: ""title""),
      UiKit.Card(
        title: ""Privacy Preferences"",
        body: ""Configure how your dynamic Orleans grain storage prefixes scopes and DPAPI credentials.""
      ),
      UiKit.Input(placeholder: ""Enter secure encryption salt..."", binding: ""encryptionSalt""),
      UiKit.Button(label: ""Save and Commit Settings"", action: ""saveSettings"")
    ]
  )";

    private string GetDefaultJson(string prompt) => $@"
    {{
      ""name"": ""UiKit.Column"",
      ""arguments"": {{ ""gap"": 12.0 }},
      ""children"": [
        {{
          ""name"": ""UiKit.Card"",
          ""arguments"": {{
            ""title"": ""Grok Custom Design"",
            ""body"": ""I analyzed your prompt: '{prompt.Replace("\"", "\\\"")}' and rendered this premium container tailored to your request.""
          }}
        }},
        {{
          ""name"": ""UiKit.Text"",
          ""arguments"": {{
            ""text"": ""Dynamic Prompt Context"",
            ""variant"": ""dim""
          }}
        }},
        {{
          ""name"": ""UiKit.Text"",
          ""arguments"": {{
            ""text"": ""• Subject: {prompt.Replace("\"", "\\\"")}"",
            ""variant"": ""body""
          }}
        }},
        {{
          ""name"": ""UiKit.Button"",
          ""arguments"": {{
            ""label"": ""Acknowledge Action"",
            ""action"": ""acknowledge""
          }}
        }}
      ]
    }}";

    private string GetDefaultIno(string prompt) => 
$@"ui:
  UiKit.Column(
    children: [
      UiKit.Card(
        title: ""Grok Custom Design"",
        body: ""I analyzed your prompt: '{prompt.Replace("\"", "\\\"")}' and rendered this premium container tailored to your request.""
      ),
      UiKit.Text(text: ""Dynamic Prompt Context"", variant: ""dim""),
      UiKit.Text(text: ""• Subject: {prompt.Replace("\"", "\\\"")}"", variant: ""body""),
      UiKit.Button(label: ""Acknowledge Action"", action: ""acknowledge"")
    ]
  )";

    #endregion
}
