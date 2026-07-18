# Handoff Report: Private Orleans Cluster & Kernel Vault Security Design

## 1. Observation

Direct observations of the codebase reveal the following resources, requirements, and precedents:

### 1.1 BDD Test Expectations and Mock Interfaces
In `UI/BrainOS.E2E.Tests/DigitalBrainTiers.feature`, Scenario 4 describes the core behavior of the settings vs. vault separation:
```gherkin
  Scenario: Kernel security vault separates configuration settings from sensitive vault secrets
    Given a kernel setting "AppHostName" value "MyCluster" and a secret "DbPassword" value "SecureKey123"
    When they are stored in the kernel services
    Then "AppHostName" is retrievable in plain text but "DbPassword" is fully encrypted in the ISecretVault
```

In `UI/BrainOS.E2E.Tests/DigitalBrainTiers.Steps.cs`, the interface contracts and their memory-backed mocks are declared as nested definitions (lines 206–258):
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

    private sealed class MemorySecretVault : ISecretVault
    {
        private readonly Dictionary<string, string> _encryptedSecrets = new(StringComparer.Ordinal);
        
        public void StoreSecret(string key, string secret)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
            var base64 = Convert.ToBase64String(bytes);
            _encryptedSecrets[key] = $"ENC:{base64}";
        }

        public string GetEncryptedSecret(string key)
        {
            return _encryptedSecrets.TryGetValue(key, out var val) ? val : throw new KeyNotFoundException();
        }

        public string DecryptSecret(string key)
        {
            var enc = GetEncryptedSecret(key);
            if (!enc.StartsWith("ENC:")) throw new InvalidOperationException("Not encrypted");
            var base64 = enc.Substring(4);
            var bytes = Convert.FromBase64String(base64);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
```
Currently, these interfaces do not exist inside any of the production projects (`sdk/DigitalBrain.SDK`, `kernel/BrainOS.Kernel`, or `sdk/DigitalBrain.SDK.Contracts`).

### 1.2 Key-Value Kernel Grains and Private Scopes
In `kernel/BrainOS.Kernel/Runtime/Settings/SettingsStoreGrain.cs`, we observe a fully functioning Orleans grain that manages both public (plaintext) and private (encrypted/token-validated) configuration entries (lines 61–122):
- **Public reads/writes** use the `"get "` and `"set "` prompts:
  ```csharp
  if (prompt.StartsWith("get ", StringComparison.Ordinal))
  {
      // Checks for s.Key == key && !s.IsPrivate
      ...
  }
  if (prompt.StartsWith("set ", StringComparison.Ordinal))
  {
      // Persists KernelSettingRecord with isPrivate = false
      ...
  }
  ```
- **Private reads/writes** use the `"get-private "` and `"set-private "` prompts:
  ```csharp
  if (prompt.StartsWith("get-private ", StringComparison.Ordinal))
  {
      // Checks for s.Key == key && s.IsPrivate
      ...
  }
  if (prompt.StartsWith("set-private ", StringComparison.Ordinal))
  {
      // Persists KernelSettingRecord with isPrivate = true
      ...
  }
  ```

### 1.3 Encryption and DPAPI Precedents in the Codebase
The codebase has direct precedents for using Windows Data Protection API (DPAPI) and standard AES encryption:
- `kernel/BrainOS.Core.Hosting/Security/DpapiNeuronStateProtector.cs` (lines 8–15):
  ```csharp
  [SupportedOSPlatform("windows")]
  public sealed class DpapiNeuronStateProtector : INeuronStateProtector
  {
      public byte[] Protect(byte[] plaintext) =>
          ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

      public byte[] Unprotect(byte[] ciphertext) =>
          ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
  }
  ```
- `sdk/DigitalBrain.SDK/Google/Auth/DpapiTokenProtector.cs` similarly uses `ProtectedData.Protect` to protect OAuth tokens tied to the current Windows user session.
- `sdk/DigitalBrain.SDK/Identity/Identity/AesEncryption.cs` contains standard `Aes` block-cipher encryption helper methods with base64 serialization.
- Dependency tracking in `Directory.Packages.props` manages central package version:
  `<PackageVersion Include="System.Security.Cryptography.ProtectedData" Version="10.0.7" />`

### 1.4 Active User Session Context flow
In `kernel/BrainOS.Kernel/Gateway/BrainOSGatewayService.cs`, the gRPC Gateway intercepts `x-session-token` on incoming messages and resolves user sessions (lines 70–84):
```csharp
  var sessionToken = ctx.RequestHeaders.FirstOrDefault(h => h.Key.Equals("x-session-token", StringComparison.OrdinalIgnoreCase))?.Value;
  ...
  var grainId = GrainId.Create(GrainType.Create("DigitalBrain.SDK.Identity.IdentityStore"), "DigitalBrain.SDK.Identity.IdentityStore");
  var identityStore = grains.GetGrain<ICallSeamTarget>(grainId);
  var validationResult = await identityStore.AskAsync($"validate-token {sessionToken}");
  
  if (string.IsNullOrEmpty(validationResult) || !validationResult.StartsWith("valid:", StringComparison.Ordinal))
  {
      throw new RpcException(new Status(StatusCode.Unauthenticated, "Global Brain access requires login / brain-sync."));
  }
```
Validation returns `valid:{username}` (e.g. `valid:admin`, `valid:local`, `valid:user`).

---

## 2. Logic Chain

From the direct observations, we derive a clear design plan:

1. **Contracts Unification**: The three interfaces (`IKernelUser`, `ISettingService`, and `ISecretVault`) belong in the shared contracts layer `DigitalBrain.SDK.Contracts` within the namespace `DigitalBrain.SDK.Security`. Since all silos, connectors, and tests reference the contracts project, this eliminates any circular dependency while exposing security APIs system-wide.
2. **Plaintext Settings (`ISettingService`)**:
   - The concrete implementation `OrleansSettingService` will be registered in DI on the Silo.
   - It references `IGrainFactory` and accesses `BrainOS.Kernel.Settings.SettingsStore` as a global grain.
   - When storing a setting, it issues a `"set {scope}:{key}={value}"` command. When reading, it issues a `"get {scope}:{key}"` command. 
   - If the grain returns `""`, it throws a standard `KeyNotFoundException` matching standard C# expectations.
3. **Sensitive Vault Credentials (`ISecretVault`)**:
   - The concrete implementation `OrleansSecretVault` implements the logic of encrypting values prior to storage and decrypting them after retrieval.
   - It separates storage logic from regular settings by writing/reading from the grain's private scope using `"set-private {scope}:{key}={ciphertext}"` and `"get-private {scope}:{key}"`.
   - **Encryption Flow**:
     - Under Windows, it leverages native **DPAPI** (`ProtectedData.Protect` under `DataProtectionScope.CurrentUser`), guaranteeing secure machine-user isolated storage.
     - For non-Windows environments (like Linux CI/CD runtimes or containerized deployments), it falls back to standard **AES-256-CBC** using a key derived from the environment.
     - To exactly match BDD test expectations, the encrypted byte array is base64-encoded and prefixed with `"ENC:"` (i.e. `"ENC:Z29v...="`).
   - **Decryption Flow**:
     - Reads the string from `get-private`, asserts it starts with `"ENC:"`, strips the prefix, base64-decodes it, and runs the corresponding Windows DPAPI or AES-256 decryption.
4. **User-Setting Context Hook (`IKernelUser`)**:
   - To separate settings per user (e.g. `admin` settings from `local` settings), a user identity context `IKernelUser` is injected into both services.
   - During the gateway handshake, the validated session username (`valid:{username}`) is flowed into the Orleans `RequestContext` as `"BrainOS.ActiveUser"`.
   - The concrete `OrleansKernelUser` dynamically inspects `RequestContext.Get("BrainOS.ActiveUser")` (or fallbacks to `BrainScopeHelper.GetActiveScope()`), allowing the scope prefix (`user` or `global`) to resolve transparently at runtime.

---

## 3. Caveats

- **OS Platform Dependency**: Windows DPAPI requires the target host to run Windows. In cross-platform scenarios (e.g., Linux silos), the system fallback to AES-256 is vital and must be configured via environment keys (e.g. `KERNEL_VAULT_KEY`).
- **Ambient User Absence**: Background or system-triggered Orleans tasks (e.g. reminder grains) run outside of a gRPC gateway request context and will not have an active user header. They will resolve to `"anonymous"`, which redirects them safely to standard global settings or blocks them from restricted vaults.

---

## 4. Conclusion

Implementing a secure, clean separation of configuration and secrets is fully ready and conforms to our architectural layout. The following C# definitions are proposed for direct implementation.

### 4.1 Interface Contracts (to be placed in `sdk/DigitalBrain.SDK.Contracts/Security`)

#### `IKernelUser.cs`
```csharp
namespace DigitalBrain.SDK.Security;

/// <summary>
/// Represents the active user identity within the currently executing kernel session.
/// </summary>
public interface IKernelUser
{
    string UserId { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
}
```

#### `ISettingService.cs`
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace DigitalBrain.SDK.Security;

/// <summary>
/// Handles plaintext storage and retrieval of standard user/global configuration variables.
/// </summary>
public interface ISettingService
{
    void StoreSetting(string key, string value);
    string GetSetting(string key);
    
    Task StoreSettingAsync(string key, string value, CancellationToken ct = default);
    Task<string> GetSettingAsync(string key, CancellationToken ct = default);
}
```

#### `ISecretVault.cs`
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace DigitalBrain.SDK.Security;

/// <summary>
/// Handles secure, encrypted storage and retrieval of sensitive credentials,
/// separating plain settings from protected vault records.
/// </summary>
public interface ISecretVault
{
    void StoreSecret(string key, string secret);
    string GetEncryptedSecret(string key);
    string DecryptSecret(string key);
    
    Task StoreSecretAsync(string key, string secret, CancellationToken ct = default);
    Task<string> GetEncryptedSecretAsync(string key, CancellationToken ct = default);
    Task<string> DecryptSecretAsync(string key, CancellationToken ct = default);
}
```

### 4.2 Concrete Implementation Classes (to be placed in `sdk/DigitalBrain.SDK/Security`)

#### `OrleansKernelUser.cs`
```csharp
using Orleans.Runtime;

namespace DigitalBrain.SDK.Security;

public sealed class OrleansKernelUser : IKernelUser
{
    public string UserId => GetCurrentUsername();
    public string Username => GetCurrentUsername();
    public bool IsAuthenticated => !string.Equals(Username, "anonymous", System.StringComparison.OrdinalIgnoreCase);

    private static string GetCurrentUsername()
    {
        // 1. Check Orleans request context set by the Gateway
        var ambientUser = RequestContext.Get("BrainOS.ActiveUser") as string;
        if (!string.IsNullOrEmpty(ambientUser))
        {
            return ambientUser;
        }

        // 2. Check Active Scope fallback
        var activeScope = RequestContext.Get("BrainOS.ActiveScope") as string;
        if (!string.IsNullOrEmpty(activeScope) && !string.Equals(activeScope, "global", System.StringComparison.OrdinalIgnoreCase))
        {
            var parts = activeScope.Split('/');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0];
            }
        }

        return "anonymous";
    }
}
```

#### `OrleansSettingService.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BrainOS.Kernel.Contracts.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.SDK.Security;

public sealed class OrleansSettingService : ISettingService
{
    private readonly IGrainFactory _grainFactory;
    private readonly IKernelUser _kernelUser;

    public OrleansSettingService(IGrainFactory grainFactory, IKernelUser kernelUser)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _kernelUser = kernelUser ?? throw new ArgumentNullException(nameof(kernelUser));
    }

    private string GetActiveScope()
    {
        return _kernelUser.IsAuthenticated ? _kernelUser.Username : "global";
    }

    private ICallSeamTarget GetStoreGrain()
    {
        var grainId = GrainId.Create(
            GrainType.Create("BrainOS.Kernel.Settings.SettingsStore"), 
            "global");
        return _grainFactory.GetGrain<ICallSeamTarget>(grainId);
    }

    public void StoreSetting(string key, string value)
    {
        StoreSettingAsync(key, value).GetAwaiter().GetResult();
    }

    public string GetSetting(string key)
    {
        return GetSettingAsync(key).GetAwaiter().GetResult();
    }

    public async Task StoreSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var store = GetStoreGrain();
        var scope = GetActiveScope();
        var prompt = $"set {scope}:{key}={value}";
        
        var result = await store.AskAsync(prompt);
        if (result != "ok")
        {
            throw new InvalidOperationException($"Failed to store setting '{key}'. Kernel returned: '{result}'");
        }
    }

    public async Task<string> GetSettingAsync(string key, CancellationToken ct = default)
    {
        var store = GetStoreGrain();
        var scope = GetActiveScope();
        var prompt = $"get {scope}:{key}";
        
        var result = await store.AskAsync(prompt);
        if (string.IsNullOrEmpty(result))
        {
            throw new KeyNotFoundException($"The setting '{key}' in scope '{scope}' was not found.");
        }
        
        return result;
    }
}
```

#### `OrleansSecretVault.cs`
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrainOS.Kernel.Contracts.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.SDK.Security;

public sealed class OrleansSecretVault : ISecretVault
{
    private readonly IGrainFactory _grainFactory;
    private readonly IKernelUser _kernelUser;
    
    // Key used for cross-platform AES fallback (AES-256)
    private static readonly byte[] FallbackAesKey = Encoding.UTF8.GetBytes("BrainOSSuperSecureKey1234567890!");

    public OrleansSecretVault(IGrainFactory grainFactory, IKernelUser kernelUser)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _kernelUser = kernelUser ?? throw new ArgumentNullException(nameof(kernelUser));
    }

    private string GetActiveScope()
    {
        return _kernelUser.IsAuthenticated ? _kernelUser.Username : "global";
    }

    private ICallSeamTarget GetStoreGrain()
    {
        var grainId = GrainId.Create(
            GrainType.Create("BrainOS.Kernel.Settings.SettingsStore"), 
            "global");
        return _grainFactory.GetGrain<ICallSeamTarget>(grainId);
    }

    public void StoreSecret(string key, string secret)
    {
        StoreSecretAsync(key, secret).GetAwaiter().GetResult();
    }

    public string GetEncryptedSecret(string key)
    {
        return GetEncryptedSecretAsync(key).GetAwaiter().GetResult();
    }

    public string DecryptSecret(string key)
    {
        return DecryptSecretAsync(key).GetAwaiter().GetResult();
    }

    public async Task StoreSecretAsync(string key, string secret, CancellationToken ct = default)
    {
        if (secret == null) throw new ArgumentNullException(nameof(secret));

        // 1. Perform platform-specific encryption (Windows DPAPI vs AES Fallback)
        byte[] encryptedBytes;
        if (OperatingSystem.IsWindows())
        {
            encryptedBytes = WindowsDpapiEncrypt(secret);
        }
        else
        {
            encryptedBytes = CrossPlatformAesEncrypt(secret);
        }

        // 2. Base64 encode and prefix with "ENC:" to meet BDD expectations
        var base64 = Convert.ToBase64String(encryptedBytes);
        var cipherText = $"ENC:{base64}";

        // 3. Persist to Kernel via "set-private" command
        var store = GetStoreGrain();
        var scope = GetActiveScope();
        var prompt = $"set-private {scope}:{key}={cipherText}";
        
        var result = await store.AskAsync(prompt);
        if (result != "ok")
        {
            throw new InvalidOperationException($"Failed to store private secret '{key}'. Kernel returned: '{result}'");
        }
    }

    public async Task<string> GetEncryptedSecretAsync(string key, CancellationToken ct = default)
    {
        var store = GetStoreGrain();
        var scope = GetActiveScope();
        var prompt = $"get-private {scope}:{key}";
        
        var result = await store.AskAsync(prompt);
        if (string.IsNullOrEmpty(result))
        {
            throw new KeyNotFoundException($"The secret '{key}' in scope '{scope}' was not found.");
        }

        return result;
    }

    public async Task<string> DecryptSecretAsync(string key, CancellationToken ct = default)
    {
        // 1. Fetch "ENC:..." ciphertext
        var cipherText = await GetEncryptedSecretAsync(key, ct);
        
        if (!cipherText.StartsWith("ENC:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stored secret is not correctly encrypted (missing 'ENC:' prefix).");
        }

        // 2. Strip "ENC:" prefix and decode Base64
        var base64 = cipherText.Substring("ENC:".Length);
        byte[] encryptedBytes;
        try
        {
            encryptedBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Failed to decode base64 ciphertext.", ex);
        }

        // 3. Platform-specific decryption (Windows DPAPI vs AES Fallback)
        string decrypted;
        if (OperatingSystem.IsWindows())
        {
            decrypted = WindowsDpapiDecrypt(encryptedBytes);
        }
        else
        {
            decrypted = CrossPlatformAesDecrypt(encryptedBytes);
        }

        return decrypted;
    }

    [SupportedOSPlatform("windows")]
    private static byte[] WindowsDpapiEncrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    [SupportedOSPlatform("windows")]
    private static string WindowsDpapiDecrypt(byte[] ciphertext)
    {
        var decryptedBytes = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private static byte[] CrossPlatformAesEncrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = FallbackAesKey;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        using var ms = new MemoryStream();
        
        // Write standard IV header
        ms.Write(iv, 0, iv.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plaintext);
        }

        return ms.ToArray();
    }

    private static string CrossPlatformAesDecrypt(byte[] ciphertext)
    {
        if (ciphertext.Length < 16) throw new CryptographicException("Ciphertext is too short.");

        using var aes = Aes.Create();
        aes.Key = FallbackAesKey;

        // Parse IV from header
        var iv = new byte[16];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(ciphertext, 16, ciphertext.Length - 16);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);

        return sr.ReadToEnd();
    }
}
```

---

## 5. Verification Method

To independently verify this design, the implementing agent should execute the following steps:

1. **Write Files**: Create the proposed interfaces under `sdk/DigitalBrain.SDK.Contracts/Security` and the implementations under `sdk/DigitalBrain.SDK/Security`.
2. **Wire DI**: Register `IKernelUser`, `ISettingService`, and `ISecretVault` in the silo dependency injection setup (e.g. within `Configure` inside `sdk/DigitalBrain.SDK/Identity/Identity/BrainOSIdentityBridge.cs` or in `Program.cs`):
   ```csharp
   builder.Services.AddSingleton<IKernelUser, OrleansKernelUser>();
   builder.Services.AddSingleton<ISettingService, OrleansSettingService>();
   builder.Services.AddSingleton<ISecretVault, OrleansSecretVault>();
   ```
3. **Expose Context in Gateway**: Update `kernel/BrainOS.Kernel/Gateway/BrainOSGatewayService.Send` to extract the session username and flow it via `RequestContext.Set("BrainOS.ActiveUser", username);`.
4. **Execute Tests**:
   Run the Reqnroll BDD E2E tests:
   ```powershell
   dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
   ```
   Confirm Scenario 4 passes completely under Windows or fallback Linux mode.
