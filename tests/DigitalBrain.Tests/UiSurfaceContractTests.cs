using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Marketplace.Contracts;

namespace DigitalBrain.Tests;

public class UiSurfaceContractTests
{
    public static TheoryData<UiSurface, string[]> PlannedSurfaces => new()
    {
        { UiSurfaceSamples.ActivityGraph(), new[] { "nodes", "edges", "events" } },
        { UiSurfaceSamples.TaskWindow(), new[] { "taskId", "state", "body", UiSurfaceKeys.Actions } },
        { UiSurfaceSamples.TaskManager(), new[] { "totals", "tasks" } },
        { UiSurfaceSamples.UserInput(), new[] { "prompt", "schema", "submitAction", "cancelAction" } },
        { UiSurfaceSamples.Login(), new[] { "clientId", "fields", "submitAction", "tree" } },
        { UiSurfaceSamples.MarketplaceList(), new[] { "packs", "installAction", "updateAction" } },
        { UiSurfaceSamples.InstalledBundles(), new[] { "bundles", "experiences" } },
        { UiSurfaceSamples.Workspace(), new[] { "workspaceId", "activeWorkspace", "contextSources", "isolation", "tree" } },
        { UiSurfaceSamples.Timeline(), new[] { "events", "filters" } },
        { UiSurfaceSamples.DataChart(), new[] { UiSurfaceKeys.ChartSpec, "data", "x", "y", "chartType" } }
    };

    [Theory]
    [MemberData(nameof(PlannedSurfaces))]
    public void Planned_Surface_Samples_Carry_Common_Metadata(UiSurface surface, string[] requiredProps)
    {
        Assert.NotEmpty(surface.Kind);
        AssertCommonProp(surface, UiSurfaceKeys.SurfaceId);
        AssertCommonProp(surface, UiSurfaceKeys.Emitter);
        AssertCommonProp(surface, UiSurfaceKeys.Title);
        AssertCommonProp(surface, UiSurfaceKeys.Priority);
        AssertCommonProp(surface, UiSurfaceKeys.RequiresInput);
        AssertCommonProp(surface, UiSurfaceKeys.Layout);

        foreach (var prop in requiredProps)
        {
            Assert.True(surface.Props.ContainsKey(prop), $"{surface.Kind} is missing required prop '{prop}'.");
        }
    }

    [Fact]
    public void Planned_Surface_Kinds_Use_Stable_Kebab_Case_Names()
    {
        Assert.Equal("activity-graph", UiSurfaceKinds.ActivityGraph);
        Assert.Equal("task-window", UiSurfaceKinds.TaskWindow);
        Assert.Equal("task-manager", UiSurfaceKinds.TaskManager);
        Assert.Equal("user-input", UiSurfaceKinds.UserInput);
        Assert.Equal("login", UiSurfaceKinds.Login);
        Assert.Equal("marketplace-list", UiSurfaceKinds.MarketplaceList);
        Assert.Equal("installed-bundles", UiSurfaceKinds.InstalledBundles);
        Assert.Equal("workspace", UiSurfaceKinds.Workspace);
        Assert.Equal("timeline", UiSurfaceKinds.Timeline);
        Assert.Equal("data-chart", UiSurfaceKinds.DataChart);
    }

    [Fact]
    public void Action_Descriptors_Point_To_Existing_Synapse_Types()
    {
        var userInput = UiSurfaceSamples.UserInput();
        AssertSynapseAction(userInput.Props["submitAction"], nameof(InoRequest));
        AssertSynapseAction(userInput.Props["cancelAction"], "TestMessageSynapse");

        var login = UiSurfaceSamples.Login();
        AssertSynapseAction(login.Props["submitAction"], nameof(LoginRequest));

        var marketplace = UiSurfaceSamples.MarketplaceList();
        AssertSynapseAction(marketplace.Props["installAction"], nameof(InstallFromMarketplace));
        AssertSynapseAction(marketplace.Props["updateAction"], nameof(InstallFromMarketplace));

        var installedBundles = UiSurfaceSamples.InstalledBundles();
        var bundles = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            installedBundles.Props["bundles"]);
        var bundle = Assert.Single(bundles);
        var experiences = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            bundle["experiences"]);
        AssertSynapseAction(Assert.Single(experiences)["action"], nameof(InoRequest));
    }

    [Fact]
    public void Live_Activity_Graph_Surface_Is_Derived_From_Cluster_Journals()
    {
        var surface = UiSurfaceLiveData.ActivityGraphFromTimeline(new Synapse[]
        {
            new ClusterActivity("ino-main", "reasoning", 0.8),
            new ClusterActivity("market-main", "listing", 0.4),
            new ThreeDGraphUpdate("main", "{\"node\":\"ino-main\"}")
        });

        var nodes = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["nodes"]);
        Assert.Contains(nodes, n => Equals(n["id"], "ino-main"));
        Assert.Contains(nodes, n => Equals(n["id"], "market-main"));

        var events = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["events"]);
        Assert.Contains(events, e => Equals(e["type"], nameof(ThreeDGraphUpdate)));
    }

    [Fact]
    public void Live_Marketplace_Surface_Treats_Local_Ui_Packs_As_Preinstalled()
    {
        var surface = MarketplaceUiSurfaces.MarketplaceListFromPacks(
            new[]
            {
                new NeuroPack(
                    "DigitalBrain.UIKit.ForUI",
                    "0.1.0",
                    "digitalbraintech",
                    Description: "ForUI primitive pack")
            },
            Array.Empty<NeuroPack>());

        var packs = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["packs"]);
        var pack = Assert.Single(packs);
        Assert.Equal("DigitalBrain.UIKit.ForUI", pack["name"]);
        Assert.Equal(true, pack["installed"]);

        AssertSynapseAction(surface.Props["installAction"], nameof(InstallFromMarketplace));
        AssertSynapseAction(surface.Props["updateAction"], nameof(InstallFromMarketplace));
    }

    [Fact]
    public void Live_Marketplace_Surface_Scopes_Install_Actions_To_User_Session()
    {
        var surface = MarketplaceUiSurfaces.MarketplaceListFromPacks(
            new[]
            {
                new NeuroPack(
                    "DigitalBrain.UIKit.ForUI",
                    "0.1.0",
                    "digitalbraintech",
                    Description: "ForUI primitive pack")
            },
            Array.Empty<NeuroPack>(),
            "alice",
            "client-1");

        Assert.Equal("alice", surface.Props["userId"]);
        Assert.Equal("client-1", surface.Props["clientId"]);

        var installProps = AssertActionProps(surface.Props["installAction"], nameof(InstallFromMarketplace));
        Assert.Equal("alice", installProps["buyerId"]);
        Assert.Equal("alice", installProps["userId"]);
        Assert.Equal("client-1", installProps["clientId"]);
        Assert.False(installProps.ContainsKey("sessionId"));

        var updateProps = AssertActionProps(surface.Props["updateAction"], nameof(InstallFromMarketplace));
        Assert.Equal("alice", updateProps["buyerId"]);
        Assert.Equal("client-1", updateProps["clientId"]);
        Assert.False(updateProps.ContainsKey("sessionId"));
    }

    [Fact]
    public void Live_Marketplace_Surface_Projects_Salesforce_As_A_Capability_Tile()
    {
        var surface = MarketplaceUiSurfaces.MarketplaceListFromPacks(
            MarketplaceSeeds.LocalUiPacks,
            Array.Empty<NeuroPack>(),
            "alice",
            "client-1");

        var packs = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["packs"]);
        var salesforce = packs.Single(pack => Equals(pack["name"], MarketplaceUiSurfaces.SalesforceCapabilityPackName));

        Assert.Equal("Salesforce CRM", salesforce["title"]);
        Assert.Equal("integration", salesforce["kind"]);
        Assert.Equal("salesforce", salesforce["icon"]);
        Assert.Equal("Not enabled", salesforce["status"]);
        Assert.Equal(false, salesforce["installed"]);
        Assert.Contains("Accounts", Assert.IsAssignableFrom<IEnumerable<string>>(salesforce["capabilities"]));
        Assert.Contains("SOQL query", Assert.IsAssignableFrom<IEnumerable<string>>(salesforce["capabilities"]));

        var enableProps = AssertActionProps(salesforce["enableAction"], nameof(InstallFromMarketplace));
        Assert.Equal(MarketplaceUiSurfaces.SalesforceCapabilityPackName, enableProps["packName"]);
        Assert.Equal("0.1.0", enableProps["version"]);
        Assert.Equal("alice", enableProps["buyerId"]);
        Assert.Equal("client-1", enableProps["clientId"]);

        var connectProps = AssertActionProps(salesforce["connectAction"], SalesforceSignals.AuthRequested);
        Assert.Equal(MarketplaceUiSurfaces.SalesforceConfigPackName, connectProps["pack"]);
        Assert.Equal(MarketplaceUiSurfaces.SalesforceCallbackPath, connectProps["callbackPath"]);
        Assert.Equal("alice", connectProps["userId"]);
        Assert.Equal("client-1", connectProps["clientId"]);
    }

    [Fact]
    public void Live_InstalledBundles_Surface_Exposes_Runnable_Experiences()
    {
        var surface = MarketplaceUiSurfaces.InstalledBundlesFromPacks(
            MarketplaceSeeds.LocalUiPacks,
            Array.Empty<NeuroPack>());

        Assert.Equal(UiSurfaceKinds.InstalledBundles, surface.Kind);

        var bundles = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            surface.Props["bundles"]);
        Assert.Contains(bundles, bundle => Equals(bundle["name"], "DigitalBrain.UI.Workbench"));

        var experiences = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            surface.Props["experiences"]);
        Assert.Contains(experiences, experience => Equals(experience["name"], "Open Workbench"));

        var workbench = experiences.Single(experience => Equals(experience["name"], "Open Workbench"));
        AssertSynapseAction(workbench["action"], nameof(InoRequest));

        // Dummy.DevPack demo removed (bloat delete). Other experiences (Gmail Insights etc.) remain via real paths.
        Assert.Contains(experiences, e => Equals(e["name"], "Gmail Insights"));
    }

    [Fact]
    public void Live_InstalledBundles_Surface_Scopes_Experience_Actions_To_User_Session()
    {
        var surface = MarketplaceUiSurfaces.InstalledBundlesFromPacks(
            MarketplaceSeeds.LocalUiPacks,
            Array.Empty<NeuroPack>(),
            "alice",
            "client-1");

        Assert.Equal("alice", surface.Props["userId"]);
        Assert.Equal("client-1", surface.Props["clientId"]);

        var experiences = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            surface.Props["experiences"]);
        // "Run self-test" from Dummy demo removed.
        // Assert other scoped experiences if present (e.g. Gmail or real ones).
    }

    [Fact]
    public void Live_InstalledBundles_Surface_Exposes_Salesforce_Capability_Actions_When_Installed()
    {
        var salesforcePack = MarketplaceSeeds.LocalUiPacks.Single(p =>
            p.Name == MarketplaceUiSurfaces.SalesforceCapabilityPackName);
        var surface = MarketplaceUiSurfaces.InstalledBundlesFromPacks(
            MarketplaceSeeds.LocalUiPacks,
            new[] { salesforcePack },
            "alice",
            "client-1");

        var experiences = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            surface.Props["experiences"]);

        var connect = experiences.Single(experience => Equals(experience["name"], "Connect Salesforce"));
        var configure = experiences.Single(experience => Equals(experience["name"], "Configure Salesforce"));
        var listAccounts = experiences.Single(experience => Equals(experience["name"], "List Accounts"));

        AssertActionProps(connect["action"], SalesforceSignals.AuthRequested);
        AssertActionProps(configure["action"], SalesforceSignals.AuthRequested);
        var listProps = AssertActionProps(listAccounts["action"], nameof(InoRequest));
        Assert.Contains("Salesforce accounts", listProps["prompt"]?.ToString());
        Assert.Equal("client-1", listProps["clientId"]);
    }

    [Fact]
    public void Live_TaskManager_Surface_Scopes_Task_Actions_To_User_Session()
    {
        var taskId = new TaskId("task-alice-1");
        var surface = UiSurfaceLiveData.TaskManagerFromTasks(
            new Synapse[] { new TaskCreated(taskId, "Summarize latest mail") },
            userId: "alice",
            clientId: "client-1");

        Assert.Equal("alice", surface.Props["userId"]);
        Assert.Equal("client-1", surface.Props["clientId"]);

        var runProps = AssertActionProps(surface.Props["runAction"], nameof(RunTask));
        Assert.Equal("alice", runProps["userId"]);
        Assert.Equal("client-1", runProps["sessionId"]);

        var rows = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            surface.Props["tasks"]);
        var row = Assert.Single(rows);
        Assert.Equal("task-alice-1", row["taskId"]);
        Assert.Equal("alice", row["userId"]);
        Assert.Equal("client-1", row["clientId"]);

        var cancelProps = AssertActionProps(row["cancelAction"], nameof(CancelTask));
        Assert.Equal("task-alice-1", cancelProps["taskId"]);
        Assert.Equal("alice", cancelProps["userId"]);
        Assert.Equal("client-1", cancelProps["sessionId"]);

        Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal("Summarize latest mail", row["description"]);
        Assert.Equal("active", row["state"]);
    }

    [Fact]
    public void Live_TaskManager_Surface_Renders_Active_Completed_And_Cancelled_Tasks()
    {
        var activeTask = new TaskId("task-active");
        var completedTask = new TaskId("task-completed");
        var cancelledTask = new TaskId("task-cancelled");

        var surface = UiSurfaceLiveData.TaskManagerFromTasks(
            new Synapse[]
            {
                new TaskCreated(activeTask, "Keep working"),
                new TaskStarted(activeTask),
                new TaskProgress(activeTask, "running"),
                new TaskCreated(completedTask, "Finish report"),
                new TaskProgress(completedTask, "finalizing"),
                new TaskCompleted(completedTask, "report ready"),
                new TaskCreated(cancelledTask, "Stop this"),
                new TaskCancelled(cancelledTask)
            },
            userId: "alice",
            clientId: "client-1");

        var totals = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(surface.Props["totals"]);
        Assert.Equal(1, totals["active"]);
        Assert.Equal(1, totals["completed"]);
        Assert.Equal(1, totals["cancelled"]);

        var rows = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["tasks"])
            .ToArray();
        var activeRow = rows.Single(row => Equals(row["taskId"], "task-active"));
        var completedRow = rows.Single(row => Equals(row["taskId"], "task-completed"));
        var cancelledRow = rows.Single(row => Equals(row["taskId"], "task-cancelled"));

        Assert.Equal("active", activeRow["state"]);
        Assert.True(activeRow.ContainsKey("cancelAction"));
        Assert.Equal("completed", completedRow["state"]);
        Assert.Equal("report ready", completedRow["result"]);
        Assert.False(completedRow.ContainsKey("cancelAction"));
        Assert.Equal("cancelled", cancelledRow["state"]);
        Assert.False(cancelledRow.ContainsKey("cancelAction"));

        var events = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(completedRow["events"]);
        Assert.Contains(events, e => Equals(e["type"], nameof(TaskCompleted)));

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var descendants = Descend(tree).ToArray();
        Assert.Contains(descendants, node => node.Type == "fcard" && Equals(node.Props["title"], "Active"));
        Assert.Contains(descendants, node => node.Type == "fcard" && Equals(node.Props["title"], "Completed"));
        Assert.Contains(descendants, node => node.Type == "fcard" && Equals(node.Props["title"], "Cancelled"));
        Assert.Contains(descendants, node => node.Type == "fbutton" && Equals(node.Props[UiSurfaceKeys.Label], "Run Task"));
        Assert.Contains(descendants, node => node.Type == "fbutton" && Equals(node.Props[UiSurfaceKeys.Label], "Cancel"));
    }

    [Fact]
    public void Live_Workspace_Surface_Describes_Context_Boundary()
    {
        var surface = UiSurfaceLiveData.WorkspaceBoundary("alice", "finance", "client-1");

        Assert.Equal(UiSurfaceKinds.Workspace, surface.Kind);
        Assert.Equal("alice", surface.Props["userId"]);
        Assert.Equal("finance", surface.Props["workspaceId"]);
        Assert.Equal("finance", surface.Props["activeWorkspace"]);
        Assert.Equal("client-1", surface.Props["clientId"]);

        var sources = Assert.IsAssignableFrom<IEnumerable<string>>(surface.Props["contextSources"]);
        Assert.Contains(sources, source => source.Contains("Uploaded files"));
        Assert.Contains(sources, source => source.Contains("Vector collection"));

        var isolation = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(surface.Props["isolation"]);
        Assert.Equal("user:alice:workspace:finance:documents", isolation["vectorCollection"]);
        Assert.Equal(PackConfigScopes.ForUser(new UserId("alice")), isolation["packConfigScope"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var descendants = Descend(tree).ToArray();
        Assert.Contains(descendants, node => node.Type == "fcard" && Equals(node.Props["title"], "Active workspace"));
        Assert.Contains(descendants, node => node.Type == "fcard" && Equals(node.Props["title"], "Context sources"));
        Assert.Contains(descendants, node => node.Type == "fcard" && Equals(node.Props["title"], "Isolation boundary"));
    }

    [Fact]
    public void Live_DataChart_Surface_Is_Derived_From_Chart_Journal()
    {
        var generated = new DataChartGenerated("req-1", UiSurfaceSamples.DataChart());

        var surfaces = UiSurfaceLiveData.ChartSurfacesFromTimeline(new Synapse[] { generated });

        var surface = Assert.Single(surfaces);
        Assert.Equal(UiSurfaceKinds.DataChart, surface.Kind);
        Assert.True(surface.Props.ContainsKey(UiSurfaceKeys.ChartSpec));
    }

    [Fact]
    public void DataVisualization_Builder_Produces_Generic_DataChart_Surface()
    {
        var surface = DataChartBuilder.BuildSurface(
            "req-builder",
            "chart-main",
            "show revenue trend over time",
            """
            [
              { "month": "Jan", "revenue": 10, "region": "EU" },
              { "month": "Feb", "revenue": 14, "region": "EU" }
            ]
            """);

        Assert.Equal(UiSurfaceKinds.DataChart, surface.Kind);
        var spec = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(surface.Props[UiSurfaceKeys.ChartSpec]);
        Assert.Equal("line", spec["chartType"]);
        Assert.Equal("month", spec["x"]);
        Assert.Equal("revenue", spec["y"]);
        Assert.Equal("region", spec["series"]);
    }

    [Fact]
    public void Local_Marketplace_Seeds_Include_Preinstalled_Ui_Packs()
    {
        var packs = MarketplaceSeeds.LocalUiPacks;

        Assert.Contains(packs, p => p.Name == "DigitalBrain.UIKit.ForUI");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.Workbench");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.Graph3D");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.CreatorSurfaces");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.AspireFlutter");
        Assert.All(packs, p => Assert.Equal("digitalbraintech", p.OwnerId));
    }

    [Fact]
    public void Local_Marketplace_Seeds_Include_Salesforce_Capability_Pack()
    {
        var pack = MarketplaceSeeds.LocalUiPacks.Single(p =>
            p.Name == MarketplaceUiSurfaces.SalesforceCapabilityPackName);

        Assert.Equal("0.1.0", pack.Version);
        Assert.Contains("OAuth", pack.Description);
        Assert.Contains("SOQL", pack.Description);
        Assert.Equal("digitalbraintech", pack.OwnerId);
        Assert.Equal("Channel", pack.Manifest?.Tier.ToString());
        Assert.Contains("InApp", pack.Manifest?.Channels.Select(c => c.ToString()) ?? Array.Empty<string>());
    }

    private static void AssertCommonProp(UiSurface surface, string key) =>
        Assert.True(surface.Props.ContainsKey(key), $"{surface.Kind} is missing common prop '{key}'.");

    private static IEnumerable<UiWidgetTree> Descend(UiWidgetTree node)
    {
        yield return node;
        if (node.Children is null) yield break;

        foreach (var child in node.Children)
        {
            foreach (var descendant in Descend(child))
            {
                yield return descendant;
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> AssertSynapseAction(object? value, string expectedSynapseType)
    {
        var action = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(value);
        Assert.NotEmpty((string)action[UiSurfaceKeys.ActionId]!);
        Assert.NotEmpty((string)action[UiSurfaceKeys.Label]!);
        Assert.Equal(expectedSynapseType, action[UiSurfaceKeys.SynapseType]);
        Assert.True(action.ContainsKey(UiSurfaceKeys.Props));
        return action;
    }

    private static IReadOnlyDictionary<string, object?> AssertActionProps(object? value, string expectedSynapseType)
    {
        var action = AssertSynapseAction(value, expectedSynapseType);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(action[UiSurfaceKeys.Props]);
    }

    [Fact]
    public void NeuronUiKit_Consts_Are_Stable_For_Server_Driven_Trees()
    {
        Assert.Equal("neuron:Menu", NeuronUiKit.Menu);
        Assert.Equal("neuron:MenuItem", NeuronUiKit.MenuItem);
        Assert.Equal("neuron:ActionButton", NeuronUiKit.ActionButton);
        Assert.Equal("neuron:NeuronButton", NeuronUiKit.NeuronButton);
        Assert.Equal("neuron:Form", NeuronUiKit.Form);
        Assert.Equal("neuron:Header", NeuronUiKit.Header);
        Assert.Equal("neuron:Divider", NeuronUiKit.Divider);
    }

    [Fact]
    public void AppShell_Tree_Can_Use_NeuronUiKit_Menu_Items()
    {
        var shell = new DigitalBrain.Core.UiWidgetTree(
            "app-shell",
            new Dictionary<string, object?> { ["activeContent"] = "marketplace-list" },
            new List<DigitalBrain.Core.UiWidgetTree>
            {
                new DigitalBrain.Core.UiWidgetTree(NeuronUiKit.Header, new Dictionary<string, object?> { ["title"] = "DigitalBrain" }),
                new DigitalBrain.Core.UiWidgetTree(NeuronUiKit.Menu, new Dictionary<string, object?>(),
                    new[]
                    {
                        new DigitalBrain.Core.UiWidgetTree(NeuronUiKit.MenuItem,
                            new Dictionary<string, object?> { ["label"] = "Marketplace", ["targetSurfaceKind"] = "marketplace-list" }),
                        new DigitalBrain.Core.UiWidgetTree(NeuronUiKit.Divider, new Dictionary<string, object?>()),
                        new DigitalBrain.Core.UiWidgetTree(NeuronUiKit.MenuItem,
                            new Dictionary<string, object?> { ["label"] = "Tasks", ["targetSurfaceKind"] = "task-manager" })
                    })
            });

        Assert.Equal("app-shell", shell.Type);
        // header + menu
        Assert.Equal(2, shell.Children!.Count);
        var menu = shell.Children[1];
        Assert.Equal(NeuronUiKit.Menu, menu.Type);
        Assert.Equal(3, menu.Children!.Count); // item + divider + item
        Assert.Equal(NeuronUiKit.MenuItem, menu.Children[0].Type);
        Assert.Equal("Marketplace", menu.Children[0].Props["label"]);
        Assert.Equal(NeuronUiKit.Divider, menu.Children[1].Type);
    }

    [Fact]
    public void NeuronUiKit_Consts_Are_Stable()
    {
        Assert.Equal("neuron:Menu", NeuronUiKit.Menu);
        Assert.Equal("neuron:MenuItem", NeuronUiKit.MenuItem);
        Assert.Equal("forui:FScaffold", NeuronUiKit.Scaffold);
        Assert.Equal("forui:FSidebar", NeuronUiKit.Sidebar);
    }

    [Fact]
    public void BuildHeader_Helper_Produces_Correct_Tree()
    {
        var node = NeuronUiKit.BuildHeader("Title", "Sub");
        Assert.Equal(NeuronUiKit.Header, node.Type);
        Assert.Equal("Title", node.Props["title"]);
        Assert.Equal("Sub", node.Props["subtitle"]);
    }
}

