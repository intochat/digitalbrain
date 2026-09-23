# DigitalBrain

A durable actor runtime on [Orleans](https://learn.microsoft.com/dotnet/orleans/), composed and hosted
with [Aspire](https://aspire.dev). The sentence that settles naming: **a neuron publishes a signal; a
client subscribes to the neuron and reads that stream**.

## Runtime

**Neuron**
A durable Orleans grain. `Neuron : Grain, INeuron` accepts observers with `Watch(INeuronObserver)` /
`Unwatch`, holds them for its activation lease, and publishes typed signals. `Neuron<TState>` persists
`TState` through `IPersistentState<TState>` and saves state and signals together in `Save`.
_Avoid_: agent, service (product language)

**Signal**
A typed, immutable message. `Signal` is the base record; concrete signals are Orleans-serializable
records published by a neuron. Delivery carries no routing of its own beyond the source neuron.
_Avoid_: event, bus message

**Subscription**
`IDigitalBrain.SubscribeAsync<TSignal>(INeuron source)` returns `ISignalSubscription<TSignal>`, a
bounded `IAsyncEnumerable<TSignal>`. In process it joins `LocalSignalHub`; out of process it registers
an `INeuronObserver` under a renewable lease and fails when the source reactivates, because live
signals may have been missed. A subscription has one reader; overflow fails it rather than blocking.
_Avoid_: message queue, journal

**IDigitalBrain**
The owner's typed handle: `Get<T>(id) where T : IGrainWithStringKey` and `SubscribeAsync<T>(source)`.
`BrainClient` implements it over `IGrainFactory`; hosts register it with `AddDigitalBrain()`.
_Avoid_: Orleans client (product language)

## Modules

**Module**
An `IModule` configures a silo and its HTTP endpoints. A module declares typed options with
`[ModuleConfiguration(typeof(...))]` and a `ModuleConfigurationContract<TModule, TOptions>`.
`AddDigitalBrain()` builds the Orleans host; `WithModule<T>()` composes modules in code; the runtime
loads the composed list from `DigitalBrain:Modules` (`TypeFullName, AssemblyName`). The AppHost writes
that list as `DigitalBrain__Modules__N`, and the container images pin the same list.

Modules: AI; Memory; ClickHouse; Supabase; Time; Google (Gmail); Salesforce; Microsoft (Aspire,
GitHub, Roslyn, DotNet); Coding; Behavior; Flutter.

## Behavior

**Behavior program**
`IBehaviorProgram`, implemented by `BehaviorProgramNeuron`, deploys saved code artifacts and owns
their lifecycle: `Deploy`, `Start`, `Stop`, `Rollback`, `Read` and `ReadLogs`. Deployment verifies the
artifact before use; revisions and logs are persisted; changes publish deployment, execution and log
signals.

**Behavior runtime**
`IBehavior` is a program run outside the silo by `BehaviorApp.RunAsync<TBehavior>`. It connects an
Orleans client, subscribes through a scoped `IDigitalBrain`, reports readiness over a control pipe,
and stops when a required subscription closes.

**Script**
C# authored against module contracts. Saving a script creates a draft and artifact; deploying and
starting it is a behavior-program lifecycle action.
_Avoid_: plugin, capability

## Providers

**IAgent**
The assistant runtime: `Ask`, definition and capability configuration, streaming responses, history,
usage and an event log. It is an `INeuron`.

Provider neurons — `IGmail`, `ISalesforce`, `IClickHouse`, `IRepository` (GitHub) and the live-table
neuron — are `INeuron` grains that expose their provider operations and publish domain signals such
as `TableRendered`.

The SDK supplies shared host helpers: HTTP surfaces, JSON webhooks and browser login.
