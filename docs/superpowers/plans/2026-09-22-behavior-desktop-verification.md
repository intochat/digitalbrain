# Behavior authoring through the desktop assistant

Verified on 2026-09-22 using the native IntoChat Windows application, Computer Use, the running Aspire cluster, and separate-process tests.

## Failures reproduced and fixed

- Contract discovery was incorrectly conditional on enabling code execution. The assistant received `Module 'flutter' is not installed` although Flutter was installed. Discovery now works independently of execution policy.
- Expected tool argument failures terminated the entire conversation. Behavior tools now return repairable errors for invalid module IDs, malformed UUIDs, revision conflicts, and disabled execution.
- The catalog omitted `IDigitalBrain`, `ISignalSubscription<T>`, generic method parameters, and signal constructors. The assistant invented callback subscriptions and constructed `TimerTick` incorrectly. Those contracts are now included, with a bootstrap example verified by compilation.
- The workspace assistant always inherited the general AI default. It can now select its model through `IntoChat:Assistant:Model`; database instructions are conditional on database requests.
- Rapid check polling let the assistant conclude before compilation finished. `code_check_read` now waits up to 20 seconds for a terminal result, honoring cancellation.

## Live desktop scenarios

The assistant repaired the existing `timer-status` draft, compiled revision 6, received a check result with 2 tests passed, deployed artifact `bc5c48a100bab0a2fc360d79fd916f6eac9b162aa757f711ababf3d96fdba890`, and observed Ready. Check counts include host-supplied tests. Worker logs independently confirmed `SubscriptionReady timer/demo-timer TimerTick`.

A diagnostic Orleans client triggered one-shot ticks against the same live cluster and read `IText("demo-status")`:

| Action | Observed result |
| --- | --- |
| Tick after deployment | Empty text became `demo-timer 2026-09-22T01:54:56.0839416+00:00` |
| Native chat: stop timer-status | Persisted state Stopped, Ready false, no generation |
| Tick while stopped | Text unchanged |
| Native chat: start timer-status | New generation, Running, Ready true |
| Tick after restart | Text became `demo-timer 2026-09-22T01:56:09.1229610+00:00` |

The stop reply initially reported that shutdown was still in progress; authoritative state subsequently settled to Stopped. The restart reply verified Ready. Timer triggers and text reads were diagnostic API operations; authoring, deployment, stop, and restart requests were entered through the native chat UI.

A fresh conversation requested `timer-proof`, using time and flutter, to copy the timer ID into `demo-proof`. Its first draft compiled and passed 3 tests but incorrectly resolved the timer through `Get<INeuron>`; the worker failed because that base interface maps to many grain types. The UI correctly reported failure and Ready false. A follow-up, "read worker logs and repair timer-proof until deployed and ready", caused the assistant to read logs and replace the base interface with `DigitalBrain.Time.Timers.ITimer`. No source patch was supplied through chat. Runtime repair required that follow-up; automatic recovery from every generated-code error is not established by these checks.

The repaired revision 2 passed all 3 check tests, deployed artifact `842b66d5289987d35d8aaa995a6fc5805d036d43a7f52fc327364eb11890d5af`, and reached Running/Ready with generation `bd251136-7ddd-4227-9be3-d4837eeb1860`. A live one-shot timer trigger changed `demo-proof` from empty text to exactly `demo-timer`. Both test behaviors were left Ready; the demo timer is one-shot, not periodic.

## Aspire evidence

- Original missing-module failure: `a005868d3b1548f358b2628782d17cae`.
- Malformed tool operation identity: `f513578175b69038f94b7d4b693e2d80`.
- Incorrect database-tool selection by the previous local model: `b636337aff9744c19a919535ccbe170f`.
- Successful repair/deploy conversation: `ec5125dd284d393046261db01efb9e5e`; trace-correlated logs show the request completing and conversation state persisting.
- Stop: `692e60f124d532a3bb063f60f73671a4`; restart: `46325075ee8c2f7647841fac481b1cff`.
- Fresh behavior with correctly reported runtime failure: `97e21f0a7797980f105ee323ca6f2383`.

Earlier traces belonged to previous AppHost sessions. Restarting the AppHost replaces dashboard history. No credentials are included here.

## Automated verification

- Coding unit suite: 52 passed, 0 failed.
- IntoChat unit suite: 22 passed, 0 failed, including actual agent-loop recovery, model selection, and pending-check waiting.
- Behavior E2E suite: 1 scenario passed, covering compile/test, timer effects, update, stop, rollback, compile failure, normal completion, readiness timeout, worker crash, termination on host loss, and recovery. The repeat run completed in 1m13s. This suite uses a scripted model; live provider behavior was checked separately through the desktop.
- `git diff --check` passed.

## Local configuration

AppHost user secrets now configure `IntoChat:Assistant:Model=IGpt56Luna`, `IntoChat:BehaviorAuthoring:AllowActivation=true`, and `IntoChat:BehaviorAuthoring:Root=E:\intochat\digitalbrain\.digitalbrain\behavior-runtime`. The general AI default is unchanged. These settings and generated artifacts are local, not committed configuration.
