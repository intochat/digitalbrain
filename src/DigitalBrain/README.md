# DigitalBrain

Framework for building AI-native apps in IntoChat OS. Designed to be used by humans and agents.
The vocabulary for writing neurons, apps and modules. No runtime and no Orleans: a runtime package hosts what you write here.

## Glossary

- **Neuron**: an addressable unit with an id `brain/type/key` that runs one turn at a time.
- **Signal**: a typed fact a neuron publishes.
- **Synapse**: a durable route that carries one signal type from one neuron to another.
- **Brain**: the scope every neuron lives in; `@kernel` is the reserved system scope.
- **App**: a class that serves a person through views and reactions.
- **Module**: the unit of code that provides capabilities and declares what it needs.
- **Capability**: a versioned contract, such as `storage@1`, that a module provides and an app requires.
- **Connection**: a person's authorised use of a module in a brain.
- **Topology**: the shape a brain runs in: `local`, `local+cloud` or `cloud`.
- **Reaction**: a method that runs when a signal arrives.
