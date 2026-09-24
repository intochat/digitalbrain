# Compute Module Location

## Goal

Move the Compute capability module beside Apps under `src/Modules/DigitalBrain`, reflecting that both are platform-level DigitalBrain modules.

## Scope

- Move `src/Modules/Compute/Compute` to `src/Modules/DigitalBrain/Compute/Compute`.
- Move `src/Modules/Compute/Contracts` to `src/Modules/DigitalBrain/Compute/Contracts`.
- Move `src/Modules/Compute/Tests` to `src/Modules/DigitalBrain/Compute/Tests`.
- Move `src/Modules/Compute/README.md` to `src/Modules/DigitalBrain/Compute/README.md`.
- Preserve assembly names, namespaces, public APIs, resource names, and behavior.
- Update project references and solution grouping to the new paths.

## Dependencies

Compute contracts are consumed by AI, Broker, Connections, and IntoChat. The Compute implementation is referenced by AppHost, IntoChat, Receipts, Connections tests, and IntoChat E2E tests. Relative references from moved projects to the kernel, Inbox contracts, and shared test projects must also be adjusted.

## Validation

Run the Compute unit test project and build the dependent projects/AppHost. Confirm no stale `src/Modules/Compute` paths remain and run `git diff --check`.
