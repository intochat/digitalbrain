import 'dart:async';

import 'package:uuid/uuid.dart';

import '../gateway/perf_gateway_client.dart';
import 'perf_sample.dart';
import 'perf_tier_controller.dart';

class PerfStream {
  PerfStream._(this.clientId, this._gateway, this.tierController);

  static const _initialRetryDelay = Duration(milliseconds: 250);
  static const _maxRetryDelay = Duration(seconds: 5);

  final String clientId;
  final PerfGatewayClient _gateway;
  final PerfTierController tierController;

  final _outbox = StreamController<PerfSample>.broadcast();
  bool _disposed = false;

  void push(PerfSample sample) => _outbox.add(sample);

  Future<void> dispose() async {
    if (_disposed) return;
    _disposed = true;
    await _outbox.close();
    tierController.dispose();
  }

  static Future<PerfStream> bootstrap({
    required PerfGatewayClient gateway,
  }) async {
    final clientId = const Uuid().v4();
    final controller = PerfTierController();
    final s = PerfStream._(clientId, gateway, controller);
    unawaited(s._pumpPushWithRetry());
    unawaited(s._pumpWatchWithRetry());
    return s;
  }

  Future<void> _pumpPushWithRetry() async {
    var backoff = _initialRetryDelay;
    while (!_disposed) {
      try {
        await _gateway.pushSamples(_outbox.stream);
      } catch (_) {
        // Retry below.
      }
      backoff = await _pauseBeforeRetry(backoff);
    }
  }

  Future<void> _pumpWatchWithRetry() async {
    var backoff = _initialRetryDelay;
    while (!_disposed) {
      try {
        await for (final hint in _gateway.watchHints(clientId)) {
          if (_disposed) return;
          tierController.update(hint.tier);
        }
      } catch (_) {
        // Retry below.
      }
      backoff = await _pauseBeforeRetry(backoff);
    }
  }

  Future<Duration> _pauseBeforeRetry(Duration backoff) async {
    if (_disposed) return backoff;
    await Future.delayed(backoff);
    if (_disposed) return backoff;
    return backoff < _maxRetryDelay ? backoff * 2 : backoff;
  }
}
