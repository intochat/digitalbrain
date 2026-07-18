# Handoff Report: Private Orleans Cluster & Kernel Vault Analysis

This handoff report summarizes the findings, architectural decisions, and next-steps plan for implementing Milestone 5: **Private Orleans Cluster & Kernel Vault**.

---

## 1. Observation

During our codebase investigation, we observed the following:

1. **Test-Stubs for Vault and Settings**:
   In `e:/digitalbrain/UI/BrainOS.E2E.Tests/DigitalBrainTiers.Steps.cs` (lines 206-258), the interfaces `ISecretVault` and `ISettingService` are declared locally as stubs inside the E2E test project:
   ```csharp
   public interface ISecretVault
   {
       void StoreSecret(string key, string secret);
       string GetEncryptedSecret(string key);
       string DecryptSecret(string key);
   }

   public interface ISettingService
   {
       void StoreSetting(string key, string value);
       string GetSetting(string key);
   }
   ```
   The mock `MemorySecretVault` implements them via a Base64 string prefixed with `ENC:`:
   ```csharp
   _encryptedSecrets[key] = $"ENC:{base64}";
   ```

2. **Durable Grain Settings Backing Store**:
   In `kernel/BrainOS.Kernel/Runtime/Settings/SettingsStoreGrain.cs` (lines 100-122), the `SettingsStoreGrain` handles settings storing with public (`set`) and private (`set-private`) scopes:
   ```csharp
   if (prompt.StartsWith("set ", StringComparison.Ordinal) || prompt.StartsWith("set-private ", StringComparison.Ordinal))
   {
       var isPrivate = prompt.StartsWith("set-private ", StringComparison.Ordinal);
       // ...
       settings.Add(new KernelSettingRecord(scope, key, val, isPrivate));
       await WriteStateAsync();
   ```
   It supports fetching private settings via the `get-private` command:
   ```csharp
   if (prompt.StartsWith("get-private ", StringComparison.Ordinal))
   {
       // ... retrieve setting where s.IsPrivate == true
   ```

3. **Neuron State Cryptography**:
   In `kernel/BrainOS.Core.Hosting/AddBrainOSSiloExtensions.cs` (lines 45-48), the DI container registers the `INeuronStateProtector` service:
   ```csharp
   builder.Services.AddSingleton<INeuronStateProtector>(_ =>
       RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
           ? new DpapiNeuronStateProtector()
           : new InMemoryNeuronStateProtector());
   ```
   The `DpapiNeuronStateProtector` (in `kernel/BrainOS.Core.Hosting/Security/DpapiNeuronStateProtector.cs`) uses local machine DPAPI:
   ```csharp
   public byte[] Protect(byte[] plaintext) =>
       ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
   ```

4. **User Virtual Actor & Identity**:
   - `IUserNeuron` is defined in `kernel/BrainOS.Kernel/User/IUserNeuron.cs` (lines 5-9) and implemented by `UserNeuron` in `UserNeuron.cs`.
   - `BrainOSGatewayService.cs` (lines 76-83) authenticates requests by calling `IdentityStoreGrain` in the SDK's `DigitalBrain.SDK.Identity` namespace using the key `DigitalBrain.SDK.Identity.IdentityStore`.

5. **Orleans Cluster Configuration**:
   - In the Aspire AppHost (`kernel/BrainOS.AppHost/Brainos/BrainOSResource.cs`, lines 25-29), Orleans is configured with Redis clustering and memory grain storage for development:
     ```csharp
     Redis = AppBuilder.AddRedis("orleans-redis");
     Orleans = AppBuilder.AddOrleans($"{Name}-cluster")
         .WithClustering(Redis)
     ```
   - Target silos invoke Orleans registration via `builder.AddBrainOSDomain()` in `kernel/BrainOS.ServiceDefaults/Extensions.cs` (lines 26-31), which calls `builder.AddBrainOSSilo()`.

---

## 2. Logic Chain

The step-by-step reasoning leading to our design recommendations is as follows:

1. **Separation of Contracts**:
   - `ISecretVault` deals with cryptography, security, and secrets. Grouping it under `BrainOS.Kernel.Contracts.Security` next to `INeuronStateProtector` maintains clear domain alignment (Observation 1, 3).
   - `ISettingService` deals with standard application settings. Grouping it under `BrainOS.Kernel.Contracts.Settings` next to standard settings contracts (like `RequestSetting` in `Contracts.cs`) ensures consistency.

2. **Asynchronous Production Design**:
   - In production, vault and settings operations run over Orleans clustering/grains and durable storage. To avoid sync-over-async blockings, production signatures must be fully asynchronous (`Task`) (Observation 1, 2).

3. **Reusing Core Cryptography**:
   - The E2E tests expect `ENC:<base64>` encoded patterns for encrypted vault values (Observation 1).
   - Rather than rolling a custom encryption algorithm or plain Base64 (which is not secure), we can utilize `INeuronStateProtector` (which uses secure DPAPI on Windows) to encrypt UTF-8 bytes of the secret, encode the resulting ciphertext to Base64, and prefix it with `"ENC:"`. This delivers OS-level cryptographic strength in production while perfectly satisfying the expected BDD test outputs (Observation 1, 3).

4. **Leveraging the Settings Store Grain**:
   - `SettingsStoreGrain` is a highly robust Orleans virtual actor with built-in state persistence and private scope support (`set-private`/`get-private`) (Observation 2).
   - By structuring `SettingService` and `SecretVault` as clients to `SettingsStoreGrain`, we gain free clustering, high-availability persistence (Redis or memory), and separate public settings variables from encrypted private vault secrets automatically, without introducing a redundant direct database dependency (Observation 2).

5. **Current User Scoped Context**:
   - Since `IKernelUser` is needed for profiles and lookup, declaring it in `BrainOS.Kernel.Contracts.User` allows full visibility.
   - Registering a scoped `IKernelUser` in DI allows components to inject it directly, resolving the current active user from the `RequestContext` scope or active gateway header, which fits Orleans and web gateway workflows seamlessly (Observation 4).

6. **Local Standalone Fallback**:
   - Redis clustering is perfect for cloud/multi-silo Aspire runs. But for single-user personal edge deployments, running a Redis container is heavyweight.
   - Introducing a dynamic fallback to `UseLocalhostClustering()` when Redis clustering configurations are absent enables running a zero-dependency private cluster host on a single machine (Observation 5).

---

## 3. Caveats

- **Linux DPAPI Support**: `DpapiNeuronStateProtector` is only supported on Windows. On non-Windows OS platforms, `InMemoryNeuronStateProtector` is currently registered as a pass-through. For Linux production deployments, a secure AES-key protector or certificate-based vault protector should be wired as the fallback instead of the pass-through.
- **BDD Test Stubs**: The BDD test code in `DigitalBrainTiers.Steps.cs` currently expects the synchronous `ISecretVault` and `ISettingService` stubs. During implementation, these E2E steps will need to be updated to consume the real asynchronous production contracts, or the E2E tests will continue using their sync stubs for local offline test runs while referencing the real interfaces.

---

## 4. Conclusion

We conclude that the proposed architecture cleanly unites Milestone 5 requirements. The implementation can proceed by:
1. Creating `ISecretVault.cs` and `ISettingService.cs` in `BrainOS.Kernel.Contracts`.
2. Implementing `SecretVault` and `SettingService` in `BrainOS.Kernel` (backed by `SettingsStoreGrain` and `INeuronStateProtector`).
3. Declaring `IKernelUser` in contracts and registering its scoped DI factory in `AddBrainOSSiloExtensions.cs` to resolve the authenticated user profile.
4. Adding localhost clustering fallback support for zero-dependency personal edge runs.

---

## 5. Verification Method

1. **Compile Verification**:
   Build the entire kernel workspace:
   ```powershell
   dotnet build kernel/
   ```
2. **Execute E2E Integration Suite**:
   Run the full BDD test scenarios to ensure zero regressions:
   ```powershell
   dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
   ```
3. **Inspect Output Files**:
   - Verify `ISecretVault` is declared in `BrainOS.Kernel.Contracts/Security/ISecretVault.cs`.
   - Verify `ISettingService` is declared in `BrainOS.Kernel.Contracts/Runtime/Settings/ISettingService.cs`.
