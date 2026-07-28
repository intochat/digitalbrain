import 'package:flutter/material.dart';

/// The conversation is a journal you can talk to, so the surface reads as an
/// instrument rather than a messenger: deep slate-indigo, two voices separated
/// by colour temperature, and a monospace rail carrying real journal sequence.
abstract final class BrainPalette {
  static const surface = Color(0xFF14161D);
  static const surfaceRaised = Color(0xFF1C1F29);
  static const line = Color(0xFF2A2E3B);
  static const textPrimary = Color(0xFFE6E8EF);
  static const textMuted = Color(0xFF8A90A3);

  /// The brain answering.
  static const signal = Color(0xFFD98A5B);

  /// The owner speaking.
  static const owner = Color(0xFF6E8FD4);
}

abstract final class BrainType {
  /// Windows ships Cascadia; the fallbacks keep the rail monospace elsewhere.
  static const monoFamily = 'Cascadia Mono';
  static const monoFallback = ['Consolas', 'Menlo', 'monospace'];

  static const bodyFamily = 'Segoe UI Variable Text';
  static const bodyFallback = ['Segoe UI', 'Inter', 'Roboto'];

  static const title = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.2,
    color: BrainPalette.textPrimary,
  );

  static const body = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    height: 1.55,
    color: BrainPalette.textPrimary,
  );

  static const meta = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w500,
    letterSpacing: 0.5,
    color: BrainPalette.textMuted,
  );

  static const empty = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 17,
    fontWeight: FontWeight.w500,
    letterSpacing: -0.2,
    color: BrainPalette.textPrimary,
  );
}
