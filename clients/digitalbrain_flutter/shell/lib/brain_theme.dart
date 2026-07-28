import 'package:flutter/material.dart';

abstract final class BrainPalette {
  static const navigation = Color(0xFF101219);
  static const surface = Color(0xFF141720);
  static const surfaceRaised = Color(0xFF1B1F2A);
  static const surfaceSunken = Color(0xFF11141B);
  static const line = Color(0xFF292E3B);
  static const lineStrong = Color(0xFF363C4C);
  static const textPrimary = Color(0xFFE9EBF2);
  static const textMuted = Color(0xFF969CAF);
  static const textFaint = Color(0xFF5E6474);
  static const signal = Color(0xFFE09261);
  static const owner = Color(0xFF7B9BE3);
  static const success = Color(0xFF65C5A0);
}

abstract final class BrainTheme {
  static ThemeData dark() {
    final scheme = ColorScheme.fromSeed(
      seedColor: BrainPalette.signal,
      brightness: Brightness.dark,
      surface: BrainPalette.surface,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: BrainPalette.surface,
      colorScheme: scheme.copyWith(
        primary: BrainPalette.signal,
        secondary: BrainPalette.owner,
        surface: BrainPalette.surface,
      ),
      dividerColor: BrainPalette.line,
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: BrainPalette.navigation,
        indicatorColor: BrainPalette.signal.withValues(alpha: 0.14),
        selectedIconTheme: const IconThemeData(
          color: BrainPalette.signal,
          size: 21,
        ),
        unselectedIconTheme: const IconThemeData(
          color: BrainPalette.textMuted,
          size: 20,
        ),
        selectedLabelTextStyle: BrainType.metaStrong.copyWith(
          color: BrainPalette.signal,
        ),
        unselectedLabelTextStyle: BrainType.meta,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: BrainPalette.navigation,
        indicatorColor: BrainPalette.signal.withValues(alpha: 0.14),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? BrainType.metaStrong.copyWith(color: BrainPalette.signal)
              : BrainType.meta,
        ),
      ),
      tooltipTheme: const TooltipThemeData(
        decoration: BoxDecoration(color: BrainPalette.surfaceRaised),
        textStyle: BrainType.meta,
      ),
    );
  }
}

abstract final class BrainType {
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

  static const heading = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 28,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.8,
    color: BrainPalette.textPrimary,
  );

  static const cardTitle = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.15,
    color: BrainPalette.textPrimary,
  );

  static const metric = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 17,
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

  static const bodyMuted = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 14,
    height: 1.5,
    color: BrainPalette.textMuted,
  );

  static const meta = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w500,
    letterSpacing: 0.45,
    color: BrainPalette.textMuted,
  );

  static const metaStrong = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w600,
    letterSpacing: 0.45,
    color: BrainPalette.textPrimary,
  );

  static const empty = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 18,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.3,
    color: BrainPalette.textPrimary,
  );
}
