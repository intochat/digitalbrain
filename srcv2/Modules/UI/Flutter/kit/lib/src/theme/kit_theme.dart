import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';

/// Product color tokens. Shell and kit both consume these — do not re-declare
/// palettes in the shell package.
abstract final class KitPalette {
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

abstract final class KitType {
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
    color: KitPalette.textPrimary,
  );

  static const heading = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 28,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.8,
    color: KitPalette.textPrimary,
  );

  static const cardTitle = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.15,
    color: KitPalette.textPrimary,
  );

  static const metric = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 17,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.2,
    color: KitPalette.textPrimary,
  );

  static const body = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    height: 1.55,
    color: KitPalette.textPrimary,
  );

  static const bodyMuted = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 14,
    height: 1.5,
    color: KitPalette.textMuted,
  );

  static const meta = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w500,
    letterSpacing: 0.45,
    color: KitPalette.textMuted,
  );

  static const metaStrong = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w600,
    letterSpacing: 0.45,
    color: KitPalette.textPrimary,
  );

  static const empty = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 18,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.3,
    color: KitPalette.textPrimary,
  );
}

abstract final class KitTheme {
  static ThemeData dark() {
    final scheme = ColorScheme.fromSeed(
      seedColor: KitPalette.signal,
      brightness: Brightness.dark,
      surface: KitPalette.surface,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: KitPalette.surface,
      colorScheme: scheme.copyWith(
        primary: KitPalette.signal,
        secondary: KitPalette.owner,
        surface: KitPalette.surface,
      ),
      dividerColor: KitPalette.line,
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: KitPalette.navigation,
        indicatorColor: KitPalette.signal.withValues(alpha: 0.14),
        selectedIconTheme: const IconThemeData(
          color: KitPalette.signal,
          size: 21,
        ),
        unselectedIconTheme: const IconThemeData(
          color: KitPalette.textMuted,
          size: 20,
        ),
        selectedLabelTextStyle: KitType.metaStrong.copyWith(
          color: KitPalette.signal,
        ),
        unselectedLabelTextStyle: KitType.meta,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: KitPalette.navigation,
        indicatorColor: KitPalette.signal.withValues(alpha: 0.14),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? KitType.metaStrong.copyWith(color: KitPalette.signal)
              : KitType.meta,
        ),
      ),
      tooltipTheme: const TooltipThemeData(
        decoration: BoxDecoration(color: KitPalette.surfaceRaised),
        textStyle: KitType.meta,
      ),
    );
  }
}

abstract final class KitChatTheme {
  static ChatTheme dark() {
    final base = ChatTypography.standard(fontFamily: KitType.bodyFamily);
    return ChatTheme(
      colors: const ChatColors(
        primary: KitPalette.owner,
        onPrimary: KitPalette.surface,
        surface: KitPalette.surface,
        onSurface: KitPalette.textPrimary,
        surfaceContainer: KitPalette.surfaceRaised,
        surfaceContainerLow: KitPalette.surfaceSunken,
        surfaceContainerHigh: KitPalette.line,
      ),
      typography: base.copyWith(
        bodyMedium: KitType.body,
        bodySmall: KitType.bodyMuted,
        labelSmall: KitType.meta,
      ),
      shape: BorderRadius.circular(14),
    );
  }
}
