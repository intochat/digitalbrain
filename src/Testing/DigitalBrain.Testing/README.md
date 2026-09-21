# DigitalBrain.Testing

Shared primitives for every DigitalBrain test kind: bounded signal probes, tracked behavior runs,
polling waits and a session lifetime that releases owned resources in reverse order.

Referenced transitively by `DigitalBrain.Testing.Unit` and `DigitalBrain.Testing.E2E`; you normally
install one of those instead.

```csharp
await using var ticks = await brain.Observe<TimerTick>(timer, ct);
await timer.Start(TimeSpan.Zero);
Assert.Equal("tea", (await ticks.NextAsync(ct: ct)).TimerId);
```

Budgets come from `TestExecutionOptions`: startup 3 minutes, assertions 5 seconds, cleanup 30 seconds.
