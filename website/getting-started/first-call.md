# Make the first MCP call

This walkthrough uses the running MCP edge to inspect and invoke a chat neuron.

## Start DigitalBrain

```powershell
cd hosts/DigitalBrain.AppHost
aspire run
```

In the Aspire dashboard, open the `brain-mcp` resource and copy its HTTP endpoint. Configure your MCP client to use that base URL; the current `MapMcp()` registration maps Streamable HTTP at the application root. The JSON below represents MCP tool arguments; it is not a PowerShell command.

## Describe a neuron

Call `neuron_describe`:

```json
{
  "address": "local-owner|main|chat/main"
}
```

The response reports kind `chat`, its revision, and the supported `chat.post.v1` contract.

## Invoke the chat contract

Call `neuron_invoke`:

```json
{
  "address": "local-owner|main|chat/main",
  "contract": "chat.post.v1",
  "inputJson": "{\"text\":\"Hello from the docs\"}",
  "commandId": "docs-first-call-001"
}
```

Keep the `commandId` stable when retrying the same intent. The kernel returns the previous receipt instead of appending the event twice.

## Read the projection

Call `neuron_read`:

```json
{
  "address": "local-owner|main|chat/main",
  "projection": "default"
}
```

The state contains the message you posted. The call traveled through MCP, `INeuron.InvokeAsync`, `NeuronGrain`, and `ChatKind`.

::: warning Development identity
The MCP edge currently injects `local-owner|actor/mcp-dev|session/dev` as a hard-coded caller. Do not interpret this tutorial as proof of production authentication.
:::
