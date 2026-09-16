# Flutter tests

Standalone tests run from `DigitalBrain.Modules.Flutter.Tests.csproj` and reference the shared DigitalBrain.Testing runtime harness.

`Scenarios/` owns this module's BDD bindings and feature files. They run through `tests/DigitalBrain.Tests` because the existing scenarios share a composed, multi-module BrainWorld. They are linked there once, not copied or compiled into this standalone test assembly. Module-specific helpers stay here; the shared test SDK does not depend on feature modules.
