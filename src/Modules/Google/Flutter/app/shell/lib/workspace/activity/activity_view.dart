import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import 'activity_controller.dart';

class ActivityScreen extends StatefulWidget {
  const ActivityScreen({
    super.key,
    required this.workspaceId,
    required this.client,
    required this.active,
  });
  final String workspaceId;
  final DigitalBrainUiClient client;
  final bool active;

  @override
  State<ActivityScreen> createState() => _ActivityScreenState();
}

class _ActivityScreenState extends State<ActivityScreen> {
  ActivityController? controller;
  String? error;

  @override
  void initState() {
    super.initState();
    _sync();
  }

  @override
  void didUpdateWidget(covariant ActivityScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.workspaceId != widget.workspaceId ||
        oldWidget.client != widget.client ||
        oldWidget.active != widget.active) {
      controller?.dispose();
      controller = null;
      _sync();
    }
  }

  void _sync() {
    if (!widget.active) return;
    error = null;
    final next = ActivityController(
      read: () => widget.client.readActivity(widget.workspaceId),
      watch: (cursor, generation) => widget.client.watchActivity(
        widget.workspaceId,
        afterSequence: cursor,
        generation: generation,
      ),
    );
    controller = next;
    unawaited(
      next.start().catchError((Object failure) {
        if (mounted && identical(controller, next)) {
          setState(() => error = '$failure');
        }
      }),
    );
  }

  @override
  void dispose() {
    controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.active) {
      return const Center(child: Text('Activity paused while hidden.'));
    }
    if (error != null) {
      return Center(child: Text('Could not load activity: $error'));
    }
    return ActivityView(controller: controller!);
  }
}

enum ActivityGraphMode { lumen, graph, spatial }

class ActivityView extends StatefulWidget {
  const ActivityView({
    super.key,
    required this.controller,
    this.spatialSceneFactory,
  });
  final ActivityController controller;
  final GraphSceneFactory? spatialSceneFactory;

  @override
  State<ActivityView> createState() => _ActivityViewState();
}

class _ActivityViewState extends State<ActivityView> {
  ActivityGraphMode mode = ActivityGraphMode.graph;
  Timer? _indicatorExpiry;
  bool _openedLumen = false;
  bool _openedSpatial = false;
  ActivityController get controller => widget.controller;

  void _scheduleIndicatorExpiry() {
    _indicatorExpiry?.cancel();
    if (controller.paused || mode != ActivityGraphMode.lumen) return;
    final now = controller.visualNow;
    final expiries =
        controller.pulses
            .where((pulse) => pulse.at != null)
            .map(
              (pulse) =>
                  (pulse.updatedAt ?? pulse.at!).add(GraphPulse.trailLifetime),
            )
            .where((expiry) => expiry.isAfter(now))
            .toList()
          ..sort();
    if (expiries.isNotEmpty) {
      _indicatorExpiry = Timer(const Duration(milliseconds: 80), () {
        if (mounted) setState(() {});
      });
    }
  }

  @override
  void dispose() {
    _indicatorExpiry?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: controller,
    builder: (context, _) {
      _scheduleIndicatorExpiry();
      return LayoutBuilder(
        builder: (context, constraints) {
          final compact = constraints.maxWidth < 700;
          final graph = _graph();
          final list = _list(context);
          final detail = _detail(context);
          return Column(
            children: [
              _toolbar(context),
              if (controller.gap)
                const MaterialBanner(
                  content: Text(
                    'Some earlier activity was lost. This path may be incomplete.',
                  ),
                  actions: [SizedBox.shrink()],
                ),
              if (controller.stale)
                const MaterialBanner(
                  content: Text(
                    'Activity connection interrupted. Reconnecting…',
                  ),
                  actions: [SizedBox.shrink()],
                ),
              Expanded(
                child: compact
                    ? Column(
                        children: [
                          Expanded(child: graph),
                          Expanded(
                            child: controller.selectedOperationId == null
                                ? list
                                : detail,
                          ),
                        ],
                      )
                    : Row(
                        children: [
                          SizedBox(width: 270, child: list),
                          Expanded(child: graph),
                          SizedBox(width: 310, child: detail),
                        ],
                      ),
              ),
            ],
          );
        },
      );
    },
  );

  Widget _toolbar(BuildContext context) {
    final min = controller.replayFirstSequence;
    final max = controller.replayLastSequence;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      child: Wrap(
        spacing: 12,
        runSpacing: 4,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          const Text(
            'Neuron activity',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
          ),
          SegmentedButton<ActivityGraphMode>(
            segments: const [
              ButtonSegment(
                value: ActivityGraphMode.lumen,
                label: Text('Lumen'),
              ),
              ButtonSegment(
                value: ActivityGraphMode.graph,
                label: Text('Graph'),
              ),
              ButtonSegment(
                value: ActivityGraphMode.spatial,
                label: Text('3D'),
              ),
            ],
            selected: {mode},
            onSelectionChanged: (selection) => setState(() {
              mode = selection.single;
              if (mode == ActivityGraphMode.lumen) _openedLumen = true;
              if (mode == ActivityGraphMode.spatial) _openedSpatial = true;
            }),
          ),
          if (controller.paused)
            SizedBox(
              width: 160,
              child: Slider(
                value: controller.visibleCursor.clamp(min, max).toDouble(),
                min: min.toDouble(),
                max: max > min ? max.toDouble() : min + 1.0,
                onChanged: (value) => controller.seek(value.round()),
              ),
            ),
          TextButton.icon(
            onPressed: controller.paused
                ? controller.resumeLive
                : controller.pause,
            icon: Icon(controller.paused ? Icons.play_arrow : Icons.pause),
            label: Text(controller.paused ? 'Live' : 'Pause'),
          ),
        ],
      ),
    );
  }

  Widget _graph() => Stack(
    fit: StackFit.expand,
    children: [
      if (_openedLumen)
        Offstage(
          key: const ValueKey('activity_lumen_pane'),
          offstage: mode != ActivityGraphMode.lumen,
          child: _lumenGraph(),
        ),
      Offstage(
        key: const ValueKey('activity_graph_pane'),
        offstage: mode != ActivityGraphMode.graph,
        child: _compactGraph(),
      ),
      if (_openedSpatial)
        Offstage(
          key: const ValueKey('activity_spatial_pane'),
          offstage: mode != ActivityGraphMode.spatial,
          child: SpatialGraph(
            nodes: controller.nodes,
            edges: controller.edges,
            tracePath: controller.tracePath,
            pulses: controller.pulses,
            active: mode == ActivityGraphMode.spatial,
            playing: !controller.paused && !controller.stale,
            now: controller.visualNow,
            selectedEdgeId: controller.highlightEdgeId,
            selectedNodeId:
                controller.selectedEvent?.targetId ??
                controller.selectedEvent?.sourceId,
            onNodeTap: (node) => controller.selectNode(node.id),
            onEdgeTap: (edge) =>
                controller.selectRoute(edge.sourceId, edge.targetId),
            onFallback: () => setState(() => mode = ActivityGraphMode.graph),
            sceneFactory: widget.spatialSceneFactory,
          ),
        ),
      if (controller.loaded && controller.nodes.isEmpty)
        Center(
          child: Text(
            controller.gap
                ? 'Earlier activity expired. New calls will appear here.'
                : 'No neuron activity yet. Start a workspace action to see routes.',
            textAlign: TextAlign.center,
          ),
        ),
    ],
  );

  Widget _lumenGraph() => LumenBrainGraph(
    snapshot: BrainSnapshot(
      rootId: 'activity',
      observedAt: controller.visualNow,
      nodes: [
        for (final node in controller.nodes)
          BrainNeuron(
            id: node.id,
            type: 'neuron',
            name: node.id,
            label: node.label,
            module: node.cluster ?? '',
            iconKey: node.iconKey,
          ),
      ],
      synapses: [
        for (final edge in controller.edges)
          BrainSynapse(
            id: edge.id,
            sourceId: edge.sourceId,
            targetId: edge.targetId,
            signalType: '',
            kind: 'Observed call',
          ),
      ],
    ),
    selectedId:
        controller.selectedEvent?.targetId ??
        controller.selectedEvent?.sourceId,
    tracePath: controller.tracePath,
    activityPulses: controller.pulses,
    activityNow: controller.visualNow,
    activeNodes: {
      for (final pulse in controller.pulses)
        if (pulse.ageAt(controller.visualNow) >= 0 &&
            pulse.ageAt(controller.visualNow) < 1)
          pulse.fromId,
    },
    signalNodes: {
      for (final pulse in controller.pulses)
        if (pulse.outcome == GraphPulseOutcome.signal &&
            pulse.ageAt(controller.visualNow) >= 0 &&
            pulse.ageAt(controller.visualNow) < 1)
          pulse.fromId,
    },
    activeEdges: {
      for (final pulse in controller.pulses)
        if (!pulse.local &&
            pulse.ageAt(controller.visualNow) >= 0 &&
            pulse.ageAt(controller.visualNow) < 1)
          '${pulse.fromId}|${pulse.toId}|call',
    },
    onNeuron: (node) => controller.selectNode(node.id),
    onSynapse: (edge) =>
        controller.selectRoute(edge.sourceId, edge.targetId),
  );

  Widget _compactGraph() => UiGraph(
    nodes: controller.nodes,
    edges: controller.edges,
    tracePath: controller.tracePath,
    pulses: controller.pulses,
    now: controller.visualNow,
    playing: !controller.paused && !controller.stale,
    highlightEdgeId: controller.highlightEdgeId,
    onNodeTap: (node) => controller.selectNode(node.id),
    onEdgeTap: (edge) =>
        controller.selectRoute(edge.sourceId, edge.targetId),
  );

  Widget _list(BuildContext context) => Column(
    children: [
      Padding(
        padding: const EdgeInsets.all(8),
        child: TextField(
          decoration: const InputDecoration(
            prefixIcon: Icon(Icons.search),
            hintText: 'Find activity',
          ),
          onChanged: controller.setSearch,
        ),
      ),
      Row(
        children: [
          Expanded(
            child: DropdownButton<String>(
              value: controller.kindFilter,
              isExpanded: true,
              items: [
                'All',
                'CallStarted',
                'CallArrived',
                'CallCompleted',
                'CallFailed',
                'SignalPublished',
              ].map((v) => DropdownMenuItem(value: v, child: Text(v))).toList(),
              onChanged: (value) {
                if (value != null) controller.setKindFilter(value);
              },
            ),
          ),
          Expanded(
            child: DropdownButton<String>(
              value: controller.statusFilter,
              isExpanded: true,
              items: [
                'All',
                'started',
                'arrived',
                'completed',
                'failed',
                'published',
              ].map((v) => DropdownMenuItem(value: v, child: Text(v))).toList(),
              onChanged: (value) {
                if (value != null) controller.setStatusFilter(value);
              },
            ),
          ),
        ],
      ),
      Expanded(
        child: !controller.loaded
            ? const Center(child: CircularProgressIndicator())
            : controller.visibleEvents.isEmpty
            ? const Center(child: Text('No recent neuron activity'))
            : ListView.builder(
                itemCount: controller.visibleEvents.length,
                itemBuilder: (context, index) {
                  final item = controller.visibleEvents.reversed.elementAt(
                    index,
                  );
                  return ListTile(
                    key: Key('activity_event_${item.sequence}'),
                    selected:
                        item.operationId == controller.selectedOperationId,
                    onTap: () => controller.selectActivity(item.id),
                    title: Text(item.type),
                    subtitle: Text(
                      '${item.kind} · ${item.sourceId ?? 'External'} → ${item.targetId ?? 'Unknown target'}',
                    ),
                    trailing: item.kind == 'CallFailed'
                        ? const Icon(Icons.error_outline)
                        : null,
                  );
                },
              ),
      ),
    ],
  );

  Widget _detail(BuildContext context) {
    final steps = controller.selectedSteps;
    if (steps.isEmpty) {
      return const Center(
        child: Text('Select an activity to inspect its path.'),
      );
    }
    return Column(
      children: [
        if (controller.gap)
          const Padding(
            padding: EdgeInsets.all(8),
            child: Text(
              'Retained observations only; earlier steps may be missing.',
            ),
          ),
        Row(
          children: [
            const Expanded(
              child: Padding(
                padding: EdgeInsets.all(12),
                child: Text(
                  'Observed path',
                  style: TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
            ),
            IconButton(
              tooltip: 'Clear selection',
              onPressed: controller.clearSelection,
              icon: const Icon(Icons.close),
            ),
          ],
        ),
        Expanded(
          child: ListView(
            children: [
              for (final item in steps)
                ListTile(
                  title: Text('${item.kind}: ${item.type}'),
                  subtitle: Text(
                    '${_pathLabel(item)}\n${item.at.toLocal()}${item.durationMs == null ? '' : ' · ${item.durationMs!.toStringAsFixed(1)} ms'}${item.failureCode == null ? '' : ' · ${item.failureCode}'}',
                  ),
                  isThreeLine: true,
                ),
            ],
          ),
        ),
      ],
    );
  }

  String _pathLabel(ActivityRecord item) => item.kind == 'SignalPublished'
      ? 'Published at ${item.sourceId ?? 'Unknown neuron'}'
      : '${item.sourceId ?? 'External'} → ${item.targetId ?? 'Unknown target'}';
}
