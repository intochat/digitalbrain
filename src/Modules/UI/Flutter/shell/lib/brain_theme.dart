import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';

// Shell consumes ui tokens. Keep Brain* names as thin aliases so existing
// screens do not churn while ui remains the single source of truth.

abstract final class BrainPalette {
  static const navigation = UiPalette.navigation;
  static const surface = UiPalette.surface;
  static const surfaceRaised = UiPalette.surfaceRaised;
  static const surfaceSunken = UiPalette.surfaceSunken;
  static const line = UiPalette.line;
  static const lineStrong = UiPalette.lineStrong;
  static const textPrimary = UiPalette.textPrimary;
  static const textMuted = UiPalette.textMuted;
  static const textFaint = UiPalette.textFaint;
  static const signal = UiPalette.signal;
  static const owner = UiPalette.owner;
  static const success = UiPalette.success;
}

abstract final class BrainType {
  static const monoFamily = UiType.monoFamily;
  static const monoFallback = UiType.monoFallback;
  static const bodyFamily = UiType.bodyFamily;
  static const bodyFallback = UiType.bodyFallback;
  static const title = UiType.title;
  static const heading = UiType.heading;
  static const cardTitle = UiType.cardTitle;
  static const metric = UiType.metric;
  static const body = UiType.body;
  static const bodyMuted = UiType.bodyMuted;
  static const meta = UiType.meta;
  static const metaStrong = UiType.metaStrong;
  static const empty = UiType.empty;
}

abstract final class BrainTheme {
  static ThemeData dark() => UiTheme.dark();
}

abstract final class BrainChatTheme {
  static ChatTheme dark() => UiChatTheme.dark();
}
