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
- **Synapses.** Immutable typed fact records. The kernel wraps each one in a read-only delivery
  envelope carrying identity, correlation and causation lineage, origin sequence and timestamp;
  authors cannot construct that envelope. Neurons declare `IHandle<TSynapse>` and `IEmit<TSynapse>`;
  a source generator emits a dispatch manifest whose completeness is proven without a cluster.
- **The synapse fabric.** A durable outbox is the source of truth for delivery: at-least-once per
  receiver, effectively-once processing through `SynapseId` dedupe, and a bounded retry horizon.
  Broadcast addresses handler **types** composed via `AddBroadcastHandlers` at silo start; the
  instance is minted from the firing correlation so never-activated neurons still receive facts.
  The durable registry remains only for install-time behavior subscriptions.
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

### Changed

- **A journal is a bounded feed, not an unbounded log.** Each journal now keeps a delta log bounded
  by both record count and total bytes, a durable tally of how many of each synapse type it has
  recorded, and a monotonic sequence. Compaction evicts from the delta log only, so the tally and
  the sequence survive it. Journal reads now take a cursor and return only later delivery envelopes;
  a cursor overtaken by compaction receives the complete tally summary and a resume sequence instead
  of a gap. Storage per neuron is bounded for the first time. The cost, stated plainly: the journal
  is no longer an audit log.
- **Delivery metadata moved off the payload.** `Synapse.Metadata`, `Synapse.Stamped`,
  `SynapseMetadata` and `RoutingMode` are gone. A synapse is now exactly the record its author writes;
  the kernel snapshots it into a kernel-owned `SynapseDelivery` envelope, which is the durable unit
  carried by the outbox and journal entries. A journal entry keeps its feed-local cursor sequence
  distinct from the envelope's origin sequence. Handlers receive their own snapshot, and neuron
  activations reject Orleans interleaving modes, including interleaving grain timers, so one turn
  cannot corrupt another turn's lineage or journal order.
- **Dedupe is O(1).** Detecting a redelivered synapse was a linear scan of the whole incoming
  journal, deserialized on every delivery, making delivery O(n) per synapse and O(n²) over a
  neuron's lifetime. A neuron now keeps the last 4,096 handled `SynapseId`s in a bounded durable
  ring, mirrored in memory and rebuilt on activation, so delivery cost is flat with respect to
  journal length. Measured over 1,000 deliveries into a journal already holding 1,000: allocation
  fell from 2.9× the baseline batch to parity, and a test fails if that regresses.

### Known limitations

- An Orleans client is a trusted cluster peer. The owner boundary constrains neuron-to-neuron and
  registry traffic; it cannot constrain a process that already holds an `IGrainFactory`. Authenticate
  at the edge and never expose an Orleans client endpoint publicly.
- A journal retains only a recent window of synapses. Older synapses are evicted and survive as
  counts, not as records, so history older than the window cannot be read back.
- Effectively-once processing holds within a window of the last 4,096 deliveries per neuron. A
  redelivery older than that window would be handled again.
- The `Embedding` model tier cannot work. Every tier is registered as an `IChatClient`, and an
  embedding model is an `IEmbeddingGenerator<string, Embedding<float>>`.
- No timeline stream, so a client can fire and read but cannot observe; samples poll.
- The client projection still delegates to Orleans' `AsClient()`, which would leak a credentialed
  provider connection string if the brain were configured with durable stores.
- `Microsoft.Agents.AI.DevUI` is not wired.
- The generated dispatch manifest does not dispatch. Runtime dispatch reflects over `IHandle<>`; the
  manifest encodes the same wiring at compile time but is consumed only by a contract test, so the
  same knowledge has two sources of truth and the compile-time one is decorative.
