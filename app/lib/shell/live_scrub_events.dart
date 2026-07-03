import 'package:flutter/foundation.dart';

// Process-wide singleton ValueNotifier that carries cross-tab scrub requests
// from the Home feed (ExplainAnswerCard) to the Live tab's timeline strip.
// Sufficient for a single-window desktop/web app — no provider overhead needed.
class LiveScrubEvents {
  LiveScrubEvents._();

  static final LiveScrubEvents instance = LiveScrubEvents._();

  final ValueNotifier<String?> targetCorrelationId = ValueNotifier<String?>(null);

  void requestScrub(String correlationId) {
    targetCorrelationId.value = correlationId;
  }

  // Called by the Live tab listener immediately after handling to prevent re-processing.
  void consume() {
    targetCorrelationId.value = null;
  }
}
