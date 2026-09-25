import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart' as graph;
import 'package:flutter/foundation.dart';

typedef ReadActivity = Future<ActivitySnapshot> Function();
typedef WatchActivity = Stream<ActivityUpdate> Function(int afterSequence);

class ActivityController extends ChangeNotifier {
  ActivityController({required this.read, required this.watch});
  final ReadActivity read;
  final WatchActivity watch;
  StreamSubscription<ActivityUpdate>? _subscription;
  bool _disposed = false;
  List<ActivityRecord> _events = [];
  int visibleCursor = 0;
  int _latestCursor = 0;
  bool paused = false;
  bool stale = false;
  bool gap = false;
  bool loaded = false;
  String? selectedOperationId;
  String? selectedCorrelationId;
  String search = '';
  String kindFilter = 'All';
  String statusFilter = 'All';

  List<ActivityRecord> get visibleEvents => _events
      .where((e) => e.sequence <= visibleCursor)
      .where((e) => kindFilter == 'All' || e.kind == kindFilter)
      .where((e) => statusFilter == 'All' || e.status == statusFilter)
      .where(
        (e) =>
            search.isEmpty ||
            '${e.type} ${e.sourceId} ${e.targetId}'.toLowerCase().contains(
              search.toLowerCase(),
            ),
      )
      .toList(growable: false);

  List<ActivityRecord> get selectedSteps => _events
      .where(
        (e) =>
            e.sequence <= visibleCursor &&
            (selectedCorrelationId != null
                ? e.correlationId == selectedCorrelationId
                : e.operationId == selectedOperationId),
      )
      .toList(growable: false);

  ActivityRecord? get selectedEvent {
    for (final event in visibleEvents.reversed) {
      if (event.operationId == selectedOperationId) return event;
    }
    return null;
  }

  List<graph.GraphNode> get nodes {
    final ids = <String>{};
    for (final event in visibleEvents) {
      if (event.sourceId != null) ids.add(event.sourceId!);
      if (event.targetId != null) ids.add(event.targetId!);
    }
    return [
      for (final id in ids.toList()..sort())
        graph.GraphNode(
          id: id,
          label: id.split('/').last,
          kind: graph.GraphNodeKind.hub,
        ),
    ];
  }

  List<graph.GraphEdge> get edges {
    final routes = <String, graph.GraphEdge>{};
    for (final event in visibleEvents) {
      if (event.kind != 'CallStarted' ||
          event.sourceId == null ||
          event.targetId == null) {
        continue;
      }
      final id = '${event.sourceId}|${event.targetId}|call';
      routes[id] = graph.GraphEdge(
        id: id,
        sourceId: event.sourceId!,
        targetId: event.targetId!,
      );
    }
    return routes.values.toList(growable: false);
  }

  graph.GraphPulse? get pulse {
    final event = selectedEvent ?? visibleEvents.lastOrNull;
    if (event?.sourceId == null) {
      return null;
    }
    if (event!.kind == 'SignalPublished') {
      return graph.GraphPulse(
        fromId: event.sourceId!,
        toId: event.sourceId!,
        signature: event.id,
      );
    }
    if (event.targetId == null) return null;
    return graph.GraphPulse(
      fromId: event.sourceId!,
      toId: event.targetId!,
      signature: event.id,
    );
  }

  String? get highlightEdgeId {
    final event = selectedEvent;
    if (event?.sourceId == null || event?.targetId == null) return null;
    return '${event!.sourceId}|${event.targetId}|call';
  }

  Future<void> start() async {
    final snapshot = await read();
    if (_disposed) return;
    _replace(snapshot);
    loaded = true;
    notifyListeners();
    _subscription = watch(snapshot.nextSequence).listen(
      _onUpdate,
      onError: (_) {
        if (_disposed) return;
        stale = true;
        notifyListeners();
      },
    );
  }

  void _replace(ActivitySnapshot snapshot) {
    _events = snapshot.events;
    _latestCursor = snapshot.nextSequence;
    if (!paused) visibleCursor = _latestCursor;
    gap = snapshot.gap;
    stale = false;
  }

  void _onUpdate(ActivityUpdate update) {
    if (_disposed) return;
    switch (update) {
      case ActivityItem(:final event):
        if (event.sequence > _latestCursor) {
          _events.add(event);
          if (_events.length > 2000) {
            _events.removeRange(0, _events.length - 2000);
          }
          _latestCursor = event.sequence;
          if (!paused) visibleCursor = _latestCursor;
        }
        stale = false;
      case ActivityGap(:final snapshot):
        _replace(snapshot);
        gap = true;
      case ActivityDisconnected():
        stale = true;
    }
    notifyListeners();
  }

  void selectActivity(String id) {
    for (final event in visibleEvents) {
      if (event.id == id) {
        selectedOperationId = event.operationId;
        selectedCorrelationId = event.correlationId;
        notifyListeners();
        return;
      }
    }
  }

  void selectNode(String id) {
    search = id;
    notifyListeners();
  }

  void clearSelection() {
    selectedOperationId = null;
    selectedCorrelationId = null;
    search = '';
    notifyListeners();
  }

  void setSearch(String value) {
    search = value;
    notifyListeners();
  }

  void setKindFilter(String value) {
    kindFilter = value;
    notifyListeners();
  }

  void setStatusFilter(String value) {
    statusFilter = value;
    notifyListeners();
  }

  void pause() {
    paused = true;
    notifyListeners();
  }

  void seek(int sequence) {
    paused = true;
    visibleCursor = sequence;
    notifyListeners();
  }

  void resumeLive() {
    paused = false;
    visibleCursor = _latestCursor;
    notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    unawaited(_subscription?.cancel());
    super.dispose();
  }
}
