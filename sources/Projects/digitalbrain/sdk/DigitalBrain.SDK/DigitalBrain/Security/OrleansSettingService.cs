using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Security;

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

    private ICallNeuronTarget GetStoreGrain()
    {
        var grainId = GrainId.Create(
            GrainType.Create("DigitalBrain.Kernel.Settings.SettingsStore"), 
            "global");
        return _grainFactory.GetGrain<ICallNeuronTarget>(grainId);
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
