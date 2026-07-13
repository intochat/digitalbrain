using DigitalBrain.Core;
using DigitalBrain.Ui.Contracts;

namespace DigitalBrain.Ui.Runtime;

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
                        ["id"] = "automation-main",
                        ["label"] = "Automations",
                        ["activity"] = 0.4
                    }
                },
                ["edges"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["from"] = "ino-main",
                        ["to"] = "automation-main",
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
                    SynapseAction("cancel-task", "Cancel", nameof(CancelTask), new Dictionary<string, object?>
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
                ["submitAction"] = SynapseAction("ask-ino", "Ask INO", "ino.interact", new Dictionary<string, object?>
                {
                    ["sessionId"] = "workbench"
                }),
                ["cancelAction"] = SynapseAction("dismiss-input", "Dismiss", nameof(CancelTask), new Dictionary<string, object?>
                {
                    ["taskId"] = "task-demo-1"
                })
            }));

    public static UiSurface Workspace() =>
        UiSurfaceLiveData.WorkspaceBoundary("anonymous", WorkspaceIds.Default, "workbench");

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
                        ["type"] = "ino.interact.completed",
                        ["title"] = "Assistant response",
                        ["at"] = DateTimeOffset.UtcNow
                    }
                },
                ["filters"] = new Dictionary<string, object?>
                {
                    ["types"] = new[] { "ino.interact.completed", nameof(TaskCompleted) }
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
            WorkspaceBoundary(userId, WorkspaceIds.Default, sessionId),
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

    public static UiSurface WorkspaceBoundary(
        string userId = "anonymous",
        string? workspaceId = null,
        string? clientId = null)
    {
        userId = EffectiveUserId(userId);
        workspaceId = WorkspaceIds.Effective(workspaceId);
        var vectorCollection = WorkspaceIds.VectorCollection(new UserId(userId), workspaceId);
        var sources = new[]
        {
            "Uploaded files",
            "Salesforce connection scope",
            "Chat/session memory",
            "Vector collection: " + vectorCollection
        };
        var isolation = new Dictionary<string, object?>
        {
            ["userId"] = userId,
            ["workspaceId"] = workspaceId,
            ["clientId"] = clientId,
            ["packConfigScope"] = PackConfigScopes.ForUser(new UserId(userId)),
            ["vectorCollection"] = vectorCollection
        };

        var tree = new UiWidgetTree("column", new Dictionary<string, object?>(),
        [
            new("fcard", new Dictionary<string, object?>
            {
                ["title"] = "Active workspace",
                ["subtitle"] = workspaceId
            }, new[]
            {
                new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "User: " + userId }),
                new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Client: " + (clientId ?? "none") })
            }),
            new("fcard", new Dictionary<string, object?>
            {
                ["title"] = "Context sources",
                ["subtitle"] = sources.Length.ToString()
            }, sources.Select(source => new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = source })).ToArray()),
            new("fcard", new Dictionary<string, object?>
            {
                ["title"] = "Isolation boundary",
                ["subtitle"] = vectorCollection
            }, isolation.Select(pair => new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = pair.Key + ": " + pair.Value })).ToArray())
        ]);

        return new UiSurface(
            UiSurfaceKinds.Workspace,
            WithCommon(
                surfaceId: "surface.workspace." + userId,
                emitter: "session-main",
                title: "Workspace",
                layout: UiSurfaceLayouts.Panel,
                priority: 12,
                props: new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["workspaceId"] = workspaceId,
                    ["clientId"] = clientId,
                    ["activeWorkspace"] = workspaceId,
                    ["contextSources"] = sources,
                    ["isolation"] = isolation,
                    ["tree"] = tree
                }));
    }

    public static UiSurface TaskManagerFromTasks(
        IReadOnlyList<Synapse> taskEvents,
        int maxEvents = 10,
        string userId = "anonymous",
        string? clientId = null)
    {
        userId = EffectiveUserId(userId);
        var created = taskEvents.OfType<TaskCreated>().ToList();
        var progresses = taskEvents.OfType<TaskProgress>().ToList();
        var completed = taskEvents.OfType<TaskCompleted>().ToList();
        var cancelled = taskEvents.OfType<TaskCancelled>().ToList();
        var completedIds = completed.Select(c => c.TaskId).ToHashSet();
        var cancelledIds = cancelled.Select(c => c.TaskId).ToHashSet();
        var createdIds = created.Select(c => c.TaskId).Distinct().ToArray();

        int activeCount = createdIds.Count(id => !completedIds.Contains(id) && !cancelledIds.Contains(id));

        var taskRows = created
            .GroupBy(c => c.TaskId)
            .Select(g => g.Last())
            .TakeLast(maxEvents)
            .Select(c =>
        {
            var latest = progresses.LastOrDefault(p => p.TaskId == c.TaskId);
            var completion = completed.LastOrDefault(x => x.TaskId == c.TaskId);
            var cancellation = cancelled.LastOrDefault(x => x.TaskId == c.TaskId);
            string status = completion is not null ? "completed"
                : cancellation is not null ? "cancelled"
                : latest != null ? "running:" + latest.Detail : "created";
            string state = completion is not null ? "completed"
                : cancellation is not null ? "cancelled"
                : "active";
            var eventRows = taskEvents
                .Where(e => IsTaskEventFor(e, c.TaskId))
                .OrderBy(e => e.Timestamp)
                .Select(TaskEventRow)
                .ToArray();

            var row = new Dictionary<string, object?>
            {
                ["taskId"] = c.TaskId.Value,
                ["description"] = c.Description,
                ["correlationId"] = c.SynapseId,
                ["shortHash"] = c.TaskId.Value.Length > 8 ? c.TaskId.Value[..8] : c.TaskId.Value,
                ["originNeuron"] = c.Sender?.Value ?? "kernel",
                ["originIcon"] = "task",
                ["ageMs"] = (int)(DateTimeOffset.UtcNow - c.Timestamp).TotalMilliseconds,
                ["edgeCount"] = 1,
                ["state"] = state,
                ["status"] = status,
                ["latestProgress"] = latest?.Detail,
                ["result"] = completion?.Result,
                ["events"] = eventRows,
                ["completed"] = completion is not null,
                ["cancelled"] = cancellation is not null,
                ["userId"] = userId,
                ["clientId"] = clientId
            };
            if (completion is null && cancellation is null)
            {
                row["cancelAction"] = UiSurfaceSamples.SynapseAction(
                    "cancel-task",
                    "Cancel",
                    nameof(CancelTask),
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = c.TaskId.Value,
                        ["userId"] = userId,
                        ["sessionId"] = clientId
                    });
            }

            return row;
        }).ToArray();

        var totals = new Dictionary<string, object?>
        {
            ["active"] = activeCount,
            ["completed"] = completed.Count,
            ["cancelled"] = cancelled.Count,
            ["failed"] = 0
        };
        var runAction = UiSurfaceSamples.SynapseAction(
            "run-task",
            "Run Task",
            nameof(RunTask),
            new Dictionary<string, object?>
            {
                ["userId"] = userId,
                ["sessionId"] = clientId
            });

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
                    ["clientId"] = clientId,
                    ["totals"] = totals,
                    ["tasks"] = taskRows,
                    ["runAction"] = runAction,
                    ["tree"] = TaskManagerTree(taskRows, totals, runAction)
                }));
    }

    private static UiWidgetTree TaskManagerTree(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> taskRows,
        IReadOnlyDictionary<string, object?> totals,
        IReadOnlyDictionary<string, object?> runAction)
    {
        var children = new List<UiWidgetTree>
        {
            new("text", new Dictionary<string, object?>
            {
                ["text"] = $"Active: {totals["active"]}  Completed: {totals["completed"]}  Cancelled: {totals["cancelled"]}"
            }),
            new("row", new Dictionary<string, object?>(), new[]
            {
                new UiWidgetTree("fbutton", new Dictionary<string, object?>(runAction))
            })
        };

        var active = taskRows.Where(IsActiveTaskRow).ToArray();
        var completed = taskRows.Where(row => Equals(row.GetValueOrDefault("state"), "completed")).ToArray();
        var cancelled = taskRows.Where(row => Equals(row.GetValueOrDefault("state"), "cancelled")).ToArray();

        children.Add(TaskSectionTree("Active", active));
        children.Add(TaskSectionTree("Completed", completed));
        children.Add(TaskSectionTree("Cancelled", cancelled));

        return new UiWidgetTree("column", new Dictionary<string, object?>(), children);
    }

    private static UiWidgetTree TaskSectionTree(
        string title,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var children = rows.Count == 0
            ?
            [
                new("text", new Dictionary<string, object?> { ["text"] = "No tasks" })
            ]
            : rows.Select(TaskCardTree).ToList();

        return new UiWidgetTree("fcard", new Dictionary<string, object?>
        {
            ["title"] = title,
            ["subtitle"] = rows.Count.ToString()
        }, children);
    }

    private static UiWidgetTree TaskCardTree(IReadOnlyDictionary<string, object?> row)
    {
        var taskId = StringProp(row, "taskId", "task");
        var description = StringProp(row, "description");
        var status = StringProp(row, "status", "created");
        var latest = StringProp(row, "latestProgress");
        var result = StringProp(row, "result");
        var children = new List<UiWidgetTree>();

        if (!string.IsNullOrWhiteSpace(description))
        {
            children.Add(new("text", new Dictionary<string, object?> { ["text"] = description }));
        }

        if (!string.IsNullOrWhiteSpace(latest))
        {
            children.Add(new("text", new Dictionary<string, object?> { ["text"] = "Progress: " + latest }));
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            children.Add(new("text", new Dictionary<string, object?> { ["text"] = "Result: " + result }));
        }

        if (row.TryGetValue("events", out var rawEvents) &&
            rawEvents is IEnumerable<IReadOnlyDictionary<string, object?>> events)
        {
            var eventText = string.Join(" -> ", events.Select(e => StringProp(e, "label")).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(eventText))
            {
                children.Add(new("text", new Dictionary<string, object?> { ["text"] = "Events: " + eventText }));
            }
        }

        if (row.TryGetValue("cancelAction", out var rawAction) &&
            rawAction is IReadOnlyDictionary<string, object?> cancelAction)
        {
            children.Add(new("row", new Dictionary<string, object?>(), new[]
            {
                new UiWidgetTree("fbutton", new Dictionary<string, object?>(cancelAction))
            }));
        }

        return new UiWidgetTree("fcard", new Dictionary<string, object?>
        {
            ["title"] = taskId,
            ["subtitle"] = status
        }, children);
    }

    private static bool IsActiveTaskRow(IReadOnlyDictionary<string, object?> row) =>
        !Equals(row.GetValueOrDefault("state"), "completed") &&
        !Equals(row.GetValueOrDefault("state"), "cancelled");

    private static bool IsTaskEventFor(Synapse synapse, TaskId taskId) =>
        synapse switch
        {
            TaskCreated e => e.TaskId == taskId,
            TaskStarted e => e.TaskId == taskId,
            TaskProgress e => e.TaskId == taskId,
            TaskCompleted e => e.TaskId == taskId,
            TaskCancelled e => e.TaskId == taskId,
            _ => false
        };

    private static Dictionary<string, object?> TaskEventRow(Synapse synapse) =>
        synapse switch
        {
            TaskCreated e => new()
            {
                ["type"] = nameof(TaskCreated),
                ["label"] = "created",
                ["detail"] = e.Description,
                ["at"] = e.Timestamp
            },
            TaskStarted e => new()
            {
                ["type"] = nameof(TaskStarted),
                ["label"] = "started",
                ["detail"] = null,
                ["at"] = e.Timestamp
            },
            TaskProgress e => new()
            {
                ["type"] = nameof(TaskProgress),
                ["label"] = e.Detail,
                ["detail"] = e.Detail,
                ["at"] = e.Timestamp
            },
            TaskCompleted e => new()
            {
                ["type"] = nameof(TaskCompleted),
                ["label"] = "completed",
                ["detail"] = e.Result,
                ["at"] = e.Timestamp
            },
            TaskCancelled e => new()
            {
                ["type"] = nameof(TaskCancelled),
                ["label"] = "cancelled",
                ["detail"] = null,
                ["at"] = e.Timestamp
            },
            _ => new()
            {
                ["type"] = synapse.Type,
                ["label"] = synapse.Type,
                ["detail"] = null,
                ["at"] = synapse.Timestamp
            }
        };

    private static string StringProp(IReadOnlyDictionary<string, object?> row, string key, string fallback = "") =>
        row.TryGetValue(key, out var value) ? value?.ToString() ?? fallback : fallback;

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
