# Imported from v2-core (webhook only)

Source lifted after `merge -s ours` of v2-core into refactoring for history.

These files target v2-core thin `DigitalBrain.Core` (Neuron<TState>, Emit, Origin.IsExternalIngress).
They are **not** in DigitalBrain.slnx and do not build here yet.

Next: port into `DigitalBrain.Sdk` folders:
- `Mcp/` — current `DigitalBrain.Mcp.Sdk` (module southbound MCP rail)
- `Webhook/` — this import, adapted to product Core/Abstractions

Do not revive v2-core product/core/docs/scenarios from history; only this webhook surface is kept.
