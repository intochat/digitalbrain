import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart' as material;

import '../lumen/lumen_palette.dart';

/// Product color tokens. Shell and ui both consume these — do not re-declare
/// palettes in the shell package.
abstract final class UiPalette {
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

abstract final class UiType {
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
    color: UiPalette.textPrimary,
  );

  static const heading = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 28,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.8,
    color: UiPalette.textPrimary,
  );

  static const cardTitle = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.15,
    color: UiPalette.textPrimary,
  );

  static const metric = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 17,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.2,
    color: UiPalette.textPrimary,
  );

  static const body = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    height: 1.55,
    color: UiPalette.textPrimary,
  );

  static const bodyMuted = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 14,
    height: 1.5,
    color: UiPalette.textMuted,
  );

  static const meta = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w500,
    letterSpacing: 0.45,
    color: UiPalette.textMuted,
  );

  static const metaStrong = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w600,
    letterSpacing: 0.45,
    color: UiPalette.textPrimary,
  );

  static const empty = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 18,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.3,
    color: UiPalette.textPrimary,
  );
}

abstract final class UiTheme {
  /// Neutral workspace surfaces with the shared Lumen accent. Legacy graph and
  /// chat tokens remain independent while workspace pages migrate.
  static ThemeData workspace(Brightness brightness) {
    final darkMode = brightness == Brightness.dark;
    final base = darkMode ? dark() : light();
    final scheme =
        ColorScheme.fromSeed(
          seedColor: LumenPalette.accent,
          brightness: brightness,
        ).copyWith(
          primary: darkMode ? const Color(0xFF91D3B8) : LumenPalette.accent,
          onPrimary: darkMode ? const Color(0xFF103B2D) : Colors.white,
          primaryContainer: darkMode
              ? const Color(0xFF243B34)
              : LumenPalette.accentSoft,
          onPrimaryContainer: darkMode
              ? const Color(0xFFA5DCC5)
              : LumenPalette.accent,
          surface: darkMode ? const Color(0xFF191A1B) : LumenPalette.surface,
          surfaceContainerLow: darkMode
              ? const Color(0xFF202122)
              : LumenPalette.surface,
          surfaceContainer: darkMode
              ? const Color(0xFF252627)
              : LumenPalette.surfaceMuted,
          surfaceContainerHigh: darkMode
              ? const Color(0xFF2B2C2D)
              : LumenPalette.surfaceMuted,
          onSurface: darkMode ? const Color(0xFFECEBE7) : LumenPalette.ink,
          onSurfaceVariant: darkMode
              ? const Color(0xFFA5A6A3)
              : LumenPalette.muted,
          outline: darkMode ? const Color(0xFF616360) : LumenPalette.lineStrong,
          outlineVariant: darkMode
              ? const Color(0xFF333533)
              : LumenPalette.line,
        );
    final border = OutlineInputBorder(
      borderRadius: BorderRadius.circular(10),
      borderSide: BorderSide(color: scheme.outline),
    );
    return base.copyWith(
      colorScheme: scheme,
      scaffoldBackgroundColor: darkMode
          ? const Color(0xFF171819)
          : LumenPalette.background,
      textTheme:
          ThemeData(
            brightness: brightness,
            fontFamily: UiType.bodyFamily,
            fontFamilyFallback: UiType.bodyFallback,
          ).textTheme.apply(
            bodyColor: scheme.onSurface,
            displayColor: scheme.onSurface,
          ),
      appBarTheme: AppBarTheme(
        backgroundColor: darkMode
            ? const Color(0xFF171819)
            : LumenPalette.background,
        foregroundColor: scheme.onSurface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        shape: Border(bottom: BorderSide(color: scheme.outlineVariant)),
      ),
      dividerColor: scheme.outlineVariant,
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(48, 44),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(10),
          ),
          textStyle: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: scheme.surfaceContainerLow,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 16,
        ),
        border: border,
        enabledBorder: border,
        focusedBorder: border.copyWith(
          borderSide: BorderSide(color: scheme.primary, width: 2),
        ),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: scheme.surfaceContainerLow,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(18),
          side: BorderSide(color: scheme.outlineVariant),
        ),
      ),
    );
  }

  // Forui + Lumen were selected for the product redesign. Keep the third-party
  // theme behind this bridge while legacy dark components migrate explicitly.
  static final _lumenForui = FThemeData(
    debugLabel: 'DigitalBrain Lumen',
    touch: true,
    colors: FColors.neutralLight.copyWith(
      background: LumenPalette.background,
      foreground: LumenPalette.ink,
      primary: LumenPalette.accent,
      primaryForeground: LumenPalette.surface,
      secondary: LumenPalette.accentSoft,
      secondaryForeground: LumenPalette.accent,
      muted: LumenPalette.surfaceMuted,
      mutedForeground: LumenPalette.muted,
      card: LumenPalette.surface,
      border: LumenPalette.line,
      destructive: LumenPalette.error,
      error: LumenPalette.error,
    ),
  );

  static ThemeData light() => ThemeData(
    useMaterial3: true,
    brightness: Brightness.light,
    fontFamily: UiType.bodyFamily,
    fontFamilyFallback: UiType.bodyFallback,
    colorScheme: const ColorScheme.light(
      primary: LumenPalette.accent,
      onPrimary: LumenPalette.surface,
      secondary: LumenPalette.accentSoft,
      onSecondary: LumenPalette.ink,
      surface: LumenPalette.surface,
      onSurface: LumenPalette.ink,
      error: LumenPalette.error,
      outline: LumenPalette.lineStrong,
      outlineVariant: LumenPalette.line,
    ),
    scaffoldBackgroundColor: LumenPalette.background,
    dividerColor: LumenPalette.line,
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: LumenPalette.surface,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(12),
        borderSide: const BorderSide(color: LumenPalette.line),
      ),
    ),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: LumenPalette.ink,
        borderRadius: BorderRadius.circular(8),
      ),
      textStyle: const TextStyle(color: LumenPalette.surface, fontSize: 12),
    ),
  );

  static ThemeData dark() {
    final scheme = ColorScheme.fromSeed(
      seedColor: UiPalette.signal,
      brightness: Brightness.dark,
      surface: UiPalette.surface,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: UiPalette.surface,
      colorScheme: scheme.copyWith(
        primary: UiPalette.signal,
        secondary: UiPalette.owner,
        surface: UiPalette.surface,
      ),
      dividerColor: UiPalette.line,
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: UiPalette.navigation,
        indicatorColor: UiPalette.signal.withValues(alpha: 0.14),
        selectedIconTheme: const IconThemeData(
          color: UiPalette.signal,
          size: 21,
        ),
        unselectedIconTheme: const IconThemeData(
          color: UiPalette.textMuted,
          size: 20,
        ),
        selectedLabelTextStyle: UiType.metaStrong.copyWith(
          color: UiPalette.signal,
        ),
        unselectedLabelTextStyle: UiType.meta,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: UiPalette.navigation,
        indicatorColor: UiPalette.signal.withValues(alpha: 0.14),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? UiType.metaStrong.copyWith(color: UiPalette.signal)
              : UiType.meta,
        ),
      ),
      tooltipTheme: const TooltipThemeData(
        decoration: BoxDecoration(color: UiPalette.surfaceRaised),
        textStyle: UiType.meta,
      ),
    );
  }
}

/// Installs the approved product control foundation below MaterialApp.
///
/// Localization stays in this bridge so surfaces never need to import Forui.
final class UiThemeScope extends StatelessWidget {
  const UiThemeScope({
    super.key,
    required this.child,
    this.brightness = Brightness.light,
  });

  final Widget child;
  final Brightness brightness;

  @override
  Widget build(BuildContext context) {
    final data = brightness == Brightness.light
        ? UiTheme._lumenForui
        : FTheme.neutral.dark.touch;
    return Localizations.override(
      context: context,
      delegates: FLocalizations.localizationsDelegates,
      child: material.Theme(
        data: data.toApproximateMaterialTheme(),
        child: material.Material(
          type: material.MaterialType.transparency,
          child: FTheme(data: data, child: child),
        ),
      ),
    );
  }
}

abstract final class UiChatTheme {
  static ChatTheme light() => ChatTheme(
    colors: const ChatColors(
      primary: LumenPalette.accent,
      onPrimary: LumenPalette.surface,
      surface: LumenPalette.background,
      onSurface: LumenPalette.ink,
      surfaceContainer: LumenPalette.surface,
      surfaceContainerLow: LumenPalette.surfaceMuted,
      surfaceContainerHigh: LumenPalette.accentSoft,
    ),
    typography: ChatTypography.standard(fontFamily: UiType.bodyFamily).copyWith(
      bodyMedium: UiType.body.copyWith(color: LumenPalette.ink),
      bodySmall: UiType.bodyMuted.copyWith(color: LumenPalette.muted),
      labelSmall: UiType.meta.copyWith(color: LumenPalette.muted),
    ),
    shape: BorderRadius.circular(16),
  );

  static ChatTheme dark() {
    final base = ChatTypography.standard(fontFamily: UiType.bodyFamily);
    return ChatTheme(
      colors: const ChatColors(
        primary: UiPalette.owner,
        onPrimary: UiPalette.surface,
        surface: UiPalette.surface,
        onSurface: UiPalette.textPrimary,
        surfaceContainer: UiPalette.surfaceRaised,
        surfaceContainerLow: UiPalette.surfaceSunken,
        surfaceContainerHigh: UiPalette.line,
      ),
      typography: base.copyWith(
        bodyMedium: UiType.body,
        bodySmall: UiType.bodyMuted,
        labelSmall: UiType.meta,
      ),
      shape: BorderRadius.circular(14),
    );
  }
}
