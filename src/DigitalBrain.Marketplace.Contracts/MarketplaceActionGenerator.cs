namespace DigitalBrain.Marketplace.Contracts;

using DigitalBrain.Core;
using DigitalBrain.Ui.Contracts;

internal static class MarketplaceActionGenerator
{
    public static IEnumerable<IReadOnlyDictionary<string, object?>> MarketplaceActionsFor(
        NeuroPack pack,
        bool installed,
        string userId,
        string? clientId)
    {
        if (pack.Name.Equals(MarketplaceUiSurfaces.SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase))
        {
            yield return UiSurfaceActions.SynapseAction(
                "enable-salesforce",
                installed ? "Enabled" : "Enable",
                nameof(InstallFromMarketplace),
                new Dictionary<string, object?>
                {
                    ["packName"] = pack.Name,
                    ["version"] = pack.Version,
                    ["buyerId"] = userId,
                    ["userId"] = userId,
                    ["clientId"] = clientId
                });
            yield return SalesforceConnectAction(userId, clientId);
            yield return SalesforceConfigureAction(userId, clientId);
            yield break;
        }

        yield return UiSurfaceActions.SynapseAction(
            installed ? "update-pack" : "install-pack",
            installed ? "Update" : "Install",
            nameof(InstallFromMarketplace),
            new Dictionary<string, object?>
            {
                ["packName"] = pack.Name,
                ["version"] = pack.Version,
                ["buyerId"] = userId,
                ["userId"] = userId,
                ["clientId"] = clientId
            });
    }

    public static IEnumerable<IReadOnlyDictionary<string, object?>> ExperiencesForPack(
        NeuroPack pack,
        string userId,
        string? clientId)
    {
        if (pack.Name.Equals("DigitalBrain.UI.Workbench", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "open",
                "Open Workbench",
                "app",
                "Launch the main DigitalBrain workbench.",
                UiSurfaceActions.SynapseAction(
                    "open-workbench",
                    "Open",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Open the DigitalBrain workbench experience.",
                        ["sessionId"] = "workbench"
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Equals("DigitalBrain.UI.Graph3D", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "cluster-graph",
                "Cluster Graph",
                "experience",
                "Open the live cluster graph experience.",
                UiSurfaceActions.SynapseAction(
                    "open-cluster-graph",
                    "Open",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Open the live cluster graph experience.",
                        ["sessionId"] = "workbench"
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Equals("DigitalBrain.UI.CreatorSurfaces", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "create-surface",
                "Create Surface",
                "experience",
                "Start a generated UI surface workflow.",
                UiSurfaceActions.SynapseAction(
                    "create-surface",
                    "Create",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Create a new DigitalBrain UI surface from the installed CreatorSurfaces bundle.",
                        ["sessionId"] = "workbench"
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Equals("DigitalBrain.UI.AspireFlutter", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "restart-ui",
                "Restart UI Client",
                "app",
                "Restart the Aspire-hosted Flutter UI client.",
                UiSurfaceActions.SynapseAction(
                    "restart-flutter-ui",
                    "Restart",
                    nameof(RestartResource),
                    new Dictionary<string, object?>
                    {
                        ["resourceName"] = "flutter-ui"
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Equals("DigitalBrain.Experience.GmailInsights", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "gmail-last-100-chart",
                "Gmail Insights",
                "experience",
                "Retrieve the last 100 Gmail messages, summarize them locally, and visualize message categories.",
                UiSurfaceActions.SynapseAction(
                    "gmail-last-100-chart",
                    "Run",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?>
                    {
                        ["packName"] = pack.Name,
                        ["action"] = "gmail:last-100-chart"
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Equals(MarketplaceUiSurfaces.SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "connect-salesforce",
                "Connect Salesforce",
                "integration",
                "Start Salesforce OAuth for this user.",
                SalesforceConnectAction(userId, clientId),
                userId,
                clientId);
            yield return ExperienceRow(
                pack,
                "configure-salesforce",
                "Configure Salesforce",
                "integration",
                "Open the Salesforce credentials and connected app configuration surface.",
                SalesforceConfigureAction(userId, clientId),
                userId,
                clientId);
            yield return ExperienceRow(
                pack,
                "list-salesforce-accounts",
                "List Accounts",
                "crm",
                "Ask INO to list Salesforce Account records through the read-only CRM neuron.",
                UiSurfaceActions.SynapseAction(
                    "list-salesforce-accounts",
                    "List Accounts",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "List Salesforce accounts using the Salesforce CRM capability.",
                        ["clientId"] = clientId
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Contains("ClosedLoop", StringComparison.OrdinalIgnoreCase))
        {
            var loopType = pack.Name.Contains("Software", StringComparison.OrdinalIgnoreCase) ? "se" : "ui";
            yield return ExperienceRow(
                pack,
                "run-closed-loop",
                "Run Closed Loop",
                "experience",
                "Run the installed closed-loop experience.",
                UiSurfaceActions.SynapseAction(
                    "run-" + ExperienceSlug(pack, "closed-loop"),
                    "Run",
                    nameof(ClosedLoopRequest),
                    new Dictionary<string, object?>
                    {
                        ["loopType"] = loopType,
                        ["prompt"] = "Run installed bundle " + pack.Name
                    }),
                userId,
                clientId);
        }
        else if (pack.Name.Contains("Dummy", StringComparison.OrdinalIgnoreCase) || pack.Name.Contains("DevPack", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "self-test",
                "Run self-test",
                "experience",
                "Execute pack self-test scenario.",
                UiSurfaceActions.SynapseAction(
                    "dummy-self-test",
                    "Run self-test",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "self-test" }),
                userId,
                clientId);
            yield return ExperienceRow(
                pack,
                "emit-test-surface",
                "Emit test surface",
                "app",
                "Pack responds by emitting a live UI surface for the main area.",
                UiSurfaceActions.SynapseAction(
                    "dummy-emit-surface",
                    "Emit test surface",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "emit-test-surface" }),
                userId,
                clientId);
        }
        else
        {
            yield return ExperienceRow(
                pack,
                "run",
                "Run",
                "experience",
                "Execute the installed pack's Respond behavior.",
                UiSurfaceActions.SynapseAction(
                    "run-" + ExperienceSlug(pack, "experience"),
                    "Run",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "run" }),
                userId,
                clientId);
            yield return ExperienceRow(
                pack,
                "emit-test-surface",
                "Emit demo surface",
                "app",
                "Trigger pack scenario that emits a live UI surface into the main area.",
                UiSurfaceActions.SynapseAction(
                    "emit-" + ExperienceSlug(pack, "surface"),
                    "Emit surface",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "emit-test-surface" }),
                userId,
                clientId);
        }
    }

    public static IReadOnlyDictionary<string, object?> ScopedAction(
        IReadOnlyDictionary<string, object?> action,
        string userId,
        string? clientId)
    {
        var scopedAction = new Dictionary<string, object?>(action);
        var props = action.TryGetValue(UiSurfaceKeys.Props, out var value) &&
            value is IReadOnlyDictionary<string, object?> existingProps
                ? new Dictionary<string, object?>(existingProps)
                : new Dictionary<string, object?>();

        props["userId"] = EffectiveUserId(userId);
        props["clientId"] = clientId;

        if (string.Equals(action.TryGetValue(UiSurfaceKeys.SynapseType, out var type) ? type?.ToString() : null,
                nameof(InstallFromMarketplace),
                StringComparison.Ordinal) &&
            !props.ContainsKey("buyerId"))
        {
            props["buyerId"] = EffectiveUserId(userId);
        }

        scopedAction[UiSurfaceKeys.Props] = props;
        return scopedAction;
    }

    private static IReadOnlyDictionary<string, object?> ExperienceRow(
        NeuroPack pack,
        string suffix,
        string name,
        string kind,
        string summary,
        IReadOnlyDictionary<string, object?> action,
        string userId,
        string? clientId) => new Dictionary<string, object?>
        {
            ["experienceId"] = ExperienceSlug(pack, suffix),
            ["bundleName"] = pack.Name,
            ["userId"] = userId,
            ["clientId"] = clientId,
            ["name"] = name,
            ["kind"] = kind,
            ["status"] = "ready",
            ["summary"] = summary,
            ["action"] = ScopedAction(action, userId, clientId)
        };

    private static IReadOnlyDictionary<string, object?> SalesforceConnectAction(string userId, string? clientId) =>
        UiSurfaceActions.SynapseAction(
            "connect-salesforce",
            "Connect Salesforce",
            SalesforceSignals.AuthRequested,
            new Dictionary<string, object?>
            {
                ["pack"] = MarketplaceUiSurfaces.SalesforceConfigPackName,
                ["callbackPath"] = MarketplaceUiSurfaces.SalesforceCallbackPath,
                ["userId"] = userId,
                ["clientId"] = clientId
            });

    private static IReadOnlyDictionary<string, object?> SalesforceConfigureAction(string userId, string? clientId) =>
        UiSurfaceActions.SynapseAction(
            "configure-salesforce",
            "Configure",
            SalesforceSignals.AuthRequested,
            new Dictionary<string, object?>
            {
                ["pack"] = MarketplaceUiSurfaces.SalesforceConfigPackName,
                ["userId"] = userId,
                ["clientId"] = clientId
            });

    private static string ExperienceSlug(NeuroPack pack, string suffix) =>
        (pack.Name + "-" + pack.Version + "-" + suffix)
            .ToLowerInvariant()
            .Replace(".", "-")
            .Replace("@", "-")
            .Replace(" ", "-");

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();
}