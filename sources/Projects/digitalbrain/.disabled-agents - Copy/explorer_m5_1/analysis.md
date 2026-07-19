# Milestone 5 Analysis: Private Orleans Cluster & Kernel Vault

This analysis covers the design and implementation specifications for Milestone 5 of **DigitalBrain**, detailing the architectural integration of `ISecretVault`, `ISettingService`, and `IKernelUser` in a single-user private Orleans cluster deployment.

---

## 1. Interface Declaration in Core Codebase

To ensure compile-time isolation and clear separation of concerns, we recommend declaring the contracts inside the existing **`BrainOS.Kernel.Contracts`** assembly.

### `ISecretVault`
- **Location**: `kernel/BrainOS.Kernel.Contracts/Security/ISecretVault.cs`
- **Namespace**: `BrainOS.Kernel.Contracts.Security`
- **Design Decisions**:
  - Placed alongside `INeuronStateProtector.cs` to group all cryptographic and secure credentials boundaries together.
  - Fully asynchronous signature to avoid blocking Orleans grain execution threads (sync-over-async anti-pattern).

```csharp
namespace BrainOS.Kernel.Contracts.Security;

public interface ISecretVault
{
    Task StoreSecretAsync(string key, string secret, CancellationToken ct = default);
    Task<string> GetEncryptedSecretAsync(string key, CancellationToken ct = default);
    Task<string> DecryptSecretAsync(string key, CancellationToken ct = default);
}
```

### `ISettingService`
- **Location**: `kernel/BrainOS.Kernel.Contracts/Runtime/Settings/ISettingService.cs`
- **Namespace**: `BrainOS.Kernel.Contracts.Runtime` or `BrainOS.Kernel.Contracts.Settings`
- **Design Decisions**:
  - Located in the Settings folder alongside messages such as `RequestSetting` and `UpdateSetting`.
  - Fully asynchronous to match enterprise database persistence patterns.

```csharp
namespace BrainOS.Kernel.Contracts.Settings;

public interface ISettingService
{
    Task StoreSettingAsync(string key, string value, CancellationToken ct = default);
    Task<string> GetSettingAsync(string key, CancellationToken ct = default);
}
```

---

## 2. Production Implementations

The production implementations will live in the **`BrainOS.Kernel`** assembly.

- **`SettingService`**: `kernel/BrainOS.Kernel/Settings/SettingService.cs` (Namespace: `BrainOS.Kernel.Settings`)
- **`SecretVault`**: `kernel/BrainOS.Kernel/Security/SecretVault.cs` (Namespace: `BrainOS.Kernel.Security`)

### Strategy: Leverage Virtual Actors
Instead of introducing direct database dependencies, the production services will interface directly with the Orleans cluster using the virtual actor **`SettingsStoreGrain`** (which is already configured with durable grain state persistence, using memory in test runs or Redis in production).

- We obtain a grain reference to the global settings store:
  ```csharp
  var grainId = GrainId.Create(GrainType.Create("BrainOS.Kernel.Settings.SettingsStore"), "global");
  var settingsStore = grainFactory.GetGrain<ICallSeamTarget>(grainId);
  ```
- **`SettingService`** routes standard key-value configuration variables through the public settings path using the `get` and `set` commands:
  ```csharp
  // Set
  await settingsStore.AskAsync($"set global:{key}={value}");
  // Get
  return await settingsStore.AskAsync($"get global:{key}");
  ```
- **`SecretVault`** routes encrypted values using private settings paths using `get-private` and `set-private` commands:
  ```csharp
  // Set Private
  await settingsStore.AskAsync($"set-private global:{key}={encryptedValue}");
  // Get Private
  return await settingsStore.AskAsync($"get-private global:{key}");
  ```

---

## 3. Cryptographically Robust Encryption for the Vault

To keep the system lightweight and transparently satisfy the BDD scenario test assertion (which requires the stored string to have the `"ENC:<base64>"` pattern), we recommend **reusing the existing `INeuronStateProtector` abstraction** under the hood.

### The Mechanism
1. **DI Resolution**: `INeuronStateProtector` is already configured in the silo:
   - On Windows, it maps to `DpapiNeuronStateProtector`, which utilizes Windows DPAPI (`ProtectedData.Protect` tied securely to the Windows user context).
   - On non-Windows OS, it maps to `InMemoryNeuronStateProtector` (or a secure AES-fallback protector).
2. **Encryption Process (`StoreSecretAsync`)**:
   - Convert plaintext `secret` to UTF-8 bytes.
   - Encrypt via `INeuronStateProtector.Protect(bytes)` to get ciphertext.
   - Convert ciphertext to Base64: `Convert.ToBase64String(ciphertext)`.
   - Store as `"ENC:{base64}"` in the backing setting store.
3. **Decryption Process (`DecryptSecretAsync`)**:
   - Strip `"ENC:"` prefix from the fetched string.
   - Decode Base64 back to ciphertext bytes.
   - Decrypt via `INeuronStateProtector.Unprotect(ciphertext)`.
   - Convert UTF-8 bytes back to plaintext.

### Rationale
- **100% Production-Ready**: On Windows personal deployments, DPAPI secures the secrets natively at the OS-level without needing complex external vault configurations.
- **100% E2E Compliant**: Generates standard Base64 formatted strings prefixed with `"ENC:"`, aligning perfectly with current and future test specifications.

---

## 4. User Profile Lookup & `IKernelUser`

The user profile lookup and session management are handled in two layers:
1. **`IdentityStoreGrain`** in the unified `DigitalBrain.SDK.Identity` namespace, which manages tokens (`validate-token`, `get-token`), locks, and spawns databases.
2. **`UserNeuron`** (`kernel/user`) representing the virtual actor for user prompts and conversations.

### Structure of `IKernelUser`
We declare `IKernelUser` in `BrainOS.Kernel.Contracts.User` namespace:

```csharp
namespace BrainOS.Kernel.Contracts.User;

public interface IKernelUser
{
    string UserId { get; }
    string DisplayName { get; }
    string Email { get; }
    IReadOnlyDictionary<string, string> Claims { get; }
}

public interface IKernelUserLookup
{
    Task<IKernelUser?> GetUserAsync(string userId, CancellationToken ct = default);
}
```

### Structuring and DI Integration
1. **`KernelUser` & `KernelUserLookup`**:
   - Implement `KernelUser` class and `KernelUserLookup` in `BrainOS.Kernel.User`.
   - `KernelUserLookup` queries the identity store or returns standard deployment profiles (e.g. `admin`, `local`, `user`).
2. **Scoped User Resolution**:
   Register a scoped `IKernelUser` in the DI container. The resolution logic extracts the active user ID from the Orleans `RequestContext` (which holds correlation and session metadata) or falling back to the active gateway scope:
   ```csharp
   builder.Services.AddScoped<IKernelUser>(sp =>
   {
       var lookup = sp.GetRequiredService<IKernelUserLookup>();
       // Extract UserId from RequestContext or active scope:
       var activeScope = Orleans.Runtime.RequestContext.Get(BrainScopeHelper.ActiveScopeKey) as string 
                         ?? "local";
       var userId = activeScope.Replace("user/", "");
       
       return lookup.GetUserAsync(userId).GetAwaiter().GetResult() 
              ?? new KernelUser(userId, "Guest User", "guest@brainos.local", new Dictionary<string, string>());
   });
   ```

---

## 5. Orleans Cluster Configuration for Single-User Personal Deployment

In production, Orleans is orchestrated via .NET Aspire and configured with **Redis Clustering**:
```csharp
Orleans = AppBuilder.AddOrleans($"{Name}-cluster")
    .WithClustering(Redis)
```

However, a single-user personal deployment (e.g. running on local desktop or low-powered edge devices) should not require running a Docker Redis container continuously.

### Localhost Single-Silo Fallback
We recommend adding a dynamic clustering selection inside the Silo builder in `AddBrainOSSiloExtensions.cs`:
- If the Redis connection string or the Aspire-injected Orleans configuration is missing/invalid, fallback automatically to **`UseLocalhostClustering()`**:
```csharp
silo.Services.AddSingleton<IClusterClient>(sp => ...);
// inside builder.UseOrleans:
if (IsClusteringRedisConfigured(builder.Configuration))
{
    // Handled automatically by Aspire WithReference
}
else
{
    silo.UseLocalhostClustering();
}
```
This enables zero-dependency single-silo clustering for personal local sandboxes.

---

## 6. Comprehensive Implementation Plan

```
Step 1: Declare Contracts in BrainOS.Kernel.Contracts
  ├── Security/ISecretVault.cs
  ├── Runtime/Settings/ISettingService.cs
  └── User/IKernelUser.cs & IKernelUserLookup.cs

Step 2: Add Real Implementations in BrainOS.Kernel
  ├── Security/SecretVault.cs (wraps SettingsStoreGrain + INeuronStateProtector)
  ├── Settings/SettingService.cs (wraps SettingsStoreGrain)
  └── User/KernelUser.cs & KernelUserLookup.cs

Step 3: Wire DI Registrations in e:/digitalbrain/kernel/BrainOS.Core.Hosting/AddBrainOSSiloExtensions.cs
  ├── Register ISettingService -> SettingService
  ├── Register ISecretVault -> SecretVault
  ├── Register IKernelUserLookup -> KernelUserLookup
  └── Register Scoped IKernelUser (resolved from RequestContext)

Step 4: Update E2E test file e:/digitalbrain/UI/BrainOS.E2E.Tests/DigitalBrainTiers.Steps.cs
  ├── Reference real contracts from BrainOS.Kernel.Contracts
  └── Retain Memory implementations in steps file for isolated/un-hosted scenario runs
```
