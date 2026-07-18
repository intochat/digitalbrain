namespace DigitalBrain.Runtime.Neurons.State;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NeuronSettingAttribute(string key, string scope = "", bool isPrivate = false) : Attribute, IFacetMetadata
{
    public string Key { get; } = key;
    public string Scope { get; } = scope;
    public bool IsPrivate { get; } = isPrivate;
}

public static class NeuronSettingResolver
{
    public static string Resolve(IConfiguration? config, NeuronSettingAttribute metadata)
    {
        var key = metadata.Key;
        var scope = metadata.Scope;

        if (config is not null)
        {
            var lookupKeys = new List<string>();

            if (metadata.IsPrivate)
            {
                lookupKeys.Add($"Parameters:{key}");
                lookupKeys.Add($"DigitalBrain:Secrets:{key}");
                lookupKeys.Add($"Secrets:{key}");
            }
            else
            {
                lookupKeys.Add($"DigitalBrain:Settings:{key}");
            }

            if (!string.IsNullOrEmpty(scope))
            {
                if (metadata.IsPrivate)
                {
                    lookupKeys.Add($"Parameters:{scope}:{key}");
                    lookupKeys.Add($"DigitalBrain:Secrets:{scope}:{key}");
                }
                else
                {
                    lookupKeys.Add($"DigitalBrain:Settings:{scope}:{key}");
                }
            }

            lookupKeys.Add(key);
            lookupKeys.Add($"DigitalBrain:{key}");

            foreach (var lookupKey in lookupKeys)
            {
                var val = config[lookupKey];
                if (val is not null) return val;
            }
        }

        // Default fallbacks if not configured in IConfiguration
        if (!metadata.IsPrivate)
        {
            return key switch
            {
                "terms-version" => "2026-05-19",
                "llm-model" => "openai-gpt-5",
                "llm-temperature" => "0.7",
                "llm-attempts" => "3",
                "theme" => "dark",
                "default-scope" => "global",
                _ => ""
            };
        }

        return "";
    }
}

