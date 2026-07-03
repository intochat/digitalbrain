namespace DigitalBrain.Core;

public static class UiSurfaceSamples
{
    public static UiSurface ActivityGraph() => new(
        UiSurfaceKinds.ActivityGraph,
        WithCommon(
            surfaceId: "surface.activity-graph",
            emitter: "digitalbrain.cluster",
            title: "Activity Graph",
            layout: UiSurfaceLayouts.Compact,
            props: new Dictionary<string, object?>
            {
                ["nodes"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "ino-main",
                        ["label"] = "INO",
                        ["activity"] = 0.8
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "market-main",
                        ["label"] = "Marketplace",
                        ["activity"] = 0.4
                    }
                },
                ["edges"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["from"] = "ino-main",
                        ["to"] = "market-main",
                        ["value"] = 0.3
                    }
                },
                ["events"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = nameof(ClusterActivity),
                        ["nodeId"] = "ino-main",
                        ["activity"] = "reasoning",
                        ["value"] = 0.8
                    }
                }
            }));

    public static UiSurface TaskWindow() => new(
        UiSurfaceKinds.TaskWindow,
        WithCommon(
            surfaceId: "surface.task-window.demo",
            emitter: "demo",
            title: "Task Window",
            layout: UiSurfaceLayouts.Panel,
            props: new Dictionary<string, object?>
            {
                ["taskId"] = "task-demo-1",
                ["state"] = "running",
                ["body"] = "Generate a concise status summary of current work.",
                [UiSurfaceKeys.Actions] = new[]
                {
                    SynapseAction("cancel-task", "Cancel", nameof(DemoMessageSynapse), new Dictionary<string, object?>
                    {
                        ["taskId"] = "task-demo-1"
                    })
                }
            }));

    public static UiSurface UserInput() => new(
        UiSurfaceKinds.UserInput,
        WithCommon(
            surfaceId: "surface.user-input.demo",
            emitter: "ino-main",
            title: "INO Input",
            layout: UiSurfaceLayouts.Modal,
            requiresInput: true,
            props: new Dictionary<string, object?>
            {
                ["prompt"] = "What should INO work on next?",
                ["schema"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["prompt"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["title"] = "Prompt"
                        }
                    },
                    ["required"] = new[] { "prompt" }
                },
                ["submitAction"] = SynapseAction("ask-ino", "Ask INO", nameof(InoRequest), new Dictionary<string, object?>
                {
                    ["sessionId"] = "workbench"
                }),
                ["cancelAction"] = SynapseAction("dismiss-input", "Dismiss", nameof(DemoMessageSynapse), new Dictionary<string, object?>
                {
                    ["taskId"] = "task-demo-1"
                })
            }));

    public static UiSurface Login(
        string? error = null,
        string clientId = "flutter",
        string? defaultUsername = null,
        string? defaultPassword = null)
    {
        static Dictionary<string, object?> Field(string name, string label, string kind, string? value) => new()
        {
            ["name"] = name,
            ["label"] = label,
            ["kind"] = kind,
            ["required"] = true,
            ["value"] = value ?? string.Empty
        };

        Dictionary<string, object?>[] Fields() => new[]
        {
            Field("username", "Username", "text", defaultUsername),
            Field("password", "Password", "password", defaultPassword)
        };

        return new(
            UiSurfaceKinds.Login,
            WithCommon(
                surfaceId: "surface.login.local",
                emitter: "session-main",
                title: "Sign In",
                layout: UiSurfaceLayouts.Panel,
                requiresInput: true,
                priority: 100,
                props: new Dictionary<string, object?>
                {
                    ["clientId"] = clientId,
                    ["mode"] = "local",
                    ["error"] = error,
                    ["fields"] = Fields(),
                    ["submitAction"] = SynapseAction(
                        "local-login",
                        "Sign in",
                        nameof(LoginRequest),
                        new Dictionary<string, object?>
                        {
                            ["clientId"] = clientId
                        }),
                    ["tree"] = new UiWidgetTree(
                        NeuronUiKit.Form,
                        new Dictionary<string, object?>
                        {
                            ["title"] = "Sign In",
                            ["submitLabel"] = "Sign in",
                            ["error"] = error,
                            [UiSurfaceKeys.SynapseType] = nameof(LoginRequest),
                            ["clientId"] = clientId,
                            ["fields"] = Fields()
                        })
                }));
    }

    public static UiSurface MarketplaceList() => new(
        UiSurfaceKinds.MarketplaceList,
        WithCommon(
            surfaceId: "surface.marketplace-list",
            emitter: "market-main",
            title: "Marketplace",
            layout: UiSurfaceLayouts.Panel,
            props: new Dictionary<string, object?>
            {
                ["packs"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "DigitalBrain.UIKit.ForUI",
                        ["version"] = "0.1.0",
                        ["installed"] = true,
                        ["description"] = "Trusted ForUI primitive pack for DigitalBrain surfaces."
                    }
                },
                ["installAction"] = SynapseAction("install-pack", "Install", "InstallFromMarketplace", new Dictionary<string, object?>
                {
                    ["version"] = "0.1.0",
                    ["buyerId"] = "anonymous",
                    ["userId"] = "anonymous"
                }),
                ["updateAction"] = SynapseAction("update-pack", "Update", "InstallFromMarketplace", new Dictionary<string, object?>
                {
                    ["version"] = "0.1.0",
                    ["buyerId"] = "anonymous",
                    ["userId"] = "anonymous"
                })
            }));

    public static UiSurface InstalledBundles() => new(
        UiSurfaceKinds.InstalledBundles,
        WithCommon(
            surfaceId: "surface.installed-bundles",
            emitter: "market-main",
            title: "Installed Bundles",
            layout: UiSurfaceLayouts.Panel,
            priority: 11,
            props: new Dictionary<string, object?>
            {
                ["bundles"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "DigitalBrain.UI.Workbench",
                        ["version"] = "0.1.0",
                        ["ownerId"] = "digitalbraintech",
                        ["installed"] = true,
                        ["status"] = "ready",
                        ["description"] = "Startup workbench experience.",
                        ["experienceCount"] = 1,
                        ["experiences"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["experienceId"] = "digitalbrain-ui-workbench-open",
                                ["name"] = "Open Workbench",
                                ["kind"] = "app",
                                ["status"] = "ready",
                                ["summary"] = "Launch the main DigitalBrain workbench.",
                                ["action"] = SynapseAction(
                                    "open-workbench",
                                    "Open",
                                    nameof(InoRequest),
                                    new Dictionary<string, object?>
                                    {
                                        ["prompt"] = "Open the DigitalBrain workbench experience.",
                                        ["sessionId"] = "workbench"
                                    })
                            }
                        }
                    }
                },
                ["experiences"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["experienceId"] = "digitalbrain-ui-workbench-open",
                        ["bundleName"] = "DigitalBrain.UI.Workbench",
                        ["name"] = "Open Workbench",
                        ["kind"] = "app",
                        ["status"] = "ready"
                    }
                }
            }));

    public static UiSurface Timeline() => new(
        UiSurfaceKinds.Timeline,
        WithCommon(
            surfaceId: "surface.timeline",
            emitter: "digitalbrain.journal",
            title: "Timeline",
            layout: UiSurfaceLayouts.Drawer,
            props: new Dictionary<string, object?>
            {
                ["events"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = nameof(DemoMessageSynapse),
                        ["title"] = "Demo message",
                        ["at"] = DateTimeOffset.UtcNow
                    }
                },
                ["filters"] = new Dictionary<string, object?>
                {
                    ["types"] = new[] { nameof(DemoMessageSynapse), nameof(InoResponse) }
                }
            }));

    public static UiSurface DataChart() => DataChart(
        surfaceId: "surface.data-chart.demo",
        emitter: "chart-main",
        spec: new ChartSpec(
            Title: "Sales by Month",
            ChartType: "bar",
            Data: new[]
            {
                new Dictionary<string, object?> { ["month"] = "Jan", ["sales"] = 12 },
                new Dictionary<string, object?> { ["month"] = "Feb", ["sales"] = 18 }
            },
            X: "month",
            Y: "sales",
            Summary: "2 rows. Bar chart of sales by month."));

    public static UiSurface TaskManager() => new(
        UiSurfaceKinds.TaskManager,
        WithCommon(
            surfaceId: "surface.task-manager.demo",
            emitter: "kernel",
            title: "Task Manager",
            layout: UiSurfaceLayouts.Panel,
            priority: 20,
            props: new Dictionary<string, object?>
            {
                ["totals"] = new Dictionary<string, object?> { ["active"] = 1, ["completed"] = 2, ["failed"] = 0 },
                ["tasks"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["correlationId"] = "t1",
                        ["shortHash"] = "abc123",
                        ["originNeuron"] = "demo",
                        ["originIcon"] = "task",
                        ["ageMs"] = 1234,
                        ["edgeCount"] = 3,
                        ["status"] = "running"
                    }
                }
            }));

    public static UiSurface DataChart(string surfaceId, string emitter, ChartSpec spec) => new(
        UiSurfaceKinds.DataChart,
        WithCommon(
            surfaceId: surfaceId,
            emitter: emitter,
            title: spec.Title,
            layout: UiSurfaceLayouts.Panel,
            priority: 6,
            props: new Dictionary<string, object?>
            {
                [UiSurfaceKeys.ChartSpec] = spec.ToProps(),
                ["chartType"] = spec.ChartType,
                ["data"] = spec.Data,
                ["x"] = spec.X,
                ["y"] = spec.Y,
                ["series"] = spec.Series,
                ["color"] = spec.Color,
                ["tooltip"] = spec.Tooltip,
                ["crosshair"] = spec.Crosshair,
                ["summary"] = spec.Summary
            }));

    public static UiSurface Chart(string surfaceId, string emitter, GraphicSpec spec) => new(
        UiSurfaceKinds.DataChart,
        WithCommon(
            surfaceId: surfaceId,
            emitter: emitter,
            title: spec.Title,
            layout: UiSurfaceLayouts.Panel,
            priority: 6,
            props: new Dictionary<string, object?>
            {
                [UiSurfaceKeys.ChartSpec] = spec.ToProps(),
                ["graphicSpec"] = spec.ToProps(),
                ["data"] = spec.Data,
                ["summary"] = spec.Summary
            }));

    public static IReadOnlyDictionary<string, object?> SynapseAction(
        string actionId,
        string label,
        string synapseType,
        IReadOnlyDictionary<string, object?>? props = null) =>
        UiSurfaceActions.SynapseAction(actionId, label, synapseType, props);

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

public static class UiSurfaceLiveData
{
    public static IReadOnlyList<UiSurface> BuildWorkbenchSurfaces(
        IEnumerable<(string TaskId, IReadOnlyList<Synapse> Timeline)> taskTimelines,
        IReadOnlyList<Synapse> graphTimeline,
        IReadOnlyList<Synapse> timelineEvents,
        int maxEvents = 20,
        IReadOnlyList<Synapse>? chartTimeline = null,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
        var surfaces = new List<UiSurface>
        {
            ActivityGraphFromTimeline(graphTimeline, maxEvents),
            TaskManagerFromTasks(taskTimelines.SelectMany(t => t.Timeline).ToList(), maxEvents, userId, sessionId)
        };

        surfaces.AddRange(ChartSurfacesFromTimeline(chartTimeline ?? timelineEvents, maxEvents));
        surfaces.Add(TimelineFromSynapses(timelineEvents, maxEvents));
        return surfaces;
    }

    public static UiSurface ActivityGraphFromTimeline(IReadOnlyList<Synapse> timeline, int maxEvents = 20)
    {
        var activity = timeline.OfType<ClusterActivity>().TakeLast(maxEvents).ToList();
        var nodes = activity
            .GroupBy(a => a.NodeId)
            .Select(g =>
            {
                var latest = g.Last();
                return new Dictionary<string, object?>
                {
                    ["id"] = latest.NodeId,
                    ["label"] = latest.NodeId,
                    ["activity"] = Math.Clamp(latest.Value, 0.0, 1.0)
                };
            })
            .ToArray();

        var edges = nodes
            .Zip(nodes.Skip(1), (from, to) => new Dictionary<string, object?>
            {
                ["from"] = from["id"],
                ["to"] = to["id"],
                ["value"] = 0.4
            })
            .ToArray();

        var events = timeline
            .Where(s => s is ClusterActivity or ThreeDGraphUpdate)
            .TakeLast(maxEvents)
            .Select(GraphEvent)
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.ActivityGraph,
            WithCommon(
                surfaceId: "surface.activity-graph.live",
                emitter: "digitalbrain.cluster",
                title: "Activity Graph",
                layout: UiSurfaceLayouts.Compact,
                priority: 5,
                props: new Dictionary<string, object?>
                {
                    ["nodes"] = nodes,
                    ["edges"] = edges,
                    ["events"] = events
                }));
    }

    public static UiSurface TimelineFromSynapses(IReadOnlyList<Synapse> timeline, int maxEvents = 20)
    {
        var events = timeline
            .OrderBy(s => s.Timestamp)
            .TakeLast(maxEvents)
            .Select(TimelineEvent)
            .ToArray();

        var filters = events
            .Select(e => e["type"])
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.Timeline,
            WithCommon(
                surfaceId: "surface.timeline.live",
                emitter: "digitalbrain.journal",
                title: "Timeline",
                layout: UiSurfaceLayouts.Drawer,
                priority: 2,
                props: new Dictionary<string, object?>
                {
                    ["events"] = events,
                    ["filters"] = new Dictionary<string, object?>
                    {
                        ["types"] = filters
                    }
                }));
    }

    public static IReadOnlyList<UiSurface> ChartSurfacesFromTimeline(IReadOnlyList<Synapse> timeline, int maxEvents = 20)
    {
        var generated = timeline
            .OfType<DataChartGenerated>()
            .Select(generated => generated.Surface);

        var direct = timeline
            .OfType<UiSurface>()
            .Where(surface => surface.Kind == UiSurfaceKinds.DataChart);

        return generated
            .Concat(direct)
            .TakeLast(maxEvents)
            .ToArray();
    }

    public static UiSurface TaskManagerFromTasks(
        IReadOnlyList<Synapse> taskEvents,
        int maxEvents = 10,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
        var created = taskEvents.OfType<TaskCreated>().ToList();
        var progresses = taskEvents.OfType<TaskProgress>().ToList();
        var completed = taskEvents.OfType<TaskCompleted>().ToList();
        var cancelled = taskEvents.OfType<TaskCancelled>().ToList();

        int activeCount = Math.Max(0, created.Count - completed.Count - cancelled.Count);

        var taskRows = created.TakeLast(maxEvents).Select(c =>
        {
            var latest = progresses.LastOrDefault(p => p.TaskId == c.TaskId);
            string status = completed.Any(x => x.TaskId == c.TaskId) ? "completed"
                : cancelled.Any(x => x.TaskId == c.TaskId) ? "cancelled"
                : latest != null ? "running:" + latest.Detail : "created";

            var row = new Dictionary<string, object?>
            {
                ["taskId"] = c.TaskId.Value,
                ["correlationId"] = c.SynapseId,
                ["shortHash"] = c.TaskId.Value.Length > 8 ? c.TaskId.Value[..8] : c.TaskId.Value,
                ["originNeuron"] = c.Sender?.Value ?? "kernel",
                ["originIcon"] = "task",
                ["ageMs"] = (int)(DateTimeOffset.UtcNow - c.Timestamp).TotalMilliseconds,
                ["edgeCount"] = 1,
                ["status"] = status,
                ["userId"] = userId,
                ["sessionId"] = sessionId
            };
            if (!completed.Any(x => x.TaskId == c.TaskId) && !cancelled.Any(x => x.TaskId == c.TaskId))
            {
                row["cancelAction"] = UiSurfaceSamples.SynapseAction(
                    "cancel-task",
                    "Cancel",
                    nameof(CancelTask),
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = c.TaskId.Value,
                        ["userId"] = userId,
                        ["sessionId"] = sessionId
                    });
            }

            return row;
        }).ToArray();

        var totals = new Dictionary<string, object?>
        {
            ["active"] = activeCount,
            ["completed"] = completed.Count,
            ["failed"] = 0
        };

        return new UiSurface(
            UiSurfaceKinds.TaskManager,
            WithCommon(
                surfaceId: "surface.task-manager.live",
                emitter: "kernel.task",
                title: "Task Manager",
                layout: UiSurfaceLayouts.Panel,
                priority: 20,
                props: new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["sessionId"] = sessionId,
                    ["totals"] = totals,
                    ["tasks"] = taskRows,
                    ["runAction"] = UiSurfaceSamples.SynapseAction(
                        "run-task",
                        "Run Task",
                        nameof(RunTask),
                        new Dictionary<string, object?>
                        {
                            ["userId"] = userId,
                            ["sessionId"] = sessionId
                        })
                }));
    }

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

    private static Dictionary<string, object?> GraphEvent(Synapse synapse) =>
        synapse switch
        {
            ClusterActivity activity => new Dictionary<string, object?>
            {
                ["type"] = nameof(ClusterActivity),
                ["nodeId"] = activity.NodeId,
                ["activity"] = activity.Activity,
                ["value"] = activity.Value,
                ["at"] = activity.Timestamp
            },
            ThreeDGraphUpdate update => new Dictionary<string, object?>
            {
                ["type"] = nameof(ThreeDGraphUpdate),
                ["graphId"] = update.GraphId,
                ["dataJson"] = update.DataJson,
                ["at"] = update.Timestamp
            },
            _ => TimelineEvent(synapse)
        };

    private static Dictionary<string, object?> TimelineEvent(Synapse synapse) =>
        new()
        {
            ["type"] = synapse.Type,
            ["title"] = TitleFor(synapse),
            ["at"] = synapse.Timestamp,
            ["sender"] = synapse.Sender?.Value,
            ["receiver"] = synapse.Receiver?.Value
        };

    private static string TitleFor(Synapse synapse) =>
        synapse switch
        {
            ClusterActivity activity => $"{activity.NodeId}: {activity.Activity}",
            ThreeDGraphUpdate update => "Graph update: " + update.GraphId,
            DataChartGenerated generated => "Chart generated: " + generated.RequestId,
            DataChartFailed failed => "Chart failed: " + failed.Reason,
            InoResponse response => response.Response,
            _ => synapse.Type
        };

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
