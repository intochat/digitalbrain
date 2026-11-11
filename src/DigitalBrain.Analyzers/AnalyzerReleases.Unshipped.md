; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DB0001 | DigitalBrain | Error | Module class needs [Module] and a public static ModuleManifest Manifest
DB0002 | DigitalBrain | Error | Scheduling attribute inside an App class
DB0003 | DigitalBrain | Warning | Orleans attribute on a DigitalBrain identity type
DB0013 | DigitalBrain | Error | Signal loop inside a Neuron or App
DB0020 | DigitalBrain.Apps | Error | [App] class must derive from App
DB0021 | DigitalBrain.Apps | Error | Requires names a module instead of a capability
DB0022 | DigitalBrain.Apps | Error | Empty or swallow-only catch
DB0023 | DigitalBrain.Apps | Error | Key composed from strings
DB0025 | DigitalBrain.Apps | Warning | Hand-written JSON
DB0026 | DigitalBrain.Apps | Warning | Re-implemented App base helper
DB0027 | DigitalBrain.Apps | Error | Missing spec comment
DB0028 | DigitalBrain.Apps | Error | Suppressing a DigitalBrain rule
DB0030 | DigitalBrain.Apps | Error | Single [App] class in application assembly
DB0031 | DigitalBrain | Error | Cross-brain contract interface requires [Shareable]
DB0032 | DigitalBrain | Error | Neuron reentrancy requires explicit justification comment
DB0033 | DigitalBrain | Error | Synchronous contract call cycle detected
DB0040 | DigitalBrain | Disabled | Bare exception for an invariant inside a neuron or app
DB0041 | DigitalBrain | Error | Synapse signature outside reaction, view
DB0042 | DigitalBrain | Error | Synapse state declared in another assembly
DB0043 | DigitalBrain | Warning | Synapse state carries a status-like member
DB0044 | DigitalBrain | Error | [On] reaction in a synapse assembly
DB0045 | DigitalBrain | Error | Banned API in a synapse assembly
