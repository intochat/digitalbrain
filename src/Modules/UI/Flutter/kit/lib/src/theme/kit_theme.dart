import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';

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
  static const bodyFamily = 'Segoe UI Variable Text';
  static const bodyFallback = ['Segoe UI', 'Inter', 'Roboto'];

  static const title = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    color: KitPalette.textPrimary,
  );

  static const body = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 14,
    height: 1.4,
    color: KitPalette.textPrimary,
  );

  static const bodyMuted = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 13,
    height: 1.35,
    color: KitPalette.textMuted,
  );

  static const meta = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 11,
    color: KitPalette.textMuted,
  );

  static const metaStrong = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 11,
    fontWeight: FontWeight.w600,
    color: KitPalette.textPrimary,
  );

  static const heading = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 22,
    fontWeight: FontWeight.w600,
    color: KitPalette.textPrimary,
  );
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
