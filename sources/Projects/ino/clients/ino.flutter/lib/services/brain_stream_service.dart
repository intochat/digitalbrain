import 'dart:async';
import 'dart:developer' as developer;

import 'package:grpc/grpc.dart';

import '../grpc/generated/ino.pbgrpc.dart';
import '../screens/brain/brain_topology.dart';
import '../state/brain_inspector_bloc.dart';

class BrainStreamService {
  BrainStreamService(this._stub, this._bloc);

  final InoClient _stub;
  final BrainInspectorBloc _bloc;
  ResponseStream<BrainPulseProto>? _subscription;
  StreamSubscription<BrainPulseProto>? _listener;

  void start({String? userIdFilter, String? sessionIdFilter}) {
    if (_subscription != null) return;
    final request = BrainWatchRequest()
      ..userIdFilter = userIdFilter ?? ''
      ..sessionIdFilter = sessionIdFilter ?? '';

    _subscription = _stub.watchBrainActivity(request);
    _listener = _subscription!.listen(
      _onPulse,
      onError: (Object err, StackTrace st) => developer.log(
        'brain.pulse.error',
        name: 'ino-flutter',
        error: err,
        stackTrace: st,
      ),
      onDone: () => developer.log('brain.pulse.done', name: 'ino-flutter'),
      cancelOnError: false,
    );
  }

  Future<void> stop() async {
    await _listener?.cancel();
    _listener = null;
    await _subscription?.cancel();
    _subscription = null;
  }

  void _onPulse(BrainPulseProto pulse) {
    final toId = topologyIdForGrain(pulse.toGrain);
    if (toId == null) return; // unmapped grain — drop pulse
    // FromGrain is currently always empty server-side (Orleans RuntimeContext is
    // internal); fold those onto the receiver so the buffer has a stable key.
    // When the server learns to populate FromGrain, replace this fallback with
    // topologyIdForGrain(pulse.fromGrain) ?? toId.
    final fromId = pulse.fromGrain.isEmpty
        ? toId
        : (topologyIdForGrain(pulse.fromGrain) ?? toId);
    _bloc.add(IngestFire(FireEvent(
      id: '${pulse.traceParent}#${pulse.timestampUnixMs}',
      traceParent: pulse.traceParent,
      synapseType: pulse.methodName,
      fromId: fromId,
      toId: toId,
      payloadJson: pulse.payloadJson,
      timestampUnixMs: pulse.timestampUnixMs.toInt(),
    )));
  }
}
