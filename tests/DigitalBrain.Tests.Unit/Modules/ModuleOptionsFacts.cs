using DigitalBrain.Modules.Configuration;
using DigitalBrain.Modules.Options;
using DigitalBrain.Modules.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace DigitalBrain.Tests.Unit.Modules;

public sealed class ModuleOptionsFacts
{
    [Fact]
    public void GetModuleOptionsBindsAndValidates()
    {
        var previous = ModuleConfigurationOptions.Current;
        try
        {
            ModuleConfigurationOptions.Current = new();

            var empty = new InMemoryConfiguration(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
            var failure = Assert.Throws<ModuleValidationException>(() => empty.GetModuleOptions<SampleOptions>("sample"));
            var issue = Assert.Single(failure.Issues);
            Assert.Equal("DigitalBrain:Modules:sample:Options:Endpoint", issue.Path);
            Assert.Equal("required", issue.Code);

            var configured = new InMemoryConfiguration(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DigitalBrain:Modules:sample:Options:Endpoint"] = "https://example.test/api",
            });
            var bound = configured.GetModuleOptions<SampleOptions>("sample");
            Assert.Equal("https://example.test/api", bound.Endpoint);
            Assert.True(bound.Validated);
        }
        finally
        {
            ModuleConfigurationOptions.Current = previous;
        }
    }

    [Fact]
    public void IsCredentialNameFlagsSecretLikeNames()
    {
        Assert.True(ModuleSettingsValidation.IsCredentialName("ApiKey"));
        Assert.True(ModuleSettingsValidation.IsCredentialName("ClientSecret"));
        Assert.False(ModuleSettingsValidation.IsCredentialName("BaseUrl"));
    }

    private sealed class SampleOptions : IModuleOptions
    {
        [Setting(Required = true)]
        public string? Endpoint { get; set; }

        public bool Validated { get; private set; }

        public void Validate() => Validated = true;
    }

    private sealed class InMemoryConfiguration(IReadOnlyDictionary<string, string?> values, string path = "", string key = "")
        : IConfiguration, IConfigurationSection
    {
        public string? this[string childKey]
        {
            get => values.TryGetValue(Combine(path, childKey), out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public string Key => key;

        public string Path => path;

        public string? Value
        {
            get => values.TryGetValue(path, out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren()
        {
            var prefix = path.Length == 0 ? string.Empty : path + ":";
            return values.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.Length > prefix.Length)
                .Select(k =>
                {
                    var rest = k[prefix.Length..];
                    var colon = rest.IndexOf(':', StringComparison.Ordinal);
                    return colon < 0 ? rest : rest[..colon];
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(GetSection);
        }

        public IChangeToken GetReloadToken() => NullChangeToken.Instance;

        public IConfigurationSection GetSection(string sectionKey)
        {
            var full = Combine(path, sectionKey);
            var colon = sectionKey.LastIndexOf(':');
            var leaf = colon < 0 ? sectionKey : sectionKey[(colon + 1)..];
            return new InMemoryConfiguration(values, full, leaf);
        }

        private static string Combine(string prefix, string suffix)
            => prefix.Length == 0 ? suffix : $"{prefix}:{suffix}";
    }

    private sealed class NullChangeToken : IChangeToken
    {
        public static readonly NullChangeToken Instance = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
