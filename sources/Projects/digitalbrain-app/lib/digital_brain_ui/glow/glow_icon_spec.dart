import 'package:flutter/widgets.dart';

class GlowIconSpec {
  const GlowIconSpec({
    required this.seed,
    required this.size,
    required this.tone,
    required this.shapeHint,
  });

  final int seed;
  final double size;
  final Color tone;
  final String shapeHint;

  @override
  bool operator ==(Object other) =>
      other is GlowIconSpec &&
      seed == other.seed &&
      size == other.size &&
      tone.toARGB32() == other.tone.toARGB32() &&
      shapeHint == other.shapeHint;

  @override
  int get hashCode => Object.hash(seed, size, tone.toARGB32(), shapeHint);
}
