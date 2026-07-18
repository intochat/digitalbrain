# DigitalBrain.Abstractions

Provider-neutral contracts for the DigitalBrain framework.

This package contains the types shared by every DigitalBrain application and kernel:

- `INeuron` — the Orleans grain marker every DigitalBrain capability interface extends.
- `BrainOwnerId` — the authenticated owner identity used as the grain key.
- `ExternalOperation` and `ExternalOperationTransitions` — the durable external-operation ledger records and their typed state machine.
- `NeuronNotification` — the durable notification outbox record.
- `BrainException` and `NeuronFailureKind` — typed operational failures.

It has no provider SDK, storage, or hosting dependencies. Applications normally consume it transitively through `DigitalBrain.Client`; kernels consume it through `DigitalBrain.Kernel`.
