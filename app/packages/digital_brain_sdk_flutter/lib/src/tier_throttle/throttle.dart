import '../perf/perf_tier.dart';

int glowPainterDotCount(PerfTier tier) => switch (tier) {
  PerfTier.smooth => 36,
  PerfTier.strained => 24,
  PerfTier.red => 16,
};

double glowPainterBlurSigma(PerfTier tier) => switch (tier) {
  PerfTier.smooth => 3.2,
  PerfTier.strained => 2.4,
  PerfTier.red => 1.8,
};

bool rimGlowEnabled(PerfTier tier) => tier != PerfTier.red;

// Gates SynapseStreamFeed.publish only (called from _LiveScreenState._onTick);
// animation pumps continue at display refresh rate.
int sceneTickMinIntervalMs(PerfTier tier) => switch (tier) {
  PerfTier.smooth => 33,
  PerfTier.strained => 125,
  PerfTier.red => 250,
};
