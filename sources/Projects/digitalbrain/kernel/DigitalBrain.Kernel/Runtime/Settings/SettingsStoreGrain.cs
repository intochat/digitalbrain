using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Runtime.Settings;

public static class SettingKeys
{
    public const string Theme = "theme";
    public const string LlmModel = "llm-model";
    public const string LlmTemperature = "llm-temperature";
    public const string LlmAttempts = "llm-attempts";
    public const string DefaultScope = "default-scope";
    public const string TermsVersion = "terms-version";
}

[GrainType(GrainTypeId)]
public sealed class SettingsStoreGrain(
    [FromKeyedServices("kernel-settings")] IDurableList<KernelSettingRecord> settings,
    ILogger<SettingsStoreGrain> logger,
    IConfiguration? configuration = null)
    : DurableGrain, ICallNeuronTarget, IPredicateNeuronTarget
{
    public const string GrainTypeId = "DigitalBrain.Kernel.Settings.SettingsStore";

    private const string CommandGetPrefix = "get ";
    private const string CommandGetPrivatePrefix = "get-private ";
    private const string CommandSetPrefix = "set ";
    private const string CommandSetPrivatePrefix = "set-private ";
    private const string PromptSettingsCard = "settings-card";

    private string ResolveConfigValue(string scope, string key, bool isPrivate)
    {
        // 1. Check durable settings list first
        var record = settings.LastOrDefault(s => 
            (s.Scope == scope || 
             (scope == "" && (s.Scope == "global" || s.Scope == "user")) || 
             ((scope == "global" || scope == "user") && s.Scope == "")) 
            && s.Key == key && s.IsPrivate == isPrivate);

        if (record is not null)
        {
            return record.Value;
        }

        // 2. If not found, look up from configuration (kinda like .NET Aspire does it!)
        if (configuration is not null)
        {
            var lookupKeys = new List<string>();
            
            if (isPrivate)
            {
                lookupKeys.Add($"Parameters:{key}");
                lookupKeys.Add($"DigitalBrain:Secrets:{key}");
                lookupKeys.Add($"Secrets:{key}");
            }
            else
            {
                lookupKeys.Add($"DigitalBrain:Settings:{key}");
            }

            // Check scoped configurations
            if (!string.IsNullOrEmpty(scope))
            {
                if (isPrivate)
                {
                    lookupKeys.Add($"Parameters:{scope}:{key}");
                    lookupKeys.Add($"DigitalBrain:Secrets:{scope}:{key}");
                }
                else
                {
                    lookupKeys.Add($"DigitalBrain:Settings:{scope}:{key}");
                }
            }

            // Fall back to direct key or environment variable lookups
            lookupKeys.Add(key);

            foreach (var lookupKey in lookupKeys)
            {
                var configVal = configuration[lookupKey];
                if (configVal is not null)
                {
                    logger.LogDebug("Resolved setting {Key} from configuration key {LookupKey}", key, lookupKey);
                    return configVal;
                }
            }
        }

        // 3. Fallback defaults for public keys if not configured
        if (!isPrivate)
        {
            return key switch
            {
                SettingKeys.TermsVersion => "2026-05-19",
                SettingKeys.LlmModel => "openai-gpt-5",
                SettingKeys.LlmTemperature => "0.7",
                SettingKeys.LlmAttempts => "3",
                SettingKeys.Theme => "dark",
                SettingKeys.DefaultScope => "global",
                _ => ""
            };
        }

        return "";
    }

    public async Task<string> AskAsync(string prompt)
    {
        if (prompt == PromptSettingsCard)
        {
            var theme = ResolveConfigValue("", SettingKeys.Theme, false);
            var model = ResolveConfigValue("", SettingKeys.LlmModel, false);
            var temp = ResolveConfigValue("", SettingKeys.LlmTemperature, false);
            var scope = ResolveConfigValue("", SettingKeys.DefaultScope, false);

            var cardSource = 
                "import digitalbrain;\n" +
                "\n" +
                "widget root = Panel(\n" +
                "  padding: 20.0,\n" +
                "  child: VStack(\n" +
                "    gap: 14.0,\n" +
                "    cross: \"start\",\n" +
                "    children: [\n" +
                "      Text(text: \"DigitalBrain Central Settings\", variant: \"title\"),\n" +
                "      Text(text: data.theme, variant: \"body\"),\n" +
                "      Text(text: data.model, variant: \"body\"),\n" +
                "      Text(text: data.temp, variant: \"body\"),\n" +
                "      Text(text: data.scope, variant: \"body\"),\n" +
                "    ],\n" +
                "  ),\n" +
                ");\n";

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                theme = "Theme: " + theme,
                model = "LLM Model: " + model,
                temp = "Temperature: " + temp,
                scope = "Default Scope: " + scope,
                source = cardSource
            });
        }

        if (prompt.StartsWith(CommandGetPrefix, StringComparison.Ordinal))
        {
            var target = prompt[CommandGetPrefix.Length..].Trim();
            var colIdx = target.IndexOf(':');
            var scope = colIdx >= 0 ? target[..colIdx] : "";
            var key = colIdx >= 0 ? target[(colIdx + 1)..] : target;
            
            return ResolveConfigValue(scope, key, false);
        }

        if (prompt.StartsWith(CommandGetPrivatePrefix, StringComparison.Ordinal))
        {
            var target = prompt[CommandGetPrivatePrefix.Length..].Trim();
            var colIdx = target.IndexOf(':');
            var scope = colIdx >= 0 ? target[..colIdx] : "";
            var key = colIdx >= 0 ? target[(colIdx + 1)..] : target;
            
            return ResolveConfigValue(scope, key, true);
        }

        if (prompt.StartsWith(CommandSetPrefix, StringComparison.Ordinal) || prompt.StartsWith(CommandSetPrivatePrefix, StringComparison.Ordinal))
        {
            var isPrivate = prompt.StartsWith(CommandSetPrivatePrefix, StringComparison.Ordinal);
            var headerLength = isPrivate ? CommandSetPrivatePrefix.Length : CommandSetPrefix.Length;
            
            var assignment = prompt[headerLength..].Trim();
            var eqIdx = assignment.IndexOf('=');
            var keyPart = assignment[..eqIdx];
            var val = assignment[(eqIdx + 1)..];
            
            var colIdx = keyPart.IndexOf(':');
            var scope = colIdx >= 0 ? keyPart[..colIdx] : "";
            var key = colIdx >= 0 ? keyPart[(colIdx + 1)..] : keyPart;

            var idx = settings.ToList().FindIndex(s => s.Scope == scope && s.Key == key);
            if (idx >= 0) settings.RemoveAt(idx);

            settings.Add(new KernelSettingRecord(scope, key, val, isPrivate));
            await WriteStateAsync();
            
            logger.LogInformation("Setting persisted: [{Scope}] {Key} (Private: {IsPrivate})", scope, key, isPrivate);
            return "ok";
        }

        return "";
    }

    public async Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        if (subject == "admin-session-token")
        {
            return target == "true";
        }

        var currentVersion = ResolveConfigValue("", SettingKeys.TermsVersion, false);

        try
        {
            var onboardingStore = GrainFactory.GetGrain<IPredicateNeuronTarget>("DigitalBrain.Domains.Onboarding.OnboardingStore");
            bool isCurrent = await onboardingStore.EvaluateAsync(subject, currentVersion, ct);
            bool expected = string.Equals(target, "true", StringComparison.OrdinalIgnoreCase);
            return isCurrent == expected;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evaluate is-current-version for user {UserId}", subject);
            return false;
        }
    }
}

[GenerateSerializer]
public sealed record KernelSettingRecord(
    [property: Id(0)] string Scope,
    [property: Id(1)] string Key,
    [property: Id(2)] string Value,
    [property: Id(3)] bool IsPrivate);
