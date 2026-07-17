# Run DigitalBrain

The supported development entry point is the Aspire AppHost.

## Prerequisites

- The .NET SDK selected by the repository.
- The Aspire CLI.
- Docker Desktop or another compatible container runtime.
- Flutter only when working on the workspace client.

Node dependencies for the documentation are installed by the Aspire resource with `npm ci`.

## Start the topology

```powershell
cd hosts/DigitalBrain.AppHost
aspire run
```

Open the dashboard URL printed by Aspire. The topology includes:

- `brain-kernel` for Orleans and module execution.
- `brain-mcp` for MCP over HTTP.
- `brain-ui` for the workspace edge.
- `brain-docs` for this VitePress website.
- `ollama` and the configured local model.

The local host uses development identities and volatile journal storage. It is an evaluation environment, not a production deployment.

## Next

[Make the first MCP call](/getting-started/first-call) and trace it through the universal grain.
