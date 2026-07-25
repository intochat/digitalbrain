---
title: Specification
---

# Specification

Executable product proofs live in the test tiers:

| Tier | Project | What it proves |
| --- | --- | --- |
| L0 | `tests/DigitalBrain.Tests` | Package graph boundaries and reflection on public shapes |
| L1 | `tests/DigitalBrain.TestingTests` | Testing product lifecycle (fixture, journals, clock, faults) |
| L1 | `tests/DigitalBrain.Quickstart.Tests` | External author greeter durability |
| L1 | `tests/DigitalBrain.Time.Tests` | Durable `ICountdown` lifecycle and recovery (Orleans reminder as wake authority) |
| L1 | `tests/DigitalBrain.ModuleTests` | Optional typed LLM smoke |
| L2 | `tests/DigitalBrain.HostTests` | Exclusive AppHost graph health |

This page is authored markdown, not generated from feature files. Restore a generated specification
only when product behaviors are again expressed as durable, author-facing scenarios.

See also: `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md`.
