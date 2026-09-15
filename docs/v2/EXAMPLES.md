# Runnable behavior examples

Run these from the DigitalBrain repository root. They use the real durable neuron runtime and registered integration methods with simulated inputs; no X account or API key is needed.

## Assistant composes an Elon-post behavior

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*BehaviorToolFacts'
```

The workspace assistant receives: “Save and start a behavior that charts Elon Musk posts containing rocket.” A scripted model invokes the production catalog, save, start, read and list tools. The graph is:

`twitter:elonmusk → text contains rocket → map to chart arguments → ui.chart/append`

The fixture sends an irrelevant post, a matching post and a duplicate provider receipt. Exactly one chart point appears. The test also verifies existing specialist tools remain exposed and invalid definitions cannot be saved. The scripted model makes tool sequencing deterministic; it is not an evaluation of a live model's planning quality.

Implementation: [BehaviorToolFacts.cs](../../tests/DigitalBrain.Tests/Features/BehaviorToolFacts.cs). Its definition builder discovers the actual chart schema instead of hardcoding a second copy.

## Internal event behavior with restart

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*BehaviorFacts'
```

A plain neuron emits observations containing a label and value. The behavior filters positive values, maps the result and appends to the existing chart. The fixture shuts down the silo, starts a new one against the same file-backed storage, delivers another observation, then stops the behavior and verifies cleanup.

Implementation: [BehaviorFacts.cs](../../tests/DigitalBrain.Tests/Features/BehaviorFacts.cs).

## Structured decisions and uncertain effects

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*BehaviorDecisionFacts'
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*BehaviorRecoveryFacts'
```

Decision examples check valid structured output and refusal of invalid output or tool calls. Recovery examples inject lost responses and missing command completion records, restart storage, and verify that uncertain actions pause with the original command identity while later inputs remain queued.
