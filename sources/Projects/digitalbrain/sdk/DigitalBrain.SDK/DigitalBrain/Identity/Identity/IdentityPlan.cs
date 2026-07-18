using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Filters;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Identity;

namespace DigitalBrain.SDK.DigitalBrain.Identity.Identity;

public static class IdentityPlan
{
    public const string CardLibrary = "digitalbrain";
    public const string CardRootWidget = "LoginCard";

    public static readonly string LoginCardSource =
        "import digitalbrain;\n" +
        "\n" +
        "widget root = Panel(\n" +
        "  padding: 24.0,\n" +
        "  child: VStack(\n" +
        "    gap: 16.0,\n" +
        "    cross: \"start\",\n" +
        "    children: [\n" +
        "      Text(text: \"Sign in to DigitalBrain\", variant: \"title\"),\n" +
        "      Text(text: \"Access the decentralized, cognitive kernel.\", variant: \"dim\"),\n" +
        "      ...for err in [data.errorMessage]:\n" +
        "        ...if err != \"\":\n" +
        "          Panel(\n" +
        "            padding: 12.0,\n" +
        "            child: Text(text: err, color: 4294921292),\n" + // Red-rose color 0xFFFF4C4C
        "          ),\n" +
        "      ...if data.isBranching:\n" +
        "        VStack(\n" +
        "          gap: 12.0,\n" +
        "          children: [\n" +
        "            Text(text: \"Create branch of '\" + data.sourceBrainId + \"'\"),\n" +
        "            Text(text: \"Select Deployment Target:\", variant: \"dim\"),\n" +
        "            HStack(\n" +
        "              gap: 8.0,\n" +
        "              children: [\n" +
        "                TabButton(label: \"Local Cluster\", active: data.syncTarget == \"local\", onTap: event \"selectSyncTarget\" { target: \"local\" }),\n" +
        "                TabButton(label: \"Kernel Cloud\", active: data.syncTarget == \"cloud\", onTap: event \"selectSyncTarget\" { target: \"cloud\" }),\n" +
        "                TabButton(label: \"Azure\", active: data.syncTarget == \"azure\", onTap: event \"selectSyncTarget\" { target: \"azure\" }),\n" +
        "                TabButton(label: \"GCP (Soon)\", active: data.syncTarget == \"gcp\", onTap: event \"selectSyncTarget\" { target: \"gcp\" }),\n" +
        "              ],\n" +
        "            ),\n" +
        "            ...for target in [data.syncTarget]:\n" +
        "              ...if target == \"gcp\":\n" +
        "                Panel(\n" +
        "                  padding: 8.0,\n" +
        "                  child: Text(text: \"Google Cloud (GCP) self-hosting is coming soon!\", color: 4294951168),\n" +
        "                ),\n" +
        "              ...if target == \"azure\":\n" +
        "                Panel(\n" +
        "                  padding: 8.0,\n" +
        "                  child: VStack(\n" +
        "                    gap: 4.0,\n" +
        "                    cross: \"start\",\n" +
        "                    children: [\n" +
        "                      Text(text: \"Azure Synapse Provisioning Info:\", color: 4286578687),\n" +
        "                      Text(text: \"• Resource Group Name: [Your Brain Name]\", variant: \"dim\"),\n" +
        "                      Text(text: \"• Location: eastus\", variant: \"dim\"),\n" +
        "                      Text(text: \"• Synapse SDK: Microsoft.Azure.ResourceGroup.Create\", variant: \"dim\"),\n" +
        "                    ],\n" +
        "                  ),\n" +
        "                ),\n" +
        "            PromptInput(\n" +
        "              placeholder: \"new-branch-name\",\n" +
        "              submitLabel: \"Branch\",\n" +
        "              onSubmit: event \"submitCreateBrain\" { source: data.sourceBrainId },\n" +
        "            ),\n" +
        "            Button(label: \"Cancel\", onTap: event \"cancelBranch\" { }),\n" +
        "          ],\n" +
        "        )\n" +
        "      ...else if data.isSpawning:\n" +
        "        VStack(\n" +
        "          gap: 12.0,\n" +
        "          children: [\n" +
        "            Text(text: \"Spawn new clean brain\"),\n" +
        "            Text(text: \"Select Deployment Target:\", variant: \"dim\"),\n" +
        "            HStack(\n" +
        "              gap: 8.0,\n" +
        "              children: [\n" +
        "                TabButton(label: \"Local Cluster\", active: data.syncTarget == \"local\", onTap: event \"selectSyncTarget\" { target: \"local\" }),\n" +
        "                TabButton(label: \"Kernel Cloud\", active: data.syncTarget == \"cloud\", onTap: event \"selectSyncTarget\" { target: \"cloud\" }),\n" +
        "                TabButton(label: \"Azure\", active: data.syncTarget == \"azure\", onTap: event \"selectSyncTarget\" { target: \"azure\" }),\n" +
        "                TabButton(label: \"GCP (Soon)\", active: data.syncTarget == \"gcp\", onTap: event \"selectSyncTarget\" { target: \"gcp\" }),\n" +
        "              ],\n" +
        "            ),\n" +
        "            ...for target in [data.syncTarget]:\n" +
        "              ...if target == \"gcp\":\n" +
        "                Panel(\n" +
        "                  padding: 8.0,\n" +
        "                  child: Text(text: \"Google Cloud (GCP) self-hosting is coming soon!\", color: 4294951168),\n" +
        "                ),\n" +
        "              ...if target == \"azure\":\n" +
        "                Panel(\n" +
        "                  padding: 8.0,\n" +
        "                  child: VStack(\n" +
        "                    gap: 4.0,\n" +
        "                    cross: \"start\",\n" +
        "                    children: [\n" +
        "                      Text(text: \"Azure Synapse Provisioning Info:\", color: 4286578687),\n" +
        "                      Text(text: \"• Resource Group Name: [Your Brain Name]\", variant: \"dim\"),\n" +
        "                      Text(text: \"• Location: eastus\", variant: \"dim\"),\n" +
        "                      Text(text: \"• Synapse SDK: Microsoft.Azure.ResourceGroup.Create\", variant: \"dim\"),\n" +
        "                    ],\n" +
        "                  ),\n" +
        "                ),\n" +
        "            PromptInput(\n" +
        "              placeholder: \"new-brain-name\",\n" +
        "              submitLabel: \"Spawn\",\n" +
        "              onSubmit: event \"submitCreateBrain\" { source: \"\" },\n" +
        "            ),\n" +
        "            Button(label: \"Cancel\", onTap: event \"cancelBranch\" { }),\n" +
        "          ],\n" +
        "        )\n" +
        "      ...else:\n" +
        "        VStack(\n" +
        "          gap: 12.0,\n" +
        "          children: [\n" +
        "            ...for brain in data.brains:\n" +
        "              HStack(\n" +
        "                gap: 12.0,\n" +
        "                children: [\n" +
        "                  Text(text: brain.name, variant: \"body\"),\n" +
        "                  Button(label: \"Login\", onTap: event \"submitLogin\" { username: \"local\", password: \"password\", brainId: brain.name }),\n" +
        "                  Button(label: \"Branch\", onTap: event \"openBranchPanel\" { source: brain.name }),\n" +
        "                ],\n" +
        "              ),\n" +
        "            Button(label: \"Spawn New Brain\", onTap: event \"openSpawnPanel\" { }),\n" +
        "          ],\n" +
        "        ),\n" +
        "    ],\n" +
        "  ),\n" +
        ");\n";

    public static string LoginCardDataJson(string username, string errorMessage = "", bool isBranching = false, bool isSpawning = false, string sourceBrainId = "", string syncTarget = "local")
    {
        var dbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalBrain", "databases");
        var brains = new List<object>();
        if (Directory.Exists(dbDir))
        {
            foreach (var file in Directory.GetFiles(dbDir, "*.db"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                brains.Add(new { name });
            }
        }
        if (brains.Count == 0)
        {
            brains.Add(new { name = "primary" });
        }

        return JsonSerializer.Serialize(new
        {
            username = username,
            errorMessage = errorMessage,
            isBranching = isBranching,
            isSpawning = isSpawning,
            sourceBrainId = sourceBrainId,
            syncTarget = syncTarget,
            brains = brains,
            source = LoginCardSource,
        });
    }

    public static LoginResult ToResult(RequestLogin req, bool success, string userId, string? errorMessage, string? sessionToken = null) =>
        new(Success:            success,
        UserId:             userId,
        ErrorMessage:       errorMessage,
        SessionToken:       sessionToken) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? CallerStampingOutgoingFilter.ExternalCallerSentinel,
            timestamp: default
        ) };

    public static RfwCard ToLoginCard(RequestLoginCard req) =>
        new(LibraryName:        CardLibrary,
        RootWidget:         CardRootWidget,
        DataJson:           LoginCardDataJson(req.UserId)) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: default
        ) };
}
