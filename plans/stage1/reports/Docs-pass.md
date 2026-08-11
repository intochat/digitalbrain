# Stage 1 docs pass — source-truth report

## What changed

- `CLAUDE.md` now describes authenticated principal scoping, Execution-backed FIFO chat, the
  allow-all verified-actor MCP boundary, current module hooks, retained Salesforce Contracts, the
  completed W2 cleanup, and deferred module-owned testing.
- `UNIFIED-ARCHITECTURE.md` is now a compact current architecture record instead of a contradictory
  pre-implementation Bind/Tasks/ShowTime plan. It documents the permanent Connect wire family,
  current graph/relay flow, kernel invariants, identity, Execution, MCP, UI, composition, and Stage-2
  seams.

## Source evidence

- Graph names and fields checked against `ISynapseGraph`, `SynapseConnection`, `Connect`,
  `Disconnect`, `SynapseGraphNeuron`, `ConnectionRelayNeuron`, and `Neuron.Messaging`.
- Identity statements checked against `AuthHostingExtensions`, `MapAuth`, `HttpActor`,
  `PrincipalScoped`, and every `.AllowAnonymous()` site in the Kernel host.
- Durable-turn statements checked against `Chat`, `ChatTurnWorker`, and the Execution contracts and
  implementation.
- MCP statements checked against `McpServerNeuron`, `McpAuthorizationRail`,
  `McpAuthorizationNeuron`, `DurableMcpTokenCache`, and `Integration`.
- Composition statements checked against `DigitalBrainComposition`, each `Core.IModule`
  implementation, and AppHost resource configuration.
- Stale scan found no live Tasks paths, ShowTime design, central-test path, old test-count claim,
  or destructive-tool-refusal claim. The one `WantsTimeButton`/`ShowTime` mention says explicitly
  that W2 is complete.

## Adversarial review

- No wire alias was renamed; the document records the exact current aliases.
- Historical proof counts were removed because deleted tests are not current authority.
- The AppHost/Kernel catalog duplication and graph-neuron rename remain documented as decisions,
  not silently presented as complete.
- Salesforce Contracts is retained as directed and is not described as janitor trash.

## Gate

`pwsh -NoProfile -File scripts/gate.ps1`:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
GATE PASS
```

No automated test command ran.

## Conflicts & risks

- None. This pass changes documentation only; production source remains authoritative.

## Out of scope

- Resolving the catalog duplication, graph-neuron naming, and other Stage-2 structural seams.
