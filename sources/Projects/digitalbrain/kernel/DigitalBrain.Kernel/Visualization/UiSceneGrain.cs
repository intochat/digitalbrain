using System;
using System.Text.Json;
using System.Threading.Tasks;
using DigitalBrain.Runtime.Visualization;
using Orleans;

namespace DigitalBrain.Kernel.Visualization;

[Orleans.Providers.StorageProvider(ProviderName = "digitalbrain")]
public sealed class UiSceneGrain : Grain, IUiSceneGrain
{
    private static readonly string TopBarTemplate =
        "import digitalbrain;\n" +
        "\n" +
        "widget root = Panel(\n" +
        "  radius: 20.0,\n" +
        "  padding: 16.0,\n" +
        "  child: HStack(\n" +
        "    between: true,\n" +
        "    children: [\n" +
        "      HStack(\n" +
        "        gap: 12.0,\n" +
        "        children: [\n" +
        "          GlowIcon(seed: 1, size: 20.0, tone: \"teal\", shapeHint: \"orb\"),\n" +
        "          Text(text: data.title, variant: \"title\"),\n" +
        "        ]\n" +
        "      ),\n" +
        "      HStack(\n" +
        "        gap: 12.0,\n" +
        "        children: [\n" +
        "          Badge(text: data.statusText, tone: data.statusTone),\n" +
        "        ]\n" +
        "      )\n" +
        "    ]\n" +
        "  )\n" +
        ");\n";

    private static readonly string ScenarioExplorerTemplate =
        "import digitalbrain;\n" +
        "\n" +
        "widget root = Panel(\n" +
        "  radius: 24.0,\n" +
        "  padding: 20.0,\n" +
        "  child: VStack(\n" +
        "    gap: 16.0,\n" +
        "    cross: \"stretch\",\n" +
        "    children: [\n" +
        "      HStack(\n" +
        "        gap: 8.0,\n" +
        "        children: [\n" +
        "          GlowIcon(seed: 2, size: 16.0, tone: \"gold\", shapeHint: \"orb\"),\n" +
        "          SectionLabel(text: \"Scenario Explorer\"),\n" +
        "        ]\n" +
        "      ),\n" +
        "      Divider(),\n" +
        "      HStack(\n" +
        "        gap: 8.0,\n" +
        "        equal: true,\n" +
        "        children: [\n" +
        "          Button(label: \"digitalbrain.ino\", onTap: event \"selectScenario\" { key: \"digitalbrain.ino\" }),\n" +
        "          Button(label: \"document_analysis.ino\", onTap: event \"selectScenario\" { key: \"document_analysis.ino\" }),\n" +
        "        ]\n" +
        "      ),\n" +
        "      CodeEditor(text: data.codeText),\n" +
        "      HStack(\n" +
        "        gap: 12.0,\n" +
        "        equal: true,\n" +
        "        children: [\n" +
        "          Button(label: \"Save Scenario\", onTap: event \"saveScenario\" {}),\n" +
        "          Button(label: \"Run Test\", onTap: event \"runScenarioTest\" {}),\n" +
        "        ]\n" +
        "      )\n" +
        "    ]\n" +
        "  )\n" +
        ");\n";

    private static readonly string InspectorPanelTemplate =
        "import digitalbrain;\n" +
        "\n" +
        "widget root = Panel(\n" +
        "  radius: 24.0,\n" +
        "  padding: 20.0,\n" +
        "  child: VStack(\n" +
        "    gap: 16.0,\n" +
        "    cross: \"stretch\",\n" +
        "    children: [\n" +
        "      HStack(\n" +
        "        between: true,\n" +
        "        children: [\n" +
        "          HStack(\n" +
        "            gap: 8.0,\n" +
        "            children: [\n" +
        "              GlowIcon(seed: 3, size: 16.0, tone: \"amber\", shapeHint: \"orb\"),\n" +
        "              Text(text: \"Inspector\", variant: \"title\"),\n" +
        "            ]\n" +
        "          ),\n" +
        "          Button(label: \"X\", onTap: event \"closeSurface\" {})\n" +
        "        ]\n" +
        "      ),\n" +
        "      Divider(),\n" +
        "      SectionLabel(text: \"IDENTIFIER\"),\n" +
        "      Text(text: data.label, variant: \"heading\"),\n" +
        "      Divider(),\n" +
        "      SectionLabel(text: \"RFW SURFACE / SCRIPT\"),\n" +
        "      CodeEditor(text: data.codePayload),\n" +
        "      Divider(),\n" +
        "      HStack(\n" +
        "        gap: 12.0,\n" +
        "        equal: true,\n" +
        "        children: [\n" +
        "          Button(label: \"Fire Synapse\", onTap: event \"fireSynapse\" {}),\n" +
        "          Button(label: \"Delete Node\", onTap: event \"deleteNode\" {})\n" +
        "        ]\n" +
        "      )\n" +
        "    ]\n" +
        "  )\n" +
        ");\n";

    private static readonly string NotificationFeedTemplate =
        "import digitalbrain;\n" +
        "\n" +
        "widget root = Panel(\n" +
        "  radius: 20.0,\n" +
        "  padding: 16.0,\n" +
        "  child: HStack(\n" +
        "    gap: 16.0,\n" +
        "    cross: \"center\",\n" +
        "    children: [\n" +
        "      Avatar(initials: data.initials, tone: data.tone, size: 40.0),\n" +
        "      VStack(\n" +
        "        cross: \"start\",\n" +
        "        gap: 4.0,\n" +
        "        children: [\n" +
        "          Text(text: data.title, variant: \"title\"),\n" +
        "          Text(text: data.body, variant: \"dim\"),\n" +
        "        ]\n" +
        "      )\n" +
        "    ]\n" +
        "  )\n" +
        ");\n";

    public Task<(string RfwTemplate, string DataJson)> GetLayoutAsync(string layoutName, string? neuronId = null)
    {
        switch (layoutName.ToLowerInvariant())
        {
            case "top_bar":
                var topBarData = new
                {
                    title = "DigitalBrain Substrate v5",
                    statusText = "Orleans Connected",
                    statusTone = "teal"
                };
                return Task.FromResult((TopBarTemplate, JsonSerializer.Serialize(topBarData)));

            case "scenario_explorer":
                var scenarioData = new
                {
                    codeText = "neuron DigitalBrain.System\n" +
                               "  \"The distributed OS coordinator. Starts core services, manages dynamic resources, and binds the visual shell.\"\n\n" +
                               "  using loaded            = synapse(DigitalBrain.Kernel.Loaded)\n" +
                               "  using brains            = neuron(DigitalBrain.BrainRegistry)\n" +
                               "  using aspire            = neuron(DigitalBrain.SDK.AspireRuntime)\n\n" +
                               "  on loaded:\n" +
                               "    log \"system: initializing DigitalBrain substrate\"\n" +
                               "    ask brains to \"list\"\n" +
                               "    ask aspire to \"register-resource orleans-redis\""
                };
                return Task.FromResult((ScenarioExplorerTemplate, JsonSerializer.Serialize(scenarioData)));

            case "inspector_panel":
                var inspectorData = new
                {
                    label = "Translation Neuron",
                    codePayload = "neuron LlmTranslationNeuron\n" +
                                  "  \"Performs real-time translations using reasoning-tier model inference.\"\n\n" +
                                  "  using request  = synapse(TranslateTextRequest)\n" +
                                  "  using response = synapse(TextTranslatedEvent)\n\n" +
                                  "  on request it:\n" +
                                  "    let translated = ask LLM to translate it.Text to it.TargetLanguage\n" +
                                  "    emit response { Text: translated }"
                };
                return Task.FromResult((InspectorPanelTemplate, JsonSerializer.Serialize(inspectorData)));

            case "notification_feed":
                var notificationData = new
                {
                    initials = "D",
                    tone = "cyan",
                    title = "Cortex Active",
                    body = "DigitalBrain substrate successfully unified. Constellation active."
                };
                return Task.FromResult((NotificationFeedTemplate, JsonSerializer.Serialize(notificationData)));

            default:
                throw new ArgumentException($"Unknown layout name '{layoutName}'");
        }
    }
}
