using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.DigitalBrain.Onboarding;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Onboarding.Onboarding;

[GrainType("DigitalBrain.Domains.Onboarding.OnboardingStore")]
public sealed class OnboardingStoreNeuron(
    [FromKeyedServices("onb-accepted")] IDurableList<PolicyAcceptanceRecord> accepted)
    : DurableGrain, ICallNeuronTarget, IPredicateNeuronTarget
{
    public async Task<string> AskAsync(string prompt)
    {
        if (prompt.StartsWith("terms-card", StringComparison.Ordinal))
        {
            var version = prompt["terms-card".Length..].Trim();
            if (string.IsNullOrEmpty(version))
            {
                try
                {
                    var activeScope = BrainScopeHelper.GetActiveScope();
                    var settingsStore = GrainFactory.GetGrain<ICallNeuronTarget>("DigitalBrain.Kernel.Settings.SettingsStore", activeScope);
                    version = await settingsStore.AskAsync("get terms-version");
                }
                catch
                {
                    version = "";
                }
                if (string.IsNullOrEmpty(version))
                {
                    version = OnboardingPolicy.DefaultTermsVersion;
                }
            }
            return OnboardingPlan.TermsCardDataJson(OnboardingPolicy.DefaultTermsText, version);
        }

        if (prompt.StartsWith("accept ", StringComparison.Ordinal))
        {
            var rest = prompt["accept ".Length..].Trim();
            var parts = rest.Split(' ', 2);
            var userId = parts[0];
            var version = parts.Length > 1 ? parts[1] : "";
            
            if (string.IsNullOrEmpty(version))
            {
                try
                {
                    var activeScope = BrainScopeHelper.GetActiveScope();
                    var settingsStore = GrainFactory.GetGrain<ICallNeuronTarget>("DigitalBrain.Kernel.Settings.SettingsStore", activeScope);
                    version = await settingsStore.AskAsync("get terms-version");
                }
                catch
                {
                    version = "";
                }
                if (string.IsNullOrEmpty(version))
                {
                    version = OnboardingPolicy.DefaultTermsVersion;
                }
            }

            accepted.Add(new PolicyAcceptanceRecord(userId, version));
            while (accepted.Count > 100) accepted.RemoveAt(0); // Match MaxJournalEntries
            await WriteStateAsync();
            return "ok";
        }

        return "";
    }

    public Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        var latest = accepted.LastOrDefault(a => a.UserId == subject)?.Version ?? "";
        return Task.FromResult(string.Equals(latest, target, StringComparison.Ordinal));
    }
}
