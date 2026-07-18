# NeuronML — Self-Optimizing Neurons via Local ML

NeuronML gives every neuron in ino the ability to learn from its own decisions and progressively replace expensive LLM calls with microsecond local ML predictions. The LLM remains as the teacher and fallback — the ML model is the student that gets cheaper over time.

## How it works

Every neuron makes decisions via LLM calls. NeuronML captures those decisions as training data, trains a local LightGBM binary classifier via ML.NET, and serves predictions when confident. The system is invisible until it has enough data — then it starts saving tokens automatically.

### The loop

```
Neuron makes a decision (e.g., "allow this tool call?")
  → LLM handles it (costs tokens, takes ~500ms)
  → Outcome recorded as training data (features + label)
  → After 50 decisions: first LightGBM model trains (~50ms)
  → Next decisions: ML predicts first
    → If confident (>90%): return instantly (microseconds, zero tokens)
    → If uncertain: fall through to LLM (still recorded for training)
  → Retrain every 25 new decisions
  → Model accuracy improves over time
```

### Timeline for a typical session

| Decisions | Behavior |
|---|---|
| 0–49 | Pure LLM. Every decision is silently recorded as training data. |
| 50 | First model trains. `agents.ml.retrains` counter increments. ~50ms. |
| 51+ | High-confidence decisions served by ML. Low-confidence → LLM fallback. |
| 75, 100, 125... | Model retrains, accuracy improves. |

## Architecture

### Three primitives

1. **NeuronOptimizer** (`src/Core/ML/NeuronOptimizerGrain.cs`) — per-neuron Orleans grain that accumulates decision data, trains LightGBM, and serves predictions. State persists across silo restarts.

2. **FeatureArchitect** (`src/Core/ML/FeatureArchitectGrain.cs`) — LLM-based grain that designs which features each neuron should collect, selecting from a predefined catalog of 10 extractors.

3. **NeuronCreator** (`features/ino-new/InoNew.Core/NeuronCreatorGrain.cs`) — factory that guarantees every runtime-created neuron is born with ML capability: Architect designs schema → Registry creates neuron → Optimizer initialized.

### Feature catalog

Predefined extractors that the FeatureArchitect selects from:

| ExtractorId | What it measures |
|---|---|
| `ToolNameHash` | Hash of the tool or verb name |
| `CallerHash` | Hash of the calling agent type |
| `ArgsComplexity` | Argument JSON length + key count |
| `PolicyCount` | Number of active authorization policies |
| `PolicyMatchScore` | Keyword overlap between tool name and policies |
| `ContextLength` | Number of recent conversation messages |
| `TimeOfDay` | UTC hour (0-23) |
| `DayOfWeek` | Day number (0-6) |
| `HistoricalSuccessRate` | Past allow/success ratio for this pattern |
| `HistoricalFailRate` | Past deny/fail ratio for this pattern |

### First consumer: ApproverAgent

The `ApproverAgent` (`src/Neurons/Security/ApproverAgent.cs`) is the first neuron using NeuronML. The authorization flow is:

```
Authorize(request) →
  1. Memo cache check (existing, unchanged)
  2. ML fast-path (NEW): optimizer.Predict(features)
     → confidence >= 0.90 → return immediately, skip LLM
     → confidence < 0.90 → fall through to LLM
  3. LLM JudgeAsync (existing, unchanged)
  4. Record outcome for ML training (NEW)
```

Human approval outcomes (`ResolveApproval`) are also recorded — when a user taps "allow" or "deny", that becomes high-quality training data.

## File layout

```
src/Core/ML/
├── FeatureSchema.cs           — per-neuron schema: which features to collect
├── FeatureCatalog.cs          — 10 predefined extractors + DecisionContext
├── DecisionRecord.cs          — training row: float[] features + label
├── OptimizationResult.cs      — prediction result: bool + confidence
├── INeuronOptimizer.cs        — Orleans grain interface
├── NeuronOptimizerGrain.cs    — LightGBM training + prediction + ONNX export
├── NeuronOptimizerState.cs    — persistent state: records, model bytes, metrics
├── IFeatureArchitect.cs       — Orleans grain interface
└── FeatureArchitectGrain.cs   — LLM-based feature schema designer

features/ino-new/InoNew.Core/
├── INeuronCreator.cs          — factory grain interface
└── NeuronCreatorGrain.cs      — births neurons with ML from day one
```

## NuGet packages

- `Microsoft.ML` (5.0.0-preview.1) — MLContext, pipeline, prediction engine
- `Microsoft.ML.LightGbm` (5.0.0-preview.1) — LightGBM binary classifier

Both added to `src/Core/Core.csproj` via centralized package management.

## Telemetry

Three new counters visible in the Aspire dashboard under the `IAW` meter:

| Counter | Description |
|---|---|
| `agents.ml.predictions` | Decisions served by local ML model (tagged: tool.name, prediction, confidence) |
| `agents.ml.fallbacks` | ML confidence too low, fell through to LLM (tagged: tool.name) |
| `agents.ml.retrains` | Model retrain events (tagged: neuron.type, accuracy, auc) |

Training events also emit OpenTelemetry spans (`ml.train`) with `neuron.type` and `record.count` tags.

## GPU path (future)

The current implementation trains and predicts on CPU — LightGBM on tabular features is microseconds, faster than GPU marshaling overhead. For future deep learning models (text embeddings, sequence classification):

1. ML.NET exports to ONNX natively via `mlContext.Model.ConvertToOnnx()`
2. `NeuronOptimizerGrain.ExportOnnx()` is already implemented
3. Add `Microsoft.ML.OnnxRuntime.Gpu` NuGet + swap prediction engine for ONNX Runtime with CUDA execution provider
4. RTX 5080 supports this via `NvTensorRtRtx` execution provider (Windows ML) or direct CUDA EP

## E2E integration — travel assistant

NeuronML is wired into the complete E2E pipeline. Every user query that flows through the travel assistant records ML training data at two decision points:

### SearchEngine routing decisions

`SearchEngineGrain.HandleUserMessageAsync` records every routing decision:
- **Successful route** (label=1): features include query complexity, specialist hash, memory hit count, time of day, specialist count
- **Failed route / no specialist** (label=0): same feature shape, negative signal

Over time, the search-engine optimizer learns patterns like "query containing 'flight' → flight-search specialist" and can eventually pre-route common queries without an LLM call.

### Handler execution outcomes

`NeuronGrain.HandleAsync` records every specialist handler invocation:
- Features: synapse verb hash, source neuron hash, payload complexity, decay score, time of day
- Label: handler success (1) or failure (0)

Each specialist neuron (flight-search, hotel-search, place-discovery) gets its own optimizer grain that learns its execution patterns independently.

### E2E data flow

```
User: "find flights from NYC to Bali"
  → SearchEngine routes to flight-search    ← ML records routing decision
  → NeuronGrain.HandleAsync dispatches      ← ML records handler outcome
  → FlightSearchHandler returns results
  → Response rendered as FlightCard RFW
  → Two DecisionRecords created (one per decision point)
```

### Verified via E2E tests

The `FindFlights_RendersFlightCards` test (`test/E2E.Tests/Travel/FlightSearchE2E.cs`) validates that:
1. The full gRPC → Orleans → specialist pipeline works
2. The `search-engine` optimizer grain is accessible and healthy
3. ML recording doesn't break the existing flow (all wrapped in try/catch)

Run: `dotnet test test/E2E.Tests --filter "FindFlights_RendersFlightCards"`

## Testing

### BDD tests (5 scenarios)

`features/ino-new/InoNew.Tests/Features/NeuronML.feature`:

| Scenario | What it proves |
|---|---|
| Optimizer records and trains after threshold | 50 decisions → model trains, accuracy > 0.5 |
| Optimizer predicts with high confidence | Clear patterns → correct prediction, confidence > 0.85 |
| Optimizer returns null before training | No model → graceful null → LLM fallback |
| FeatureArchitect designs schema | LLM selects 4-8 features from catalog, all valid |
| NeuronCreator births neuron with ML | Full loop: architect → registry → optimizer initialized |

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "FullyQualifiedName~NeuronML"`

### E2E tests (travel pipeline)

Run: `dotnet test test/E2E.Tests --filter "FindFlights_RendersFlightCards"`

## Configuration

No configuration needed. NeuronML is constitutional — it activates automatically:

- **Approver**: starts recording decisions on first `Authorize()` call
- **Runtime neurons**: any neuron created via `INeuronCreator` gets ML from birth
- **Training threshold**: 50 minimum records, retrain every 25 after that
- **Confidence threshold**: 0.90 (only use ML when this confident)
- **Max records**: 10,000 per neuron (circular buffer, oldest dropped)

These constants live in `NeuronOptimizerState` and can be adjusted per-neuron if needed.
