# DigitalBrain.Capabilities.Compute

The `compute@1` capability contract: cost accounting, funding, budgets, ambient traces, price lists, and external HTTP call metering.

- `ICostMeter`, `IPriceList`, `Cost`, `CostKind`, `CostSummary`, `Funding`, `PriceQuote`, `ComputeTracePage`, and `BudgetExhaustedException`.
- `Trace` and `TraceContext` over `AsyncLocal<Trace?>`.
- `ExternalCallHandler`, `ExternalCallHttp`, `MeteredHttpClients`, and `ModulePriceLists` (`AddModulePriceList<TPriceList>`).

References `DigitalBrain` and `Microsoft.Extensions.Http` only; no Orleans, no ASP.NET Core.
