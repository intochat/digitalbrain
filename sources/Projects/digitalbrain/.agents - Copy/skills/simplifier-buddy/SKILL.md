---
name: simplifier-buddy
description: "Rethink, optimize, and simplify the BrainOS / DigitalBrain codebase using first-principles thinking. Unify the platform under the v5 single-file (.ino) paradigm, support constructor-based neuron building and debugging, and streamline our domains to keep only what is vital."
---

# Simplifier Buddy — First-Principles Codebase Optimization

Use this skill when refactoring, simplifying, or optimizing the BrainOS substrate or the DigitalBrain operating system layer. It is built around first-principles thinking (the Elon Musk method) to aggressively prune bloat, align architecture, and consolidate under the unified `v5 (The Cut)` standard.

---

## 1. First-Principles Thinking (The Checklist)

1. **Question Every Requirement**: Every class, project, abstraction, or interface must justify its existence. If a feature does not directly serve the vision "user speaks/writes, the brain executes," it is a candidate for removal.
2. **Delete Any Part or Process You Can**: Do not build for "just in case" (YAGNI). Prune dead projects, unused helper classes, over-engineered mapping layers, and manual bootstrap steps.
3. **Simplify and Optimize**: Reduce nesting, unify namespaces explicitly, and replace complex custom systems with standard generic host patterns (`IConfiguration`, Orleans Facets, Keyed Services).
4. **Go Faster**: Minimize compilation time and test execution loops. Fast feedback is critical.

---

## 2. Core Simplifying Directives (The v5 Cut)

* **One File per Behavior (`.ino`)**: Ganish the triplet format (`.cs` + `.feature` + `.Steps.cs`). Authors write a single `.ino` file containing:
  * Neuron configuration (`using mailbox = neuron(...)`)
  * Synapse contract records (inline)
  * Dynamic RFW widget layout definitions (`rfw:` block)
  * Behavior execution flows (`on Activated:`)
  * Gherkin-less tests (`scenario` blocks)
* **One Process, One Silo**: Maintain exactly one Aspire composition running under a unified `BrainOS.Runtime` project.
* **No Global Catalogs**: Keep ports resolved lazily at activation time.
* **Domain Specific Names**: Rename types to be highly descriptive, clean, and explicit (e.g. `<Verb><Noun>Neuron` or `<NounPhrase>Neuron`). Avoid generic suffixes like `Manager`, `Helper`, `Service`, `Util`, or `Grain` (use grain only inside backing silos, never in public SDKs).

---

## 3. Dynamic Constructor-Based Neuron Building & Debugging

When simplifying the dynamic domain and Creator logic:
1. **The Dynamic Neuron Constructor**: Provide a programmatic constructor (or builder) in the SDK (`DigitalBrain.SDK.NeuronBuilder`) allowing developers and scripts to assemble a neuron dynamically in memory, configure its input/output synapse dependencies, and run it.
2. **Interactive Debugging**: Ensure neurons can run inside an isolated, lightweight test harness (`BrainOS.NeuronTesting` or direct in-memory silos) where synapse emissions are captured, step-by-step executions are logged, and exceptions are cleanly output without crashing the runtime.
3. **The SDK Versioning / Publishing Story**:
   * When publishing `.ino` files, they can declare an optional header defining SDK requirements (e.g. `requires sdk >= 5.1`).
   * Keep it simple: if the target brain does not support the declared capabilities or has a lower SDK version, fail with a clean, actionable `Neuron.ActivationFailed` error at activation time instead of introducing a heavy compile-time package manager.

---

## 4. NuGets, Flutter, and Best Coding Practices

* **Always Stay Pinned to the Latest Previews**: Keep Microsoft.Extensions.*, Orleans, and Microsoft.Extensions.AI updated to their latest preview versions via standard NuGet.
* **Best Practices for Keyed Services**: Avoid custom service routers when standard Microsoft dependency injection keyed services can resolve types (e.g. `[FromKeyedServices("...")` or generic `IAttributeToFactoryMapper`).
* **Declarative Settings & Secrets**: Never inject configuration values manually in constructors if they can be annotated using `[NeuronSetting("key", isPrivate: true)]` for automatic, secure, Aspire-aligned parameter resolution.
