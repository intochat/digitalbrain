import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';

// Shell consumes kit tokens. Keep Brain* names as thin aliases so existing
// screens do not churn while kit remains the single source of truth.

abstract final class BrainPalette {
  static const navigation = KitPalette.navigation;
  static const surface = KitPalette.surface;
  static const surfaceRaised = KitPalette.surfaceRaised;
  static const surfaceSunken = KitPalette.surfaceSunken;
  static const line = KitPalette.line;
  static const lineStrong = KitPalette.lineStrong;
  static const textPrimary = KitPalette.textPrimary;
  static const textMuted = KitPalette.textMuted;
  static const textFaint = KitPalette.textFaint;
  static const signal = KitPalette.signal;
  static const owner = KitPalette.owner;
  static const success = KitPalette.success;
}

abstract final class BrainType {
  static const monoFamily = KitType.monoFamily;
  static const monoFallback = KitType.monoFallback;
  static const bodyFamily = KitType.bodyFamily;
  static const bodyFallback = KitType.bodyFallback;
  static const title = KitType.title;
  static const heading = KitType.heading;
  static const cardTitle = KitType.cardTitle;
  static const metric = KitType.metric;
  static const body = KitType.body;
  static const bodyMuted = KitType.bodyMuted;
  static const meta = KitType.meta;
  static const metaStrong = KitType.metaStrong;
  static const empty = KitType.empty;
}

abstract final class BrainTheme {
  static ThemeData dark() => KitTheme.dark();
}

abstract final class BrainChatTheme {
  static ChatTheme dark() => KitChatTheme.dark();
}
