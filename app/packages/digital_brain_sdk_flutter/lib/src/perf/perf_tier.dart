enum PerfTier { smooth, strained, red }

PerfTier perfTierFromString(String s) => switch (s) {
  'red' => PerfTier.red,
  'strained' => PerfTier.strained,
  _ => PerfTier.smooth,
};
