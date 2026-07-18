import 'package:flutter/foundation.dart';

import 'activity_gateway.dart';
import 'activity_models.dart';

enum ActivityLoadState { idle, loading, ready, failed }

class ActivityFailure {
  const ActivityFailure({required this.message, required this.retryable});

  final String message;
  final bool retryable;
}

class ActivityController extends ChangeNotifier {
  ActivityController({required ActivityGateway gateway}) : _gateway = gateway;

  final ActivityGateway _gateway;
  final Map<String, String> _knownFeatures = {};

  ActivityLoadState _state = ActivityLoadState.idle;
  List<ActivityRun> _runs = const [];
  ActivityFailure? _failure;
  ActivityStatus? _statusFilter;
  ActivityOrigin? _originFilter;
  String? _featureFilter;
  String? _selectedRunId;
  var _requestFence = 0;
  var _disposed = false;

  ActivityLoadState get state => _state;
  List<ActivityRun> get runs => _runs;
  ActivityFailure? get failure => _failure;
  ActivityStatus? get statusFilter => _statusFilter;
  ActivityOrigin? get originFilter => _originFilter;
  String? get featureFilter => _featureFilter;
  bool get isInitialLoading =>
      _state == ActivityLoadState.loading && _runs.isEmpty;
  bool get isRefreshing =>
      _state == ActivityLoadState.loading && _runs.isNotEmpty;
  bool get hasActiveFilters =>
      _statusFilter != null || _originFilter != null || _featureFilter != null;

  List<ActivityFeature> get availableFeatures {
    final values = _knownFeatures.entries
        .map((entry) => ActivityFeature(id: entry.key, name: entry.value))
        .toList(growable: false);
    values.sort((left, right) {
      final nameOrder = left.name.toLowerCase().compareTo(
        right.name.toLowerCase(),
      );
      return nameOrder != 0 ? nameOrder : left.id.compareTo(right.id);
    });
    return List.unmodifiable(values);
  }

  List<ActivityRun> get filteredRuns => List.unmodifiable(
    _runs.where(
      (run) =>
          (_statusFilter == null || run.status == _statusFilter) &&
          (_originFilter == null || run.origin == _originFilter) &&
          (_featureFilter == null || run.featureId == _featureFilter),
    ),
  );

  ActivityRun? get selectedRun {
    final selectedId = _selectedRunId;
    if (selectedId == null) return null;
    for (final run in filteredRuns) {
      if (run.runId == selectedId) return run;
    }
    return null;
  }

  Future<void> load() async {
    final request = ++_requestFence;
    final status = _statusFilter;
    final origin = _originFilter;
    final featureId = _featureFilter;
    _state = ActivityLoadState.loading;
    _failure = null;
    _publish();
    try {
      final loaded = _verifiedRuns(
        await _gateway.loadRuns(
          status: status,
          origin: origin,
          featureId: featureId,
        ),
      );
      if (!_isCurrent(request)) return;
      _runs = loaded;
      _rememberFeatures(loaded);
      _repairFeatureFilter();
      _repairSelection();
      _state = ActivityLoadState.ready;
      _failure = null;
    } on _ActivityProjectionException {
      if (!_isCurrent(request)) return;
      _state = ActivityLoadState.failed;
      _failure = const ActivityFailure(
        message: 'Activity data could not be verified.',
        retryable: true,
      );
    } on Object {
      if (!_isCurrent(request)) return;
      _state = ActivityLoadState.failed;
      _failure = const ActivityFailure(
        message: "We couldn't load Activity. Try again.",
        retryable: true,
      );
    }
    _publish();
  }

  Future<void> refresh() => load();

  Future<void> retry() {
    if (_failure?.retryable == true) return load();
    return Future.value();
  }

  Future<void> setStatusFilter(ActivityStatus? value) {
    if (_statusFilter == value) return Future.value();
    _statusFilter = value;
    _repairSelection();
    return load();
  }

  Future<void> setOriginFilter(ActivityOrigin? value) {
    if (_originFilter == value) return Future.value();
    _originFilter = value;
    _repairSelection();
    return load();
  }

  Future<void> setFeatureFilter(String? value) {
    final accepted = value != null && _knownFeatures.containsKey(value)
        ? value
        : null;
    if (_featureFilter == accepted) return Future.value();
    _featureFilter = accepted;
    _repairSelection();
    return load();
  }

  Future<void> clearFilters() {
    if (!hasActiveFilters) return Future.value();
    _statusFilter = null;
    _originFilter = null;
    _featureFilter = null;
    _repairSelection();
    return load();
  }

  void selectRun(String runId) {
    if (_selectedRunId == runId ||
        !filteredRuns.any((run) => run.runId == runId)) {
      return;
    }
    _selectedRunId = runId;
    _publish();
  }

  List<ActivityRun> _verifiedRuns(List<ActivityRun> values) {
    final runIds = <String>{};
    final featureNames = <String, String>{};
    for (final run in values) {
      if (!runIds.add(run.runId)) throw const _ActivityProjectionException();
      final knownName = featureNames[run.featureId];
      if (knownName != null && knownName != run.featureName) {
        throw const _ActivityProjectionException();
      }
      featureNames[run.featureId] = run.featureName;
    }
    final ordered = List<ActivityRun>.of(values);
    ordered.sort((left, right) {
      final recencyOrder = (right.completedAt ?? right.occurredAt).compareTo(
        left.completedAt ?? left.occurredAt,
      );
      if (recencyOrder != 0) return recencyOrder;
      final occurrenceOrder = right.occurredAt.compareTo(left.occurredAt);
      return occurrenceOrder != 0
          ? occurrenceOrder
          : left.runId.compareTo(right.runId);
    });
    return List.unmodifiable(ordered);
  }

  void _repairFeatureFilter() {
    final feature = _featureFilter;
    if (feature != null && !_knownFeatures.containsKey(feature)) {
      _featureFilter = null;
    }
  }

  void _rememberFeatures(List<ActivityRun> values) {
    for (final run in values) {
      _knownFeatures[run.featureId] = run.featureName;
    }
  }

  void _repairSelection() {
    final visible = filteredRuns;
    if (visible.any((run) => run.runId == _selectedRunId)) return;
    _selectedRunId = visible.isEmpty ? null : visible.first.runId;
  }

  bool _isCurrent(int request) => !_disposed && request == _requestFence;

  void _publish() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _requestFence++;
    super.dispose();
  }
}

class _ActivityProjectionException implements Exception {
  const _ActivityProjectionException();
}
