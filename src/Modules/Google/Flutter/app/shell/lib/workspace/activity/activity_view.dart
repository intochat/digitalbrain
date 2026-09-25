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

class ActivityView extends StatelessWidget {
  const ActivityView({super.key, required this.controller});
  final ActivityController controller;

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: controller,
    builder: (context, _) => LayoutBuilder(
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
                content: Text('Activity connection interrupted. Reconnecting…'),
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
    ),
  );

  Widget _toolbar(BuildContext context) {
    final min = controller.replayFirstSequence;
    final max = controller.replayLastSequence;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      child: Row(
        children: [
          const Text(
            'Neuron activity',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
          ),
          const Spacer(),
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

  Widget _graph() => UiGraph(
    nodes: controller.nodes,
    edges: controller.edges,
    pulse: controller.pulse,
    highlightEdgeId: controller.highlightEdgeId,
    onNodeTap: (node) => controller.selectNode(node.id),
    onEdgeTap: (edge) => controller.selectRoute(edge.sourceId, edge.targetId),
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
                    '${item.sourceId ?? 'External'} → ${item.targetId ?? 'Unknown target'}\n${item.at.toLocal()}${item.durationMs == null ? '' : ' · ${item.durationMs!.toStringAsFixed(1)} ms'}${item.failureCode == null ? '' : ' · ${item.failureCode}'}',
                  ),
                  isThreeLine: true,
                ),
            ],
          ),
        ),
      ],
    );
  }
}
