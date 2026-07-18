import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../gateway/brain_gateway.dart';
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
    _subscription = client
        .watch(cursor: cursor)
        .listen(
          (message) {
            unawaited(_onMessage(message));
          },
          onError: _onTransportError,
          onDone: _onDone,
          cancelOnError: false,
        );
  }

  bool ingestRaw(String raw) {
    Map<String, dynamic> decoded;
    try {
      final value = jsonDecode(raw);
      if (value is! Map<String, dynamic>) {
        _closeProtocol('feed frame rejected');
        return false;
      }
      decoded = value;
    } on FormatException {
      _closeProtocol('feed frame rejected');
      return false;
    }

    try {
      final message = UiFeedMessage.parse(decoded);
      unawaited(_onMessage(message));
      return true;
    } on FormatException catch (error) {
      if (error.message.contains('schema')) {
        _closeProtocol('unsupported schema version');
      } else if (error.message.contains('sequence')) {
        _closeProtocol('invalid feed sequence');
      } else {
        _closeProtocol('feed frame rejected');
      }
      return false;
    }
  }

  Future<void> sendAction({
    required String surfaceId,
    required String actionId,
    required int expectedRevision,
  }) async {
    final current = _surfaces[surfaceId];
    if (current == null) {
      return;
    }

    if (expectedRevision != current.revision) {
      final recovered = await _recoverSnapshot(surfaceId);
      if (!recovered) {
        return;
      }
      final refreshed = _surfaces[surfaceId];
      if (refreshed == null || expectedRevision != refreshed.revision) {
        _closeProtocol('action revision conflict');
        return;
      }
    }

    try {
      await client.sendSurfaceAction(
        surfaceId: surfaceId,
        actionId: actionId,
        expectedRevision: expectedRevision,
      );
    } catch (error) {
      _onTransportError(error, StackTrace.current);
    }
  }

  Future<void> _onMessage(UiFeedMessage message) async {
    if (_closedFailure != null) {
      return;
    }
    if (message.sequence <= _feedCursor) {
      return;
    }

    switch (message) {
      case UiSnapshotMessage(:final snapshot):
        _applySnapshot(snapshot.surface);
        _commitCursor(message.sequence);
      case UiPatchMessage(:final patch):
        await _applyPatch(patch, message.sequence);
      case UiFailureMessage(:final text):
        _sanitizedFailure = text;
        _commitCursor(message.sequence);
        notifyListeners();
    }
  }

  void _applySnapshot(UiSurface surface) {
    _surfaces[surface.surfaceId] = surface;
    _closedFailure = null;
    notifyListeners();
  }

  Future<void> _applyPatch(UiSurfacePatch patch, int sequence) async {
    final current = _surfaces[patch.surfaceId];
    if (current != null && patch.toRevision <= current.revision) {
      _commitCursor(sequence);
      return;
    }

    if (current != null && patch.fromRevision == current.revision) {
      final updated = UiSurfacePatcher.apply(current, patch);
      if (updated != null) {
        _surfaces[patch.surfaceId] = updated;
        _commitCursor(sequence);
        notifyListeners();
        return;
      }
    }

    final recovered = await _recoverSnapshot(patch.surfaceId);
    if (recovered) {
      _commitCursor(sequence);
    }
  }

  Future<bool> _recoverSnapshot(String surfaceId) async {
    if (_snapshotInFlight.contains(surfaceId)) {
      return false;
    }
    _snapshotInFlight.add(surfaceId);
    try {
      final snapshot = await client.fetchSnapshot(surfaceId);
      _applySnapshot(snapshot.surface);
      return true;
    } catch (_) {
      _closeProtocol('connection failure');
      return false;
    } finally {
      _snapshotInFlight.remove(surfaceId);
    }
  }

  void _commitCursor(int sequence) {
    if (sequence <= _feedCursor) {
      return;
    }
    _feedCursor = sequence;
    cursorStore.write(_feedCursor);
  }

  void _onTransportError(Object error, StackTrace stackTrace) {
    if (error is GatewayException) {
      _closeProtocol(_sanitizedMessage(error.code));
      return;
    }
    _closeProtocol('connection failure');
  }

  void _onDone() {}

  void _closeProtocol(String message) {
    _closedFailure = message;
    notifyListeners();
  }

  static String _sanitizedMessage(String code) {
    switch (code) {
      case 'schema.unsupported':
        return 'unsupported schema version';
      case 'sequence.invalid':
        return 'invalid feed sequence';
      case 'action.revision-conflict':
      case 'action.revision-stale':
        return 'action revision conflict';
      default:
        return 'connection failure';
    }
  }

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
    _subscription = null;
    super.dispose();
  }
}
