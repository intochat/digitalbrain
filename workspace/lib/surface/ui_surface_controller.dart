import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';

import 'feed_cursor_store.dart';
import 'ui_surface_client.dart';
import 'ui_surface_models.dart';
import 'ui_surface_patcher.dart';

class UiSurfaceController extends ChangeNotifier {
  UiSurfaceController({
    required this.client,
    required this.cursorStore,
  }) : _feedCursor = cursorStore.read() ?? 0;

  final UiSurfaceClient client;
  final FeedCursorStore cursorStore;
  final Map<String, UiSurface> _surfaces = {};
  final Set<String> _snapshotInFlight = {};

  StreamSubscription<UiFeedMessage>? _subscription;
  int _feedCursor;
  String? _closedFailure;
  String? _sanitizedFailure;
  bool _started = false;

  int get feedCursor => _feedCursor;
  String? get closedFailure => _closedFailure;
  String? get sanitizedFailure => _sanitizedFailure;
  List<UiSurface> get surfaces =>
      List<UiSurface>.unmodifiable(_surfaces.values);

  UiSurface? surface(String surfaceId) => _surfaces[surfaceId];

  Future<void> start() async {
    if (_started) {
      return;
    }
    _started = true;
    final cursor = cursorStore.read() ?? 0;
    _feedCursor = cursor;
    _subscription = client.watch(cursor: cursor).listen(_onMessage);
  }

  bool ingestRaw(String raw) {
    Map<String, dynamic> decoded;
    try {
      final value = jsonDecode(raw);
      if (value is! Map<String, dynamic>) {
        return false;
      }
      decoded = value;
    } on FormatException {
      return false;
    }

    final schemaVersion = decoded['schemaVersion'];
    if (schemaVersion is int &&
        schemaVersion != UiFeedMessage.supportedSchemaVersion) {
      _closedFailure = 'unsupported schema version $schemaVersion';
      notifyListeners();
      return false;
    }

    final message = UiFeedMessage.parse(decoded);
    if (message == null) {
      return false;
    }
    _onMessage(message);
    return true;
  }

  Future<void> sendAction({
    required String surfaceId,
    required String actionId,
  }) async {
    final current = _surfaces[surfaceId];
    if (current == null) {
      return;
    }
    await client.sendSurfaceAction(
      surfaceId: surfaceId,
      actionId: actionId,
      expectedRevision: current.revision,
    );
  }

  void _onMessage(UiFeedMessage message) {
    if (message.sequence > _feedCursor) {
      _feedCursor = message.sequence;
      cursorStore.write(_feedCursor);
    }

    switch (message) {
      case UiSnapshotMessage(:final snapshot):
        _applySnapshot(snapshot.surface);
      case UiPatchMessage(:final patch):
        _applyPatch(patch);
      case UiFailureMessage(:final text):
        _sanitizedFailure = text;
        notifyListeners();
    }
  }

  void _applySnapshot(UiSurface surface) {
    _surfaces[surface.surfaceId] = surface;
    _closedFailure = null;
    notifyListeners();
  }

  void _applyPatch(UiSurfacePatch patch) {
    final current = _surfaces[patch.surfaceId];
    if (current == null) {
      unawaited(_requestSnapshot(patch.surfaceId));
      return;
    }

    if (patch.toRevision <= current.revision) {
      return;
    }

    if (patch.fromRevision != current.revision) {
      unawaited(_requestSnapshot(patch.surfaceId));
      return;
    }

    final updated = UiSurfacePatcher.apply(current, patch);
    if (updated == null) {
      unawaited(_requestSnapshot(patch.surfaceId));
      return;
    }
    _surfaces[patch.surfaceId] = updated;
    notifyListeners();
  }

  Future<void> _requestSnapshot(String surfaceId) async {
    if (_snapshotInFlight.contains(surfaceId)) {
      return;
    }
    _snapshotInFlight.add(surfaceId);
    try {
      final snapshot = await client.fetchSnapshot(surfaceId);
      _applySnapshot(snapshot.surface);
    } finally {
      _snapshotInFlight.remove(surfaceId);
    }
  }

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
    _subscription = null;
    super.dispose();
  }
}
