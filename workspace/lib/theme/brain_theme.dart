import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

class BrainColors {
  BrainColors._();

  static const Color ground = Color(0xFF070708);
  static const Color surface = Color(0xFF0A0A0C);
  static const Color surfaceRaised = Color(0xFF12141A);

  static const Color hairline = Color(0x14FFFFFF);
  static const Color hairlineStrong = Color(0x28FFFFFF);

  static const Color ink = Color(0xFFE8EAF0);
  static const Color inkMuted = Color(0xFF8A90A0);
  static const Color inkFaint = Color(0xFF565C6B);

  static const Color indigo = Color(0xFF6C7BFF);

  static const Color amber = Color(0xFFE8B34B);
  static const Color green = Color(0xFF4BC98A);
  static const Color orange = Color(0xFFE8734B);
}

class BrainTheme {
  BrainTheme._();

  static TextStyle mono(TextStyle? base) {
    return GoogleFonts.jetBrainsMono(textStyle: base);
  }

  static ThemeData get dark {
    final colorScheme = ColorScheme.fromSeed(
      seedColor: BrainColors.indigo,
      brightness: Brightness.dark,
      primary: BrainColors.indigo,
      surface: BrainColors.surface,
      onSurface: BrainColors.ink,
      error: BrainColors.orange,
    );
    final baseTextTheme = GoogleFonts.interTextTheme(
      ThemeData(brightness: Brightness.dark).textTheme,
    ).apply(bodyColor: BrainColors.ink, displayColor: BrainColors.ink);

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: colorScheme,
      scaffoldBackgroundColor: BrainColors.ground,
      textTheme: baseTextTheme,
      fontFamily: GoogleFonts.inter().fontFamily,
      dividerColor: BrainColors.hairline,
      dividerTheme: const DividerThemeData(
        color: BrainColors.hairline,
        thickness: 1,
        space: 1,
      ),
      cardTheme: CardThemeData(
        color: BrainColors.surfaceRaised,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
          side: const BorderSide(color: BrainColors.hairline),
        ),
      ),
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: BrainColors.surface,
        indicatorColor: BrainColors.indigo.withValues(alpha: 0.16),
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: BrainColors.surface,
        surfaceTintColor: Colors.transparent,
        foregroundColor: BrainColors.ink,
      ),
    );
  }
}
