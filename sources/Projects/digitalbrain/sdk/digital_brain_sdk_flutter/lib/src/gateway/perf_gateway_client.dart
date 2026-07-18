import '../perf/perf_sample.dart';
import 'perf_tier_hint.dart';

// Closure-injected gateway adapter. The client builds the actual gRPC
// closures (using its own generated DigitalBrainGatewayClient stubs) and hands
// them in — keeps the SDK free of any gRPC/proto dependency and breaks the
// circular path-dep with the consuming Flutter client.
class PerfGatewayClient {
  PerfGatewayClient({required this.pushSamples, required this.watchHints});

  final Future<void> Function(Stream<PerfSample> samples) pushSamples;
  final Stream<PerfTierHint> Function(String clientId) watchHints;
}
