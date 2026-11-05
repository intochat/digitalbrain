# Testing Tiers

DigitalBrain enforces a multi-tier testing strategy ensuring deterministic execution and fast verification cycles.

## 1. Unit Facts

- Fast, in-memory execution running on Microsoft.Testing.Platform with xUnit v3.
- Isolated facts verify logic, state machines, and Roslyn analyzers without external dependencies.

## 2. Integration Facts

- Validate component interactions within memory or local test topologies.
- Example: `TestOS.Local()` validates neuron lifecycles and messaging without distributed overhead.

## 3. End-to-End (E2E) Conformance

- Validate host conformance against local Orleans clustering and cloud emulators (such as Azurite).
- Regulated by Roslyn banned symbols: conformance tests must execute completely and never be skipped.
