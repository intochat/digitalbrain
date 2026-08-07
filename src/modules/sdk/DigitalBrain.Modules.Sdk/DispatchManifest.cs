using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Generated;

[ExcludeFromCodeCoverage]
internal static class DispatchManifest
{
    internal static readonly (string Neuron, string Synapse, bool IsHandler)[] Wirings =
    [
        ("DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron", "DigitalBrain.Modules.Sdk.Mcp.AuthorizationCompleted", false),
        ("DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron", "DigitalBrain.Modules.Sdk.Mcp.AuthorizationDenied", false),
        ("DigitalBrain.Modules.Sdk.Mcp.McpAuthorizationNeuron", "DigitalBrain.Modules.Sdk.Mcp.AuthorizationRequired", false),
        ("DigitalBrain.Modules.Sdk.Webhook.WebhookIngressNeuron", "DigitalBrain.Modules.Sdk.Webhook.VerifiedWebhookDeliveryReceived", true),
        ("DigitalBrain.Modules.Sdk.Webhook.WebhookIngressNeuron", "DigitalBrain.Modules.Sdk.Webhook.WebhookDeliveryAccepted", false),
        ("DigitalBrain.Modules.Sdk.Webhook.WebhookIngressNeuron", "DigitalBrain.Modules.Sdk.Webhook.WebhookDeliveryConflict", false),
        ("DigitalBrain.Modules.Sdk.Webhook.WebhookIngressNeuron", "DigitalBrain.Modules.Sdk.Webhook.WebhookDeliveryDuplicate", false),
    ];
}
