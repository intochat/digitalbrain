using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Testing;

internal sealed class MethodScopedConfigurationSource(
    MethodScopedConfigurationProvider provider) :
    IConfigurationSource
{
    public IConfigurationProvider Build(
        IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return provider;
    }
}

internal sealed class MethodScopedConfigurationProvider :
    ConfigurationProvider
{
    private readonly Lock _gate = new();

    public override IEnumerable<string> GetChildKeys(
        IEnumerable<string> earlierKeys,
        string? parentPath)
    {
        ArgumentNullException.ThrowIfNull(earlierKeys);

        lock (_gate)
        {
            return base
                .GetChildKeys(earlierKeys, parentPath)
                .ToArray();
        }
    }

    public override void Set(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            if (value is null)
            {
                Data.Remove(key);
            }
            else
            {
                Data[key] = value;
            }
        }

        OnReload();
    }

    public override bool TryGet(
        string key,
        out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            return base.TryGet(key, out value);
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            Data.Clear();
        }

        OnReload();
    }
}
