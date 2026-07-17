# DigitalBrain

DigitalBrain is an open-source capability operating system built on .NET Aspire and Orleans. One universal `NeuronGrain` hosts durable, addressable capabilities; registered `INeuronKind` strategies provide their behavior.

The current repository implements a compact v2 kernel, MCP and UI edges, a Flutter workspace, and explicitly composed modules for Workspace, AI, Web, Connections, Google, Salesforce, and Behaviors.

## The current execution path

```text
typed INeuronContract
  → NeuronProxy
  → INeuron.InvokeAsync
  → NeuronGrain
  → INeuronKind
  → journaled state and optional governed effect
```

The typed client façade is implemented. MCP and HTTP use the same universal invocation envelope at their edge. External mutations can be proposed, approved or declined, and claimed with an approval proof.

The repository is still under active development. Authentication uses development callers, journal storage is volatile in the local host, and several architecture boundaries remain targets or open decisions. See the [implementation status](website/reference/status.md) before relying on a capability claim.

## Repository shape

```text
kernel/        contracts, universal grain, typed proxy, effect gate
modules/       workspace, AI, Web, connections, providers, behaviors
edge/          MCP and UI gateway
hosts/         kernel host, Aspire AppHost, service defaults
workspace/     Flutter client
tests/         kernel and module conformance tests
website/       VitePress documentation
```

## Run it

```powershell
dotnet build Brain.slnx
dotnet test --logger "console;verbosity=minimal"
cd hosts/DigitalBrain.AppHost
aspire run
```

Open the Aspire dashboard and select `brain-docs` for the documentation, `brain-mcp` for MCP over HTTP, or `brain-ui` for the UI gateway.

## Start contributing

- [Run DigitalBrain](website/getting-started/index.md)
- [Make the first MCP call](website/getting-started/first-call.md)
- [Build a module against the current model](website/build/first-module.md)
- [Read the way of working](CLAUDE.md)
