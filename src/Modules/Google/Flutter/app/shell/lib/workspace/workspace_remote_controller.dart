import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

/// Revisions invalidate a projection; only an authoritative read supplies state.
final class WorkspaceRemoteController {
  WorkspaceRemoteController({
    required this.read,
    required this.watch,
    required this.apply,
    this.onError,
  });
  final Future<WorkspaceSnapshot> Function(String) read;
  final Stream<WorkspaceRevision> Function(String) watch;
  final void Function(WorkspaceSnapshot) apply;
  final void Function(Object)? onError;
  StreamSubscription<WorkspaceRevision>? _subscription;
  Timer? _reconnect;
  String? _workspace;
  int _revision = -1, _requested = -1;
  bool _disposed = false, _reading = false;
  void start(String workspace) {
    if (_workspace != null) throw StateError('Controller already started');
    _workspace = workspace;
    _connect();
  }

  void _connect() {
    if (_disposed) return;
    _subscription = watch(_workspace!).listen(
      (event) {
        if (event.revision > _revision) {
          _requested = event.revision;
          unawaited(refresh());
        }
      },
      onError: (Object error) {
        onError?.call(error);
        _retry();
      },
      onDone: _retry,
    );
    unawaited(refresh());
  }

  void _retry() {
    if (_disposed || _reconnect != null) return;
    _subscription?.cancel();
    _reconnect = Timer(const Duration(seconds: 1), () {
      _reconnect = null;
      _connect();
    });
  }

  Future<void> refresh() async {
    if (_disposed || _reading) return;
    _reading = true;
    try {
      do {
        final snapshot = await read(_workspace!);
        if (_disposed) return;
        if (snapshot.revision >= _revision) {
          _revision = snapshot.revision;
          apply(snapshot);
        }
      } while (!_disposed && _requested > _revision);
    } catch (error) {
      if (!_disposed) {
        onError?.call(error);
        _retry();
      }
    } finally {
      _reading = false;
    }
  }

  void dispose() {
    _disposed = true;
    _reconnect?.cancel();
    _subscription?.cancel();
  }
}
