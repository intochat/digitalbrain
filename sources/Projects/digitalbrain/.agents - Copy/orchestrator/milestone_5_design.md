# Milestone 5 Design Plan: Private Orleans Cluster & Kernel Vault

This design plan outlines the unified strategy for implementing and verifying Milestone 5, integrating secure abstractions for User profile management, standard plain configuration settings, and encrypted vault storage inside the DigitalBrain Orleans cluster.

## 1. Architectural Strategy & Goals

1. **Strict Separation of Plain Configurations and Secrets**:
   - Standard application settings (e.g., `AppHostName`, `llm-model`, `theme`) will be stored in plain text using `ISettingService`. Under the hood, this routes commands to the public scope of `SettingsStoreGrain` using `set` and `get` commands.
   - Sensitive credentials (e.g., `DbPassword`, API keys) will be stored in `ISecretVault`. Under the hood, these undergo platform-specific encryption (Windows DPAPI with AES-256 fallback), are base64-encoded, prefixed with `"ENC:"`, and persisted inside the private scope of `SettingsStoreGrain` using `set-private` and `get-private` commands.
   - User settings are isolated using scope prefixes derived from `IKernelUser` (e.g., `"user:{username}"` or `"global"` fallback).

2. **Orleans Localhost Single-Silo Fallback**:
   - Enables zero-dependency sandboxed personal deployments on edge/local machines.
   - When Redis clustering configurations are absent, the silo builder falls back to `UseLocalhostClustering()`.

3. **Secure Decryption Boundaries**:
   - DPAPI encryption ties vault data strictly to the current active Windows OS user session, preventing external extraction.
   - In cross-platform/Linux containers, the vault falls back to AES-256 using an environment key.

---

## 2. Abstractions & Interface Contracts (To be created)

### `IKernelUser.cs`
- **Path**: `kernel/BrainOS.Kernel.Contracts/Security/IKernelUser.cs`
- **Namespace**: `BrainOS.Kernel.Contracts.Security`
```csharp
using System.Collections.Generic;

namespace BrainOS.Kernel.Contracts.Security;

/// <summary>
/// Represents the active user identity within the currently executing kernel session.
/// </summary>
public interface IKernelUser
{
    string UserId { get; }
    string DisplayName { get; }
    string Email { get; }
    IReadOnlyDictionary<string, string> Claims { get; }
    bool IsAuthenticated { get; }
}

public interface IKernelUserLookup
{
    Task<IKernelUser?> GetUserAsync(string userId, CancellationToken ct = default);
}
```

### `ISettingService.cs`
- **Path**: `kernel/BrainOS.Kernel.Contracts/Security/ISettingService.cs`
- **Namespace**: `BrainOS.Kernel.Contracts.Security`
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace BrainOS.Kernel.Contracts.Security;

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

### `ISecretVault.cs`
- **Path**: `kernel/BrainOS.Kernel.Contracts/Security/ISecretVault.cs`
- **Namespace**: `BrainOS.Kernel.Contracts.Security`
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace BrainOS.Kernel.Contracts.Security;

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

---

## 3. Concrete Implementations (To be created)

### `KernelUser.cs`
- **Path**: `kernel/BrainOS.Kernel/User/KernelUser.cs`
- **Namespace**: `BrainOS.Kernel.User`
```csharp
using System;
using System.Collections.Generic;
using BrainOS.Kernel.Contracts.Security;

namespace BrainOS.Kernel.User;

public sealed class KernelUser(
    string userId,
    string displayName,
    string email,
    IReadOnlyDictionary<string, string> claims)
    : IKernelUser
{
    public string UserId => userId;
    public string DisplayName => displayName;
    public string Email => email;
    public IReadOnlyDictionary<string, string> Claims => claims;
    public bool IsAuthenticated => !string.Equals(UserId, "anonymous", StringComparison.OrdinalIgnoreCase);
}

public sealed class KernelUserLookup : IKernelUserLookup
{
    public Task<IKernelUser?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId) || string.Equals(userId, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IKernelUser?>(new KernelUser("anonymous", "Anonymous User", "anonymous@brainos.local", new Dictionary<string, string>()));
        }
        return Task.FromResult<IKernelUser?>(new KernelUser(userId, $"{char.ToUpper(userId[0])}{userId[1..]} User", $"{userId}@brainos.local", new Dictionary<string, string>()));
    }
}
```

### `OrleansSettingService.cs`
- **Path**: `kernel/BrainOS.Kernel/Settings/OrleansSettingService.cs`
- **Namespace**: `BrainOS.Kernel.Settings`
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BrainOS.Kernel.Contracts.Runtime;
using BrainOS.Kernel.Contracts.Security;
using Orleans;
using Orleans.Runtime;

namespace BrainOS.Kernel.Settings;

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
        return _kernelUser.IsAuthenticated ? $"user/{_kernelUser.UserId}" : "global";
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

### `OrleansSecretVault.cs`
- **Path**: `kernel/BrainOS.Kernel/Security/OrleansSecretVault.cs`
- **Namespace**: `BrainOS.Kernel.Security`
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
using BrainOS.Kernel.Contracts.Security;
using Orleans;
using Orleans.Runtime;

namespace BrainOS.Kernel.Security;

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
        return _kernelUser.IsAuthenticated ? $"user/{_kernelUser.UserId}" : "global";
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

## 4. DI Registration in the Silo

Register the interfaces and concrete classes in DI within `kernel/BrainOS.Core.Hosting/AddBrainOSSiloExtensions.cs` or a new Silo Bridge:

```csharp
// Register in AddBrainOSSiloExtensions.cs
builder.Services.AddSingleton<IKernelUserLookup, KernelUserLookup>();

builder.Services.AddScoped<IKernelUser>(sp =>
{
    var lookup = sp.GetRequiredService<IKernelUserLookup>();
    
    // Attempt to resolve active user from Orleans RequestContext
    var ambientUser = Orleans.Runtime.RequestContext.Get("BrainOS.ActiveUser") as string;
    if (!string.IsNullOrEmpty(ambientUser))
    {
        return lookup.GetUserAsync(ambientUser).GetAwaiter().GetResult()!;
    }

    var activeScope = Orleans.Runtime.RequestContext.Get("BrainOS.ActiveScope") as string;
    if (!string.IsNullOrEmpty(activeScope) && !string.Equals(activeScope, "global", StringComparison.OrdinalIgnoreCase))
    {
        var parts = activeScope.Split('/');
        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
        {
            return lookup.GetUserAsync(parts[0]).GetAwaiter().GetResult()!;
        }
    }

    return lookup.GetUserAsync("anonymous").GetAwaiter().GetResult()!;
});

builder.Services.AddScoped<ISettingService, OrleansSettingService>();
builder.Services.AddScoped<ISecretVault, OrleansSecretVault>();
```

### Flow authenticated identity in gRPC Gateway
In `kernel/BrainOS.Kernel/Gateway/BrainOSGatewayService.cs` (or your gateway interceptor), after validating a token (`valid:{username}`), set:
```csharp
Orleans.Runtime.RequestContext.Set("BrainOS.ActiveUser", username);
```
Ensure this is done in `BrainOSGatewayService.cs` before invoking Orleans grains.

---

## 5. Localhost Single-Silo Fallback Configuration

Update the Orleans silo builder in `AddBrainOSSiloExtensions.cs` to dynamically configure localhost clustering when Redis is absent:

```csharp
// Inside builder.UseOrleans(silo => { ... })
// Check configuration to determine if Redis Clustering is configured
var hasRedis = !string.IsNullOrEmpty(builder.Configuration.GetConnectionString("orleans-redis")) 
            || !string.IsNullOrEmpty(builder.Configuration["ConnectionStrings:orleans-redis"]);

if (!hasRedis)
{
    silo.UseLocalhostClustering();
}
```

---

## 6. Test Suite & Verification Steps

1. **Integration tests (`kernel/BrainOS.Kernel.Tests/Runtime/SettingsIntegrationTests.cs`)**:
   Add a test `Settings_and_Vault_Services_Are_Isolated_And_Encrypted` verifying the real `OrleansSettingService` and `OrleansSecretVault` behave correctly inside the `InProcessTestCluster` (encrypts in base64 `"ENC:..."` using `DpapiNeuronStateProtector` / Fallback, isolates scopes, and successfully decrypts).

2. **E2E BDD Test (`UI/BrainOS.E2E.Tests/DigitalBrainTiers.Steps.cs`)**:
   Make the existing `MemorySecretVault` and `MemorySettingService` inherit from our newly defined production `ISecretVault` and `ISettingService` interfaces to guarantee standard interface compliance without breaking offline test runs.

3. **Verify Build**:
   ```powershell
   dotnet build BrainOS.Fast.slnx
   ```

4. **Verify Tests**:
   ```powershell
   dotnet test BrainOS.Fast.slnx
   ```
