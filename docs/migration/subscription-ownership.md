# Subscription ownership and Orleans guidance

Follow-up to the foundation pilot, 2026-09-19. Public usage remains
`await brain.SubscribeAsync<T>(source, ct)`, followed by `ReadAllAsync`, `Completion`, and disposal.

## What changed

`BrainClient` creates subscriptions and keeps strong references until cleanup finishes.
`SignalSubscription<T>` implements `INeuronObserver` explicitly and owns its connection, renewal,
buffer and cleanup. `ConnectAsync` awaits the actual registration before the factory returns.
There is no readiness TaskCompletionSource, Registered notification, Start callback or separate Catcher.

One terminal transition completes the buffer and public Completion with the same outcome; the first
outcome wins. Renewal failure is visible before remote cleanup finishes. Disposal joins renewal and
one shared cleanup task. Late registration is still observed and removed after it completes, even if
immediate Unwatch ran first. Cleanup never waits indefinitely for the pending registration.

This simplifies ownership rather than minimizing the length of SignalSubscription.cs: transport code
previously inside BrainClient now lives with the subscription. Necessary lifetime protection remains
explicit. No replacement subscription abstraction or additional package was introduced.

## Orleans findings

Context7 resolution was attempted, but its monthly quota was exhausted. Checked Microsoft documentation
and compiled against the repository's pinned Orleans 10.3.1 packages.

| Option | Finding and decision |
| --- | --- |
| Client observers | Fit live notifications to external C# clients. Retained. |
| ObserverManager | Already handles server membership expiry. Clients still need renewal and explicit reference cleanup. |
| Broadcast channels | The documented consumers are implicitly subscribed grains. Not a direct replacement for these dynamic client subscriptions. |
| Orleans Streams | Support pub/sub with explicit stream-provider configuration. Useful if the product later needs those semantics; unnecessary for the approved live-only observer contract. |
| Remote IAsyncEnumerable | Supported for grain-to-caller response streaming. A promising larger alternative, but would require redesigning the eager readiness protocol and testing buffering/backpressure, cancellation and activation loss. Not silently substituted in this refactor. |

The observer instance must remain strongly reachable: Orleans holds it weakly. The client's owned set
now retains the subscription itself, so a separate catcher and GC.KeepAlive are unnecessary. Observer
callbacks return Task and only enqueue; behavior work runs in the consumer loop. Orleans serializes
calls to each observer, but cancellation/disposal can run concurrently, so local terminal transitions
remain synchronized.

Microsoft also suggests considering OneWay for best-effort notifications. We retain the existing
request/response callback: changing it would remove the callback acknowledgement from publication.
That is a delivery-semantics choice, not a prerequisite for simplifying ownership.

## Evidence

- A new regression first failed because renewal failure was hidden behind held Unwatch; it now passes.
- Client disposal during held registration is covered, including cleanup after the late Watch succeeds.
- Overflow remains the terminal error after disposal, for both Completion and the reader.
- Existing cancellation, renewal, activation-change, readiness, persistence, actual reminder and
  separate-process behavior tests remain part of the foundation suite.

Sources:

- [Client observers](https://learn.microsoft.com/en-us/dotnet/orleans/grains/observers)
- [Broadcast channels](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/broadcast-channel)
- [Streams configuration](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/streams-quick-start)
- [IAsyncEnumerable grain methods](https://learn.microsoft.com/en-us/dotnet/orleans/grains/#iasyncenumerable-return-values)
