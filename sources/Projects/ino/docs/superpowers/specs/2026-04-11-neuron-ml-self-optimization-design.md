# NeuronML: Self-Optimizing Neurons via Local ML

**Date:** 2026-04-11
**Status:** Approved

## Problem

Every neuron in ino makes decisions via LLM calls — tool authorization, agent routing, code generation, memory recall. Each call costs tokens and adds latency. The system has rich telemetry (`gen_ai.*` spans, durable event logs, approver counters) but no feedback loop: decisions are never learned from.

## Solution

**NeuronML** — a framework layer in `iaw/Core/ML/` that gives every neuron self-optimization capability via ML.NET. Neurons collect decision data, train local LightGBM models, and progressively replace LLM calls with microsecond ML predictions. The LLM remains as fallback for novel cases.

Three neurons form the creation loop:

1. **NeuronCreator** — factory that births all runtime neurons. Guarantees every neuron gets a `FeatureSchema` from birth.
2. **FeatureArchitect** — when Creator spawns a neuron, Architect uses LLM to select which features from a predefined catalog are relevant for that neuron's decisions.
3. **NeuronOptimizer** — per-neuron grain that accumulates decision data, trains LightGBM, and serves predictions.

## Architecture

### Feature Catalog

A predefined set of feature extractors. The FeatureArchitect selects from this catalog — no arbitrary code generation on the feature extraction path.

| Feature | ExtractorId | Extracts From |
|---|---|---|
| Tool/verb name hash | `ToolNameHash` | tool or synapse verb |
| Caller agent type hash | `CallerHash` | calling agent's type name |
| Arguments complexity | `ArgsComplexity` | args JSON length + key count |
| Policy match score | `PolicyMatchScore` | keyword overlap with active rules |
| Active policy count | `PolicyCount` | number of stored policies |
| Conversation context length | `ContextLength` | recent message count |
| Hour of day | `TimeOfDay` | UTC hour (0-23) |
| Day of week | `DayOfWeek` | day number (0-6) |
| Historical success rate | `HistoricalSuccessRate` | past allow/success ratio for this pattern |
| Historical deny/fail rate | `HistoricalFailRate` | past deny/fail ratio for this pattern |

### FeatureSchema

```csharp
[GenerateSerializer]
public sealed record FeatureSchema(
    [property: Id(0)] string NeuronType,
    [property: Id(1)] IReadOnlyList<FeatureSlot> Slots,
    [property: Id(2)] DateTimeOffset DesignedAt);

[GenerateSerializer]
public sealed record FeatureSlot(
    [property: Id(0)] string Name,
    [property: Id(1)] string ExtractorId,
    [property: Id(2)] float Importance);
```

### NeuronOptimizer Grain

- **Key**: neuron type string (e.g. `"approver"`, `"agent-selector"`)
- **State**: circular buffer of `DecisionRecord` (max 10,000), serialized ML.NET model as `byte[]`, model version, last accuracy/AUC
- **Training trigger**: every 25 new records after minimum 50 accumulated
- **Algorithm**: LightGBM binary classifier via ML.NET — trains in ~50ms on tabular data
- **Confidence threshold**: 0.90 — only use ML prediction when this confident
- **ONNX export**: built-in via `mlContext.Model.ConvertToOnnx()` for future GPU serving

### Approver Integration (First Consumer)

In `ApproverAgent.Authorize()`, before the LLM `JudgeAsync()` call:

1. Extract features from `ToolAuthorizationRequest` + `LoadPolicies()`
2. Call `optimizer.Predict(features)`
3. If confidence >= 0.90 → return immediately, skip LLM
4. If confidence < 0.90 → fall through to existing LLM path
5. After LLM returns → `optimizer.RecordDecision(features, outcome)`
6. On `ResolveApproval` (human answer) → also record with human's label

### Self-Improving Creation Loop

```
Creator receives "create neuron for X"
  → generates system prompt + tool list
  → calls FeatureArchitect.DesignSchema(purpose, tools)
  → Architect LLM selects features from catalog
  → registers in NeuronRegistry with FeatureSchema
  → NeuronOptimizer activates with schema
  → neuron starts making decisions, records outcomes
  → after 50 decisions, first model trains
  → progressively replaces LLM calls with ML predictions
```

### NuGet Packages

- `Microsoft.ML` — MLContext, pipeline, prediction engine
- `Microsoft.ML.LightGbm` — LightGBM trainer

Added to `iaw/Core/Core.csproj` and `Directory.Packages.props`.

### Telemetry

New counters in `AgentTelemetry`:
- `agents.ml.predictions` — ML served the decision
- `agents.ml.fallbacks` — confidence too low, fell through to LLM
- `agents.ml.retrains` — retrain events

### File Layout

```
iaw/Core/ML/
├── FeatureSchema.cs
├── FeatureCatalog.cs
├── FeatureExtractor.cs
├── DecisionRecord.cs
├── INeuronOptimizer.cs
├── NeuronOptimizerGrain.cs
├── NeuronOptimizerState.cs
├── OptimizationResult.cs
├── IFeatureArchitect.cs
└── FeatureArchitectGrain.cs

features/ino-new/InoNew.Core/
├── INeuronCreator.cs          (new — grain interface)
└── NeuronCreatorGrain.cs      (new — factory implementation)

features/ino-new/InoNew.Tests/
├── Features/NeuronML.feature  (new — Gherkin contract)
├── Steps/NeuronMLSteps.cs     (new — step definitions)
└── Steps/NeuronMLScenarioTests.cs (new — xunit.v3 runner)
```
