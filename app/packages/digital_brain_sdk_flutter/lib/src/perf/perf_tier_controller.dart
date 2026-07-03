import 'package:flutter/foundation.dart';
import 'perf_tier.dart';

class PerfTierController extends ChangeNotifier {
  PerfTier _current = PerfTier.smooth;
  PerfTier get current => _current;

  void update(PerfTier tier) {
    if (_current == tier) return;
    _current = tier;
    notifyListeners();
  }
}
