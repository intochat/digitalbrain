---
title: Specification
---

# Specification

Executable product proofs live in the test tiers:

| Tier | Project | What it proves |
| --- | --- | --- |
| L0 | `tests/DigitalBrain.Tests` | Package graph, assembly boundaries, public shapes, capability reification, AI surface pins |
| L1 | `tests/DigitalBrain.TestingTests` | Testing product lifecycle (fixture, journals, clock, faults) |
| L1 | `tests/DigitalBrain.Quickstart.Tests` | External author greeter durability |
| L1 | `tests/DigitalBrain.Time.Tests` | Durable `ICountdown` lifecycle and recovery (Orleans reminder as wake authority) |
| L1 | `tests/DigitalBrain.Tasks.Tests` | Task Start/Cancel lifecycle via test-only `IWorker` |
| L1 | `tests/DigitalBrain.ModuleTests` | Typed LLM smoke; Concurrent/GroupChat Respond multi-participant + session reuse |
| L1 | `tests/DigitalBrain.Integrations.Tests` | Gmail ReadMessage admit + annotation refusal; Salesforce propose/reject/approve on scripted MCP edge |
| L2 | `tests/DigitalBrain.HostTests` | Exclusive AppHost graph health |

This page is authored markdown, not generated from feature files. Restore a generated specification
only when product behaviors are again expressed as durable, author-facing scenarios.

See also: `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md`.
