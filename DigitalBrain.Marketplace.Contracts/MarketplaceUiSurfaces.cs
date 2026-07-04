namespace DigitalBrain.Marketplace.Contracts;

using DigitalBrain.Core;

public static class MarketplaceUiSurfaces
{
    public const string SalesforceCapabilityPackName = "DigitalBrain.Capability.Salesforce";
    public const string SalesforceConfigPackName = "salesforce";
    public const string SalesforceCallbackPath = "/salesforce-callback";

    public static UiSurface MarketplaceListFromPacks(
        IReadOnlyList<NeuroPack> publishedPacks,
        IReadOnlyList<NeuroPack> installedPacks,
        string userId = "anonymous",
        string? clientId = null)
    {
        userId = EffectiveUserId(userId);
        var installedKeys = installedPacks
            .Select(PackKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var packs = publishedPacks
            .Select(pack =>
            {
                var installed = installedKeys.Contains(PackKey(pack)) || IsPreinstalledLocalPack(pack);
                var row = new Dictionary<string, object?>
                {
                    ["name"] = pack.Name,
                    ["title"] = DisplayName(pack),
                    ["label"] = DisplayName(pack),
                    ["version"] = pack.Version,
                    ["ownerId"] = pack.OwnerId,
                    ["private"] = pack.IsPrivate,
                    ["commissionRate"] = pack.CommissionRate,
                    ["description"] = pack.Description,
                    ["installed"] = installed,
                    ["status"] = StatusFor(pack, installed),
                    ["kind"] = CapabilityKind(pack),
                    ["icon"] = IconFor(pack),
                    ["capabilities"] = CapabilitiesFor(pack),
                    ["tier"] = pack.Manifest?.Tier.ToString(),
                    ["channels"] = pack.Manifest?.Channels.Select(c => c.ToString()).ToArray(),
                    ["entryExperienceId"] = pack.Manifest?.EntryExperience?.ExperienceId
                };

                var actions = MarketplaceActionsFor(pack, installed, userId, clientId).ToArray();
                row["actions"] = actions;
                if (actions.FirstOrDefault(a => Equals(a[UiSurfaceKeys.ActionId], "enable-salesforce")) is { } enable)
                    row["enableAction"] = enable;
                if (actions.FirstOrDefault(a => Equals(a[UiSurfaceKeys.ActionId], "connect-salesforce")) is { } connect)
                    row["connectAction"] = connect;
                if (actions.FirstOrDefault(a => Equals(a[UiSurfaceKeys.ActionId], "configure-salesforce")) is { } configure)
                    row["configureAction"] = configure;

                return row;
            })
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.MarketplaceList,
            WithCommon(
                surfaceId: "surface.marketplace-list.live",
                emitter: "market-main",
                title: "Marketplace",
                layout: UiSurfaceLayouts.Panel,
                priority: 4,
                props: new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["clientId"] = clientId,
                    ["packs"] = packs,
                    ["installAction"] = UiSurfaceActions.SynapseAction(
                        "install-pack",
                        "Install",
                        nameof(InstallFromMarketplace),
                        new Dictionary<string, object?>
                        {
                            ["buyerId"] = userId,
                            ["userId"] = userId,
                            ["clientId"] = clientId
                        }),
                    ["updateAction"] = UiSurfaceActions.SynapseAction(
                        "update-pack",
                        "Update",
                        nameof(InstallFromMarketplace),
                        new Dictionary<string, object?>
                        {
                            ["buyerId"] = userId,
                            ["userId"] = userId,
                            ["clientId"] = clientId
                        })
                }));
    }

    public static UiSurface MarketplaceTreeSurface(
        IReadOnlyList<NeuroPack> published,
        IReadOnlyList<NeuroPack> installed,
        string? tierFilter,
        string? channelFilter,
        string emitter,
        string title = "Marketplace",
        string userId = "anonymous",
        string? sessionId = null)
    {
        var listSurface = MarketplaceListFromPacks(published, installed, userId, sessionId);
        var allItems = (Dictionary<string, object?>[])listSurface.Props["packs"]!;

        bool Matches(Dictionary<string, object?> item)
        {
            if (tierFilter is not null && item.GetValueOrDefault("tier")?.ToString() != tierFilter) return false;
            if (channelFilter is not null)
            {
                var channels = item.GetValueOrDefault("channels") as string[] ?? Array.Empty<string>();
                if (!channels.Contains(channelFilter)) return false;
            }
            return true;
        }

        var filtered = allItems.Where(Matches).ToArray();

        var tiers = allItems
            .Select(i => i.GetValueOrDefault("tier")?.ToString())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();
        var channelNames = allItems
            .SelectMany(i => i.GetValueOrDefault("channels") as string[] ?? Array.Empty<string>())
            .Distinct()
            .ToList();

        UiWidgetTree FacetButton(string label, string? tier, string? channel) =>
            new(NeuronUiKit.ActionButton, new Dictionary<string, object?>
            {
                [UiSurfaceKeys.Label] = label,
                [UiSurfaceKeys.SynapseType] = nameof(FilterMarketplace),
                [UiSurfaceKeys.Props] = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["tier"] = tier, ["channel"] = channel }
            });

        var facetButtons = new List<UiWidgetTree> { FacetButton("All", null, null) };
        facetButtons.AddRange(tiers.Select(t => FacetButton(t!, t, null)));
        facetButtons.AddRange(channelNames.Select(c => FacetButton(c, null, c)));

        var tree = new UiWidgetTree("column", new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new("row", new Dictionary<string, object?>(), facetButtons),
            new("column", new Dictionary<string, object?>(), filtered.Select(MarketplaceCardTree).ToArray())
        });

        return new UiSurface(UiSurfaceKinds.MarketplaceList, new Dictionary<string, object?>
        {
            ["tree"] = tree,
            ["packs"] = filtered,
            [UiSurfaceKeys.Title] = title,
            [UiSurfaceKeys.Emitter] = emitter,
            ["userId"] = userId,
            ["sessionId"] = sessionId,
            ["activeTier"] = tierFilter,
            ["activeChannel"] = channelFilter
        });
    }

    public static UiSurface InstalledBundlesFromPacks(
        IReadOnlyList<NeuroPack> publishedPacks,
        IReadOnlyList<NeuroPack> installedPacks,
        string userId = "anonymous",
        string? clientId = null)
    {
        userId = EffectiveUserId(userId);
        var installedKeys = installedPacks
            .Select(PackKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bundles = installedPacks
            .Concat(publishedPacks.Where(pack =>
                installedKeys.Contains(PackKey(pack)) ||
                IsPreinstalledLocalPack(pack)))
            .GroupBy(PackKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BundleRow(group.First(), userId, clientId))
            .ToArray();

        var experiences = bundles
            .SelectMany(bundle =>
                bundle.TryGetValue("experiences", out var value) &&
                value is IEnumerable<IReadOnlyDictionary<string, object?>> rows
                    ? rows
                    : Array.Empty<IReadOnlyDictionary<string, object?>>())
            .ToArray();

        var launcherTree = BuildInstalledLauncherTree(bundles, userId, clientId);
        return new UiSurface(
            UiSurfaceKinds.InstalledBundles,
            WithCommon(
                surfaceId: "surface.installed-bundles.live",
                emitter: "market-main",
                title: "Installed Bundles",
                layout: UiSurfaceLayouts.Panel,
                priority: 11,
                props: new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["clientId"] = clientId,
                    ["bundles"] = bundles,
                    ["experiences"] = experiences,
                    ["tree"] = launcherTree
                }));
    }

    public static UiWidgetTree BuildInstalledLauncherTree(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> bundles,
        string userId = "anonymous",
        string? clientId = null)
    {
        userId = EffectiveUserId(userId);
        var kids = new List<UiWidgetTree>();
        if (bundles != null)
        {
            foreach (var b in bundles)
            {
                var name = b.TryGetValue("name", out var n) ? n?.ToString() ?? "bundle" : "bundle";
                var ver = b.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
                var owner = b.TryGetValue("ownerId", out var o) ? o?.ToString() ?? "" : "";
                var desc = b.TryGetValue("description", out var d) ? d?.ToString() ?? "" : "";
                var exps = b.TryGetValue("experiences", out var e) && e is System.Collections.IEnumerable ie
                    ? ie.OfType<IReadOnlyDictionary<string, object?>>().ToList()
                    : new List<IReadOnlyDictionary<string, object?>>();

                var actionKids = new List<UiWidgetTree>();
                var hasOpenExperience = exps.Any(ex =>
                    ex.TryGetValue("name", out var exName) &&
                    string.Equals(exName?.ToString(), "Open", StringComparison.OrdinalIgnoreCase));
                if (!hasOpenExperience)
                {
                    actionKids.Add(new UiWidgetTree("fbutton", new Dictionary<string, object?>
                    {
                        ["label"] = "Open",
                        [UiSurfaceKeys.SynapseType] = nameof(ExperienceUsed),
                        ["packName"] = name,
                        ["action"] = "open",
                        ["targetSurfaceKind"] = name,
                        ["userId"] = userId,
                        ["sessionId"] = clientId
                    }));
                }

                foreach (var ex in exps.Take(2))
                {
                    var lbl = ex.TryGetValue("name", out var ln) ? ln?.ToString() ?? "Run" : "Run";
                    IReadOnlyDictionary<string, object?>? act = null;
                    if (ex.TryGetValue("action", out var av) && av is IReadOnlyDictionary<string, object?> am) act = am;
                    var st = act != null && act.TryGetValue(UiSurfaceKeys.SynapseType, out var stv) ? stv?.ToString() ?? nameof(ExperienceUsed) : nameof(ExperienceUsed);
                    var pName = name;
                    var pAction = act != null && act.TryGetValue(UiSurfaceKeys.Props, out var pv) && pv is IReadOnlyDictionary<string, object?> pmap && pmap.TryGetValue("action", out var av2) ? av2?.ToString() ?? "run" : ex.TryGetValue("experienceId", out var eid) ? eid?.ToString() ?? "run" : "run";
                    var btnP = act != null && act.TryGetValue(UiSurfaceKeys.Props, out var btnProps) && btnProps is IReadOnlyDictionary<string, object?> btnPropMap
                        ? new Dictionary<string, object?>(btnPropMap)
                        : new Dictionary<string, object?>();
                    btnP["label"] = lbl;
                    btnP[UiSurfaceKeys.SynapseType] = st;
                    btnP["packName"] = pName;
                    btnP["action"] = pAction;
                    btnP["userId"] = userId;
                    btnP["sessionId"] = clientId;
                    if (act != null)
                    {
                        btnP["actionDescriptor"] = ScopedAction(act, userId, clientId);
                    }
                    actionKids.Add(new UiWidgetTree("fbutton", btnP));
                }

                var row = new UiWidgetTree("row", new Dictionary<string, object?>(), actionKids);
                var cardKids = new List<UiWidgetTree>
                {
                    new("text", new Dictionary<string, object?> { ["text"] = desc }),
                    row
                };
                kids.Add(new UiWidgetTree("fcard", new Dictionary<string, object?>
                {
                    ["title"] = name + (string.IsNullOrEmpty(ver) ? "" : " " + ver),
                    ["subtitle"] = owner
                }, cardKids));
            }
        }
        return new UiWidgetTree("column", new Dictionary<string, object?>(), kids);
    }

    private static Dictionary<string, object?> BundleRow(NeuroPack pack, string userId, string? clientId)
    {
        var experiences = ExperiencesForPack(pack, userId, clientId).ToArray();
        return new Dictionary<string, object?>
        {
            ["name"] = pack.Name,
            ["version"] = pack.Version,
            ["ownerId"] = pack.OwnerId,
            ["userId"] = userId,
            ["clientId"] = clientId,
            ["installed"] = true,
            ["hasUi"] = true,
            ["status"] = experiences.Length == 0 ? "installed" : "ready",
            ["description"] = pack.Description,
            ["experienceCount"] = experiences.Length,
            ["scenarios"] = experiences.Select(e => e.TryGetValue("name", out var n) ? n?.ToString() : null).Where(s => s != null).ToArray(),
            ["experiences"] = experiences
        };
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ExperiencesForPack(NeuroPack pack, string userId, string? clientId)
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
        else if (pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase))
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

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

    private static IReadOnlyDictionary<string, object?> ScopedAction(
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

    private static string ExperienceSlug(NeuroPack pack, string suffix) =>
        (pack.Name + "-" + pack.Version + "-" + suffix)
            .ToLowerInvariant()
            .Replace(".", "-")
            .Replace("@", "-")
            .Replace(" ", "-");

    private static string PackKey(NeuroPack pack) => pack.Name + "@" + pack.Version;

    private static bool IsPreinstalledLocalPack(NeuroPack pack) =>
        pack.Name.StartsWith("DigitalBrain.UI", StringComparison.Ordinal) ||
        pack.Name.StartsWith("DigitalBrain.Experience", StringComparison.Ordinal) ||
        pack.Name.Equals("ui-gallery", StringComparison.OrdinalIgnoreCase) ||
        pack.Name.Contains("Dummy", StringComparison.OrdinalIgnoreCase);

    private static string DisplayName(NeuroPack pack) =>
        pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase)
            ? "Salesforce CRM"
            : pack.Name;

    private static string CapabilityKind(NeuroPack pack) =>
        pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase)
            ? "integration"
            : pack.Manifest?.Tier.ToString()?.ToLowerInvariant() ?? "pack";

    private static string IconFor(NeuroPack pack) =>
        pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase)
            ? "salesforce"
            : "package";

    private static string StatusFor(NeuroPack pack, bool installed) =>
        pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase)
            ? installed ? "Enabled" : "Not enabled"
            : installed ? "Installed" : "Available";

    private static string[] CapabilitiesFor(NeuroPack pack) =>
        pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase)
            ? new[] { "Accounts", "SOQL query", "CRM summaries" }
            : Array.Empty<string>();

    private static IEnumerable<IReadOnlyDictionary<string, object?>> MarketplaceActionsFor(
        NeuroPack pack,
        bool installed,
        string userId,
        string? clientId)
    {
        if (pack.Name.Equals(SalesforceCapabilityPackName, StringComparison.OrdinalIgnoreCase))
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

    private static IReadOnlyDictionary<string, object?> SalesforceConnectAction(string userId, string? clientId) =>
        UiSurfaceActions.SynapseAction(
            "connect-salesforce",
            "Connect Salesforce",
            SalesforceSignals.AuthRequested,
            new Dictionary<string, object?>
            {
                ["pack"] = SalesforceConfigPackName,
                ["callbackPath"] = SalesforceCallbackPath,
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
                ["pack"] = SalesforceConfigPackName,
                ["userId"] = userId,
                ["clientId"] = clientId
            });

    private static UiWidgetTree MarketplaceCardTree(IReadOnlyDictionary<string, object?> item)
    {
        var title = item.TryGetValue("title", out var t) ? t?.ToString() ?? "" : "";
        var subtitle = item.TryGetValue("status", out var s) ? s?.ToString() ?? "" : "";
        var description = item.TryGetValue("description", out var d) ? d?.ToString() ?? "" : "";
        var capabilities = item.TryGetValue("capabilities", out var c) && c is IEnumerable<string> cs
            ? cs.ToArray()
            : Array.Empty<string>();
        var actions = item.TryGetValue("actions", out var a) && a is IEnumerable<IReadOnlyDictionary<string, object?>> actionRows
            ? actionRows
            : Array.Empty<IReadOnlyDictionary<string, object?>>();

        var children = new List<UiWidgetTree>();
        if (!string.IsNullOrWhiteSpace(description))
            children.Add(new("text", new Dictionary<string, object?> { ["text"] = description }));
        if (capabilities.Length > 0)
            children.Add(new("text", new Dictionary<string, object?> { ["text"] = "Capabilities: " + string.Join(", ", capabilities) }));

        var buttons = actions
            .Select(action => new UiWidgetTree("fbutton", new Dictionary<string, object?>(action)))
            .ToArray();
        if (buttons.Length > 0)
            children.Add(new("row", new Dictionary<string, object?>(), buttons));

        return new UiWidgetTree("fcard", new Dictionary<string, object?>
        {
            ["title"] = title,
            ["subtitle"] = subtitle
        }, children);
    }

    private static IReadOnlyDictionary<string, object?> WithCommon(
        string surfaceId,
        string emitter,
        string title,
        string layout,
        Dictionary<string, object?> props,
        int priority = 0,
        bool requiresInput = false)
    {
        props[UiSurfaceKeys.SurfaceId] = surfaceId;
        props[UiSurfaceKeys.Emitter] = emitter;
        props[UiSurfaceKeys.Title] = title;
        props[UiSurfaceKeys.Priority] = priority;
        props[UiSurfaceKeys.RequiresInput] = requiresInput;
        props[UiSurfaceKeys.Layout] = layout;

        if (!props.ContainsKey(UiSurfaceKeys.Actions))
        {
            props[UiSurfaceKeys.Actions] = Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        return props;
    }
}
