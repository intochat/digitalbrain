# Changelog

All notable changes to DigitalBrain are recorded here. Versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Nothing has been published to NuGet yet.

## 0.1.0-alpha.1 — unreleased

The first cut of the v2 foundation. Prerelease because the framework pins the Orleans `10.2.2-rc.2`
line, including the experimental `Microsoft.Orleans.Journaling` package, and a stable release may not
depend on prereleases.

### Added

- **Neurons.** `Neuron` is an Orleans journaled grain with dual durable journals — incoming and
  outgoing — typed identity, owner-bound authorization, and restart recovery.
- **Synapses.** Immutable typed records carrying correlation and causation lineage stamped on every
  hop. Neurons declare `IHandle<TSynapse>` and `IEmit<TSynapse>`; a source generator emits a dispatch
  manifest whose completeness is proven without a cluster.
- **The synapse fabric.** A durable outbox is the source of truth for delivery: at-least-once per
  registered subscriber, effectively-once processing through `SynapseId` dedupe, and a bounded retry
  horizon. Broadcast is owner-scoped through a journaled subscription registry that tolerates neuron
  types registered after silo start, provided the neuron has activated at least once.
- **Multi-silo.** Cross-silo point-to-point and broadcast, cluster-wide registry correctness, and
  `[PinToSilo]` placement onto labelled silos.
- **AI model binding.** Role tiers bound to models by AppHost configuration; OpenAI and Anthropic
  adapters live only in `DigitalBrain.Kernel`. Three tiers work: fast, balanced and reasoning. The
  declared `Embedding` tier does not, and is listed under known limitations.
- **`DigitalBrain.Client`.** Owner sessions, typed neuron access, and fire-and-read from outside the
  cluster, with the owner boundary enforced at the session.
- **`DigitalBrain.Aspire.Hosting` and `DigitalBrain.Aspire`.** A brain resource composing Orleans and
  storage with a privileged silo projection and a client projection that carries no model binding and
  no secret.
- **`DigitalBrain.Testing`.** One shared in-process cluster per test run, a Gherkin vocabulary over
  `Fire`/`Expect`, and a deterministic scripted model that fails loudly on an unscripted prompt.
- **`DigitalBrain.DevTools`.** The Orleans Dashboard and a volatile journal store, both
  Development-only.
- **`DigitalBrain`.** A convenience metapackage pulling in `DigitalBrain.Abstractions`,
  `DigitalBrain.Client` and `DigitalBrain.Aspire` — the packages a consumer of a brain needs. It
  deliberately excludes `DigitalBrain.Kernel`, which is where provider SDKs and credentials live.

### Known limitations

- An Orleans client is a trusted cluster peer. The owner boundary constrains neuron-to-neuron and
  registry traffic; it cannot constrain a process that already holds an `IGrainFactory`. Authenticate
  at the edge and never expose an Orleans client endpoint publicly.
- Journals are never compacted, and every delivery deserializes the whole incoming journal to dedupe
  by `SynapseId`. The cost is O(n) per synapse and O(n²) over a neuron's lifetime: delivering 1,000
  synapses into a journal already holding 1,000 allocates 2.9× what the first 1,000 allocate.
- The `Embedding` model tier cannot work. Every tier is registered as an `IChatClient`, and an
  embedding model is an `IEmbeddingGenerator<string, Embedding<float>>`.
- One unreachable receiver blocks a neuron's entire outbox. The outbox drains in order and stops at
  the first entry with an undelivered receiver, stalling traffic to reachable receivers behind it
  until that entry exhausts its attempts or the 30-minute retry horizon expires.
- Subscriptions are never removed, so broadcast fan-out grows monotonically.
- A neuron that has never activated receives no broadcasts: subscription is registered during
  activation, and `EmitAsync` reads subscribers at emit time.
- No timeline stream, so a client can fire and read but cannot observe; samples poll.
- Outbox redelivery after a receiver outage is implemented but not proven by a scenario.
- The client projection still delegates to Orleans' `AsClient()`, which would leak a credentialed
  provider connection string if the brain were configured with durable stores.
- `Microsoft.Agents.AI.DevUI` is not wired.
- The generated dispatch manifest does not dispatch. Runtime dispatch reflects over `IHandle<>`; the
  manifest encodes the same wiring at compile time but is consumed only by a contract test, so the
  same knowledge has two sources of truth and the compile-time one is decorative.
