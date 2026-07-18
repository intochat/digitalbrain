import '../perf/perf_sample.dart';
import 'perf_tier_hint.dart';

class PerfGatewayClient {
  PerfGatewayClient({required this.pushSamples, required this.watchHints});

  final Future<void> Function(Stream<PerfSample> samples) pushSamples;
  final Stream<PerfTierHint> Function(String clientId) watchHints;
}
