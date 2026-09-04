# Scripted Behaviors: Day-Zero Design

Date: 2026-09-04

## Goal

Make scripts the programmable application layer of DigitalBrain from the first working slice. The kernel remains a small, deterministic execution substrate. User-authored behaviors can later compose neurons and other behaviors through a narrow capability API.

The first script is `start.cs`. It runs after the owner brain is activated and receives an already-connected `IDigitalBrain` client.

## Runtime ownership

Aspire owns process and resource startup. The kernel owns brain lifecycle. Scripts own application behavior.

The startup sequence is:

1. Aspire starts storage, the Orleans kernel, and the scripting worker.
2. The kernel becomes healthy and activates the owner brain.
3. The kernel publishes one `DigitalBrainActivated` lifecycle signal.
4. Built-in module handlers perform lightweight initialization.
5. The scripting worker observes activation and runs `start.cs` once for that activation.
6. `start.cs` configures the user application and publishes `ApplicationStarted` when successful.

`start.cs` does not publish `DigitalBrainActivated`. A user script cannot claim that the kernel is ready.

## Concepts

### Neuron

A neuron is the trusted durable execution primitive. It owns identity, serialized turns, state, journals, signal dispatch, and learned synapses. Runtime-generated code does not define new Orleans grain types.

### Behavior

A behavior is a user-authored program. Eventually, each running behavior will be hosted by a generic durable behavior neuron, giving scripts neuron-like identity and lifecycle without loading generated assemblies into the silo.

### Script

A script is versioned source executed outside the kernel process. It receives only a typed context and explicitly granted capabilities. It cannot access Orleans, kernel services, credentials, or arbitrary host dependencies directly.

`start.cs` is a privileged system behavior only in the sense that Aspire selects it as the startup script. It still executes outside the silo and uses the same public client boundary.

## Messaging vocabulary

The intended public vocabulary is deliberately small:

- `Send`: deliver a signal to one addressed neuron or behavior and await its delivery outcome.
- `Publish`: deliver a signal to all durable matching subscriptions.
- `Run`: start another behavior and await its typed result.
- `Subscribe`: durable deployment/configuration that binds a signal and optional filter to a behavior.

There is no separate public `Broadcast` semantic. Kernel activation may internally use fan-out, but the public operation is `Publish`.

The first implementation slice does not add all four operations. It establishes the execution boundary on which they will be added.

## First working slice

The smallest proof of the architecture contains:

1. A real `DigitalBrain.Scripting` worker process, hosted by Aspire as a sibling of the kernel and MCP processes.
2. A configured startup script path, initially `scripts/start.cs`.
3. An Orleans client connection created through the existing `AddDigitalBrainClient` hosting path.
4. An activation observer that waits for `DigitalBrainActivated` from the owner root journal.
5. A script compiler/runner that executes `start.cs` out of process from the kernel and supplies a `ScriptContext` containing the connected `IDigitalBrain`.
6. A minimal example `start.cs` that records or publishes one meaningful startup result.
7. Tests proving ordering, single execution, failure reporting, and that generated script assemblies are not loaded into the silo.

The slice explicitly excludes general behavior deployment, runtime-created subscription registries, long-running workflow state machines, script-to-script `Run`, arbitrary NuGet references, and production sandboxing. Those become later slices after the day-zero execution boundary is proven.

## Script surface

The startup script is C# and is compiled against a deliberately tiny SDK. Its conceptual shape is:

```csharp
await brain.PublishAsync(new ApplicationStarted("digitalbrain"));
```

The implementation may initially expose this through `ScriptContext` until `PublishAsync` exists on the final client abstraction:

```csharp
await context.PublishAsync(new ApplicationStarted("digitalbrain"));
```

The script must not create an Aspire builder, start the kernel, obtain `IGrainFactory`, define a grain, or broadcast `DigitalBrainActivated`.

## Activation and idempotency

The activation journal is the source of truth. The worker reads or watches the owner root journal rather than relying on process timing.

Each execution is identified by owner, startup-script version, and activation signal identity. The worker records completion before acknowledging success. Re-observing the same activation must not execute the same script version twice.

For the first slice, the completion record may use a worker-owned durable file or store if introducing a behavior neuron would enlarge the proof unnecessarily. The persistence boundary must be replaceable by the future generic behavior neuron.

## Failures

- Compilation failure prevents execution and produces a structured diagnostic result.
- Runtime failure produces a structured failure result associated with the activation and script version.
- A failed script does not republish `DigitalBrainActivated` and does not make the kernel unhealthy.
- Retry is explicit. Automatic retry is not part of the first slice.
- Cancellation terminates the script worker invocation without stopping the kernel.

## Security boundary

Generated code never runs in the kernel process and never becomes part of a wire-contract assembly. The first slice is a trusted-development proof, not a production sandbox. Its API and process boundary must allow later addition of time limits, memory limits, reference allowlists, filesystem isolation, and capability grants without changing behavior contracts.

## Testing

The implementation is accepted when:

1. The solution builds with zero errors.
2. Existing substrate, catalog, and simulation tests pass.
3. Starting the Aspire app causes the kernel to activate before `start.cs` runs.
4. `start.cs` can use the supplied DigitalBrain client and produce an observable result.
5. The same activation identity does not execute the same startup script version twice.
6. Compilation and runtime failures are observable and do not terminate the kernel.
7. A dependency test confirms the kernel does not reference `DigitalBrain.Scripting`.

## Later evolution

After this slice is stable:

1. Add a generic durable `BehaviorNeuron` and behavior registry.
2. Add durable runtime subscriptions.
3. Add `Run` for typed behavior composition.
4. Restrict scripts to deterministic turns that emit commands for the runtime to execute and journal.
5. Add capability grants and production-grade process isolation.

The target pull-request flow is then:

`PullRequestOpened` is published, a repository-specific behavior subscription matches it, that behavior runs the reusable `code-review` behavior, calls an approved GitHub capability, and publishes `PullRequestReviewed`.

## Non-goals

- Replacing Aspire with a neuron.
- Loading generated assemblies into the Orleans silo.
- Generating a new grain type for every behavior.
- Adding a second fan-out concept beside `Publish`.
- Designing the complete future workflow language in the first slice.
