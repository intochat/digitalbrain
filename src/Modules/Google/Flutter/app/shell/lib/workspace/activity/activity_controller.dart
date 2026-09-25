import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart' as graph;
import 'package:flutter/foundation.dart';

typedef ReadActivity = Future<ActivitySnapshot> Function();
typedef WatchActivity = Stream<ActivityUpdate> Function(
  int afterSequence,
  String? generation,
);

class ActivityController extends ChangeNotifier {
  ActivityController({required this.read, required this.watch});

  /// The observed boundary for calls initiated outside a neuron.
  static const externalSourceId = 'activity:external';

  static String? _sourceFor(ActivityRecord event) =>
      event.sourceId ??
      (event.kind != 'SignalPublished' && event.targetId != null
          ? externalSourceId
          : null);
  final ReadActivity read;
  final WatchActivity watch;
  StreamSubscription<ActivityUpdate>? _subscription;
  bool _disposed = false;
  List<ActivityRecord> _events = [];
  List<ActivityRecord>? _pausedEvents;
  int visibleCursor = 0;
  int _latestCursor = 0;
  bool paused = false;
  DateTime? _pausedAt;
  DateTime get visualNow => _pausedAt ?? DateTime.now().toUtc();
  bool stale = false;
  bool gap = false;
  bool loaded = false;
  String? selectedOperationId;
  String? selectedCorrelationId;
  String search = '';
  String kindFilter = 'All';
  String statusFilter = 'All';
  String? routeSourceId;
  String? routeTargetId;

  List<ActivityRecord> get _frame => _pausedEvents ?? _events;
  int get replayFirstSequence => _frame.isEmpty ? 0 : _frame.first.sequence;
  int get replayLastSequence => _frame.isEmpty ? 0 : _frame.last.sequence;
  List<ActivityRecord> get _frameEvents =>
      _frame.where((e) => e.sequence <= visibleCursor).toList(growable: false);

  List<ActivityRecord> get visibleEvents => _frameEvents
      .where((e) => kindFilter == 'All' || e.kind == kindFilter)
      .where((e) => statusFilter == 'All' || e.status == statusFilter)
      .where(
        (e) =>
            routeSourceId == null ||
            (_sourceFor(e) == routeSourceId && e.targetId == routeTargetId),
      )
      .where(
        (e) =>
            search.isEmpty ||
            '${e.type} ${_sourceFor(e)} ${e.targetId}'.toLowerCase().contains(
              search.toLowerCase(),
            ),
      )
      .toList(growable: false);

  List<ActivityRecord> get selectedSteps => _frame
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
    for (final event in _frameEvents) {
      if (_sourceFor(event) case final source?) ids.add(source);
      if (event.targetId != null) ids.add(event.targetId!);
    }
    final typeCounts = <String, int>{};
    for (final id in ids) {
      final type = id.split('/').first;
      typeCounts[type] = (typeCounts[type] ?? 0) + 1;
    }
    return [
      for (final id in ids.toList()..sort())
        graph.GraphNode(
          id: id,
          label: _nodeLabel(id, typeCounts[id.split('/').first] ?? 1),
          kind: id == externalSourceId
              ? graph.GraphNodeKind.entity
              : graph.GraphNodeKind.hub,
          iconKey: id == externalSourceId
              ? graph.NeuronIconKind.conversation.name
              : graph.NeuronIconKind.forGrainId(id).name,
        ),
    ];
  }

  static String _nodeLabel(String id, int count) {
    if (id == externalSourceId) return 'External call';
    final parts = id.split('/');
    if (parts.length == 1) return id;
    final words = parts.first.split(RegExp(r'[-.]'));
    final type = words
        .map(
          (word) => word.isEmpty
              ? word
              : '${word[0].toUpperCase()}${word.substring(1)}',
        )
        .join(' ');
    final instance = parts.last;
    if (instance.length <= 16 && !instance.contains('-')) {
      return '$type $instance';
    }
    return count > 1 ? '$type · ${instance.substring(0, 4)}' : type;
  }

  List<graph.GraphEdge> get edges {
    final routes = <String, graph.GraphEdge>{};
    for (final event in _frameEvents) {
      if (event.kind == 'SignalPublished' || event.targetId == null) {
        continue;
      }
      final source = _sourceFor(event)!;
      final id = '$source|${event.targetId}|call';
      routes[id] = graph.GraphEdge(
        id: id,
        sourceId: source,
        targetId: event.targetId!,
      );
    }
    return routes.values.toList(growable: false);
  }

  List<graph.GraphEdge> get tracePath {
    final correlation = selectedCorrelationId;
    if (correlation == null) return const [];
    final calls =
        _frameEvents
            .where(
              (event) =>
                  event.kind == 'CallStarted' &&
                  event.correlationId == correlation &&
                  event.targetId != null,
            )
            .toList()
          ..sort((a, b) => a.sequence.compareTo(b.sequence));
    return [
      for (var i = 0; i < calls.length; i++)
        graph.GraphEdge(
          id: 'trace:$correlation:${i + 1}',
          sourceId: _sourceFor(calls[i])!,
          targetId: calls[i].targetId!,
          label: '${i + 1}',
        ),
    ];
  }

  List<graph.GraphPulse> get pulses {
    final latest = <String, ActivityRecord>{};
    final starts = <String, DateTime>{};
    for (final event in visibleEvents.reversed.take(80).toList().reversed) {
      if (event.kind == 'CallStarted') {
        starts.putIfAbsent(event.operationId, () => event.at);
      }
      // A publication is an observation in its own right even if subsequent
      // calls share the operation id. The correlation remains in the list.
      final key = event.kind == 'SignalPublished'
          ? event.id
          : event.operationId;
      latest[key] = event;
    }
    final operations = latest.values.toList()
      ..sort((a, b) => a.sequence.compareTo(b.sequence));
    return [
      for (final event in operations.reversed.take(10).toList().reversed)
        if (_sourceFor(event) != null &&
            (event.kind == 'SignalPublished' || event.targetId != null))
          graph.GraphPulse(
            fromId: _sourceFor(event)!,
            toId: event.kind == 'SignalPublished'
                ? _sourceFor(event)!
                : event.targetId!,
            signature: event.id,
            operationId: event.kind == 'SignalPublished'
                ? event.id
                : event.operationId,
            at: event.kind == 'SignalPublished'
                ? event.at
                : starts[event.operationId] ?? event.at,
            updatedAt: event.at,
            outcome: switch (event.kind) {
              'CallArrived' => graph.GraphPulseOutcome.arrived,
              'CallCompleted' => graph.GraphPulseOutcome.completed,
              'CallFailed' => graph.GraphPulseOutcome.failed,
              'SignalPublished' => graph.GraphPulseOutcome.signal,
              _ => graph.GraphPulseOutcome.inFlight,
            },
          ),
    ];
  }

  graph.GraphPulse? get pulse => selectedEvent == null
      ? pulses.lastOrNull
      : pulses.where((p) => p.signature == selectedEvent!.id).firstOrNull;

  String? get highlightEdgeId {
    final event = selectedEvent;
    if (event?.targetId == null) return null;
    return '${_sourceFor(event!)}|${event.targetId}|call';
  }

  Future<void> start() async {
    final snapshot = await read();
    if (_disposed) return;
    _replace(snapshot);
    loaded = true;
    notifyListeners();
    _subscription = watch(snapshot.nextSequence, snapshot.generation).listen(
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
            gap = true;
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
    routeSourceId = null;
    routeTargetId = null;
    notifyListeners();
  }

  void selectRoute(String sourceId, String targetId) {
    routeSourceId = sourceId;
    routeTargetId = targetId;
    search = '';
    notifyListeners();
  }

  void clearSelection() {
    selectedOperationId = null;
    selectedCorrelationId = null;
    search = '';
    routeSourceId = null;
    routeTargetId = null;
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
    _pausedAt = DateTime.now().toUtc();
    _pausedEvents = List.of(_events);
    notifyListeners();
  }

  void seek(int sequence) {
    if (!paused) pause();
    visibleCursor = sequence.clamp(replayFirstSequence, replayLastSequence);
    notifyListeners();
  }

  Future<void> resumeLive() async {
    final snapshot = await read();
    if (_disposed) return;
    paused = false;
    _pausedAt = null;
    _pausedEvents = null;
    _replace(snapshot);
    notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    unawaited(_subscription?.cancel());
    super.dispose();
  }
}
