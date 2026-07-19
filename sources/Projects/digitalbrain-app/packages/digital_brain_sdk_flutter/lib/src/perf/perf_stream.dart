import 'dart:async';

import 'package:uuid/uuid.dart';

import '../gateway/perf_gateway_client.dart';
import 'perf_sample.dart';
import 'perf_tier_controller.dart';

class PerfStream {
  PerfStream._(this.clientId, this._gateway, this.tierController);

  final String clientId;
  final PerfGatewayClient _gateway;
  final PerfTierController tierController;

  final _outbox = StreamController<PerfSample>.broadcast();

  void push(PerfSample sample) => _outbox.add(sample);

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
    var backoff = const Duration(milliseconds: 250);
    while (true) {
      try {
        await _gateway.pushSamples(_outbox.stream);
        backoff = const Duration(milliseconds: 250);
      } catch (_) {
        await Future.delayed(backoff);
        if (backoff < const Duration(seconds: 5)) {
          backoff *= 2;
        }
      }
    }
  }

  Future<void> _pumpWatchWithRetry() async {
    var backoff = const Duration(milliseconds: 250);
    while (true) {
      try {
        await for (final hint in _gateway.watchHints(clientId)) {
          tierController.update(hint.tier);
        }
      } catch (_) {
        await Future.delayed(backoff);
        if (backoff < const Duration(seconds: 5)) {
          backoff *= 2;
        }
      }
    }
  }
}
