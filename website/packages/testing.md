---
title: DigitalBrain.Testing
---

# DigitalBrain.Testing

The dev-only simulation package runs real neurons on a shared three-silo in-process Orleans cluster.
It supplies Reqnroll steps, journal assertions, deterministic placement labels, restart support, and
telemetry-based waits.

```gherkin
Scenario: a reply is recorded
    Given a brain for owner "verbs"
    When Ping is sent to the Greeter neuron named "polite"
    Then the outgoing journal of the Greeter neuron named "polite" contains Pong
```

Simulation diagnostics use the owner-bound session contract directly. The package does not expose a
second client facade and contains no fake model implementation.

This package references Kernel because it hosts real silos. It is never a production dependency.
