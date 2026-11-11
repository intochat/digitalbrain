; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DB0001 | DigitalBrain | Error | Module class needs [Module] and a public static ModuleManifest Manifest
DB0003 | DigitalBrain | Warning | Orleans attribute on a DigitalBrain identity type
DB0013 | DigitalBrain | Error | Signal loop inside a Neuron or App
DB0022 | DigitalBrain.Screens | Error | Empty or swallow-only catch
DB0025 | DigitalBrain.Screens | Warning | Hand-written JSON
DB0027 | DigitalBrain.Screens | Error | Missing spec comment
DB0028 | DigitalBrain.Screens | Error | Suppressing a DigitalBrain rule
DB0031 | DigitalBrain | Error | Cross-brain contract interface requires [Shareable]
DB0032 | DigitalBrain | Error | Neuron reentrancy requires explicit justification comment
DB0033 | DigitalBrain | Error | Synchronous contract call cycle detected
DB0040 | DigitalBrain | Disabled | Bare exception for an invariant inside a neuron or app
DB0041 | DigitalBrain | Error | Synapse signature outside reaction, view
DB0042 | DigitalBrain | Error | Synapse state declared in another assembly
DB0043 | DigitalBrain | Warning | Synapse state carries a status-like member
DB0044 | DigitalBrain | Error | [On] reaction in a synapse assembly
DB0045 | DigitalBrain | Error | Banned API in a synapse assembly
