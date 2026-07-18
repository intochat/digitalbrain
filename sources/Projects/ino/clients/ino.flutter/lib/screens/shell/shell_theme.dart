import 'package:flutter/material.dart';

/// Design tokens for the ino-shell screen.
///
/// All values are sourced verbatim from the Claude Design handoff at
/// `docs/ino-design/ino-shell.html`. Keep this file in lockstep with the
/// prototype's `:root` CSS variables and motion section.
class InoShellTheme {
  InoShellTheme._();

  // --- ink palette
  static const Color ink0 = Color(0xFF0A0E14);
  static const Color ink1 = Color(0xFF11161F);
  static const Color ink2 = Color(0xFF161D29);

  // --- semantic palette
  static const Color cyan = Color(0xFF3DDCFF);   // neuron
  static const Color indigo = Color(0xFF7C8AFF); // synapse
  static const Color gold = Color(0xFFE8C56A);   // recall — only warm
  static const Color pink = Color(0xFFF4B8E4);
  static const Color red = Color(0xFFFF6B6B);    // incident — sparingly

  // --- text
  static const Color text = Color(0xFFE6EDF7);
  static const Color muted = Color(0xFF7C8AAA);
  static const Color muted2 = Color(0xFF5A6680);

  // --- glass / lines
  static const Color glassFill = Color(0x0AFFFFFF);          // rgba(255,255,255,0.04)
  static const Color glassFillStrong = Color(0x10FFFFFF);    // rgba(255,255,255,0.06)
  static const Color line = Color(0x247D8AFF);               // rgba(125,138,255,0.14)
  static const Color lineStrong = Color(0x477D8AFF);         // rgba(125,138,255,0.28)

  // --- motion
  static const Cubic easeOut = Cubic(0.22, 1, 0.36, 1);
  static const Duration cometDur = Duration(milliseconds: 480);
  static const Duration cardEntryDur = Duration(milliseconds: 240);
  static const Duration brainIdleBeat = Duration(milliseconds: 4800);
  static const double cameraOrbitRadPerSec = 0.05;

  // --- latency budgets — assert in DemoRunner; later wire to SLO logging
  static const Duration utteranceToFirstCometBudget = Duration(milliseconds: 400);
  static const Duration toFirstCardBudget = Duration(milliseconds: 2500);
  static const Duration toCompletePlanBudget = Duration(seconds: 6);

  // --- glass blur sigma
  static const double glassBlurSigma = 24;
  static const double glassBlurSigmaStrong = 28;
}
