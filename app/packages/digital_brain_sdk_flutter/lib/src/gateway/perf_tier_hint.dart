import '../perf/perf_tier.dart';

class PerfTierHint {
  PerfTierHint({required this.tier, required this.reason});
  final PerfTier tier;
  final String reason;
}
