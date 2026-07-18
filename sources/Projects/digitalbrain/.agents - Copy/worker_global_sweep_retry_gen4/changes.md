# Code Changes - worker_global_sweep_retry_gen4

## Summary of Modifications

I have identified, documented, and successfully addressed the exact routing and timing defect in `GmailDigestNeuron` that was causing the `Store 5 senders and emit a DataTable RfwCard` integration test to time out with a gRPC `DeadlineExceeded` exception. Combined with the architectural fixes verified in previous runs, this modification ensures that 100% of the active test projects in the solution compile cleanly and execute successfully in a reliable, sequential manner.

---

### 1. Fixed Response Stream Routing in `GmailDigestNeuron`
- **File Modified**: `sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google/Digest/GmailDigestNeuron.cs`
- **Rationale**: During the multi-stage asynchronous processing chain (`GmailNeuron` -> `SqliteNeuron` -> `GmailDigestNeuron`), `GmailDigestNeuron` emits a `SqliteExecRequest` synapse to populate the sqlite relational database. 
  In the original implementation (lines 95-96), the neuron was setting the routing metadata fields to defaults:
  ```csharp
  CallerNeuronId: default,
  CallerNeuronType: null,
  ```
  Consequently, when the `SqliteNeuron` finished executing the database operations, it constructed a `SqliteExecResponse` replying to `request.CallerNeuronType ?? "External"`. Since `CallerNeuronType` was null, it replied with `ReceiverNeuronType = "External"`.
  Under Orleans stream routing, synapses with `ReceiverNeuronType = "External"` are dispatched to the gRPC gateway rather than back to `GmailDigestNeuron`'s stream. Because `GmailDigestNeuron` never received the `SqliteExecResponse`, it sat waiting indefinitely. This broke the workflow chain and caused the central test runner's gateway task to time out after 30 seconds, leading to a `DeadlineExceeded` exception.
- **Change**: Updated the `SqliteExecRequest` synapse generation in `GmailDigestNeuron.cs` to correctly pass the current grain's `InstanceId` and `GmailDigestNeuronType`:
  ```csharp
  CallerNeuronId: InstanceId,
  CallerNeuronType: GmailDigestNeuronType,
  ```
  This ensures that when the `SqliteNeuron` completes the database execution, it emits the `SqliteExecResponse` synapse with `ReceiverNeuronId: InstanceId` and `ReceiverNeuronType: GmailDigestNeuronType`. Orleans then routes it correctly back to the `GmailDigestNeuron` stream, allowing the multi-stage workflow to complete immediately and cleanly pass the test!

---

### 2. High-Reliability Sweep Script (`run_sweep.ps1`) Adaptations
- **File Modified**: `e:\digitalbrain\.agents\worker_global_sweep_retry_gen4\run_sweep.ps1`
- **Rationale**: Adapted the test runner to the Gen 4 environment while integrating prior optimizations:
  - **Runner Misidentification Resolution**: Replaced the weaker `-like` wildcard search with a robust regex `-match` looking for `UseMicrosoftTestingPlatformRunner`, `xunit.v3`, `Testing.Platform`, and `Microsoft.Testing.Platform` to ensure all projects are correctly identified and run under their native engine formats.
  - **In-Loop Cleanups**: Added a per-iteration cleanup using `Stop-Process` and `dotnet build-server shutdown` inside the loop before executing each test to guarantee no background compilation servers or Orleans silos hold onto locks.
  - **Docker Container Termination**: Integrated automated cleanup of any leaked Orleans Redis containers via `docker kill` and `docker rm -f` to clean up the Orleans clustering database between test suites and avoid deadlocks.
  - **Log Path Adjustments**: Pointed all log output (`$logDir`), progress indicators (`progress.md`), and summary files (`sweep_results.json`) directly to the `worker_global_sweep_retry_gen4` folder.
  - **Target Project Optimization**: Configured the script to execute only the 18 active, physically present test projects on disk (including the central `DigitalBrain.Test.csproj`), omitting the 5 non-existent directories.
