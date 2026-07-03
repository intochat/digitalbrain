import 'dart:ui';

sealed class EffectsPulse {
  const EffectsPulse({required this.startedAt, required this.duration});
  final DateTime startedAt;
  final Duration duration;

  double progress(DateTime now) =>
      (now.difference(startedAt).inMilliseconds / duration.inMilliseconds)
          .clamp(0.0, 1.0);

  bool isExpired(DateTime now) => now.difference(startedAt) > duration;
}

final class CollapseWave extends EffectsPulse {
  CollapseWave({
    required this.origin,
    super.duration = const Duration(milliseconds: 500),
    DateTime? startedAt,
  }) : super(startedAt: startedAt ?? DateTime.now());

  final Offset origin;
}

final class BirthPulse extends EffectsPulse {
  BirthPulse({
    required this.neuronId,
    required this.origin,
    required this.target,
    super.duration = const Duration(milliseconds: 800),
    DateTime? startedAt,
  }) : super(startedAt: startedAt ?? DateTime.now());

  final String neuronId;
  final Offset origin;
  final Offset target;
}
