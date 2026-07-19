---
layout: home

hero:
  name: DigitalBrain
  text: Durable agents for .NET
  tagline: Neurons, synapses, and simulations on Orleans and Aspire
  image:
    src: /logo.svg
    alt: DigitalBrain
  actions:
    - theme: brand
      text: Quickstart
      link: /quickstart
    - theme: alt
      text: Concepts
      link: /concepts
    - theme: alt
      text: Specification
      link: /specification

features:
  - title: Neurons
    details: A neuron is a durable agent — an Orleans journaled grain with dual durable journals for incoming and outgoing synapses, typed identity, owner-bound authorization, and restart recovery.
  - title: Synapses
    details: A synapse is an immutable typed message carrying correlation and causation lineage on every hop. Neurons declare what they consume with IHandle and what they produce with IEmit, provable at build time.
  - title: Simulations
    details: The testing primitive. Fire a synapse into a real in-process cluster and expect synapses on the timeline. The framework's own suite and its consumers' suites use the same machine.
---
