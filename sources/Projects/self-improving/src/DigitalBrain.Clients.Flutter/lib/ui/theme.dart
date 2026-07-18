import 'package:flutter/material.dart';
import 'dart:ui' show lerpDouble;

/// Design system v2 tokens as ThemeExtension for proper Flutter theming.
/// Shared values with C# LiquidGlassDesignTokens for consistency across renderers.
class LiquidGlassTokens extends ThemeExtension<LiquidGlassTokens> {
  const LiquidGlassTokens({
    required this.primaryColor,
    required this.secondaryColor,
    required this.backgroundColor,
    required this.cardColor,
    required this.buttonColor,
    required this.textColor,
    required this.blurSigma,
    required this.borderOpacity,
    required this.backgroundOpacity,
    required this.borderRadiusLarge,
    required this.borderRadiusMedium,
    required this.borderRadiusSmall,
    required this.spacingTiny,
    required this.spacingSmall,
    required this.spacingMedium,
    required this.spacingLarge,
    required this.motionShortDuration,
    required this.shadowBlurRadius,
    required this.shadowOffsetY,
  });

  final Color primaryColor;
  final Color secondaryColor;
  final Color backgroundColor;
  final Color cardColor;
  final Color buttonColor;
  final Color textColor;
  final double blurSigma;
  final double borderOpacity;
  final double backgroundOpacity;
  final double borderRadiusLarge;
  final double borderRadiusMedium;
  final double borderRadiusSmall;
  final double spacingTiny;
  final double spacingSmall;
  final double spacingMedium;
  final double spacingLarge;
  final Duration motionShortDuration;
  final double shadowBlurRadius;
  final double shadowOffsetY;

  /// Fallback for cases without theme context (e.g. tests, console parity).
  static const LiquidGlassTokens fallback = LiquidGlassTokens(
    primaryColor: Color(0xFF00E5D1),
    secondaryColor: Color(0xFF9E00FF),
    backgroundColor: Color(0xFF0A0E1A),
    cardColor: Color(0xFF121A2A),
    buttonColor: Color(0xFF1A2333),
    textColor: Color(0xFFE0F2F1),
    blurSigma: 14.0,
    borderOpacity: 0.15,
    backgroundOpacity: 0.60,
    borderRadiusLarge: 18.0,
    borderRadiusMedium: 12.0,
    borderRadiusSmall: 8.0,
    spacingTiny: 4.0,
    spacingSmall: 8.0,
    spacingMedium: 16.0,
    spacingLarge: 24.0,
    motionShortDuration: Duration(milliseconds: 200),
    shadowBlurRadius: 16.0,
    shadowOffsetY: 5.0,
  );

  @override
  LiquidGlassTokens copyWith({
    Color? primaryColor,
    Color? secondaryColor,
    Color? backgroundColor,
    Color? cardColor,
    Color? buttonColor,
    Color? textColor,
    double? blurSigma,
    double? borderOpacity,
    double? backgroundOpacity,
    double? borderRadiusLarge,
    double? borderRadiusMedium,
    double? borderRadiusSmall,
    double? spacingTiny,
    double? spacingSmall,
    double? spacingMedium,
    double? spacingLarge,
    Duration? motionShortDuration,
    double? shadowBlurRadius,
    double? shadowOffsetY,
  }) {
    return LiquidGlassTokens(
      primaryColor: primaryColor ?? this.primaryColor,
      secondaryColor: secondaryColor ?? this.secondaryColor,
      backgroundColor: backgroundColor ?? this.backgroundColor,
      cardColor: cardColor ?? this.cardColor,
      buttonColor: buttonColor ?? this.buttonColor,
      textColor: textColor ?? this.textColor,
      blurSigma: blurSigma ?? this.blurSigma,
      borderOpacity: borderOpacity ?? this.borderOpacity,
      backgroundOpacity: backgroundOpacity ?? this.backgroundOpacity,
      borderRadiusLarge: borderRadiusLarge ?? this.borderRadiusLarge,
      borderRadiusMedium: borderRadiusMedium ?? this.borderRadiusMedium,
      borderRadiusSmall: borderRadiusSmall ?? this.borderRadiusSmall,
      spacingTiny: spacingTiny ?? this.spacingTiny,
      spacingSmall: spacingSmall ?? this.spacingSmall,
      spacingMedium: spacingMedium ?? this.spacingMedium,
      spacingLarge: spacingLarge ?? this.spacingLarge,
      motionShortDuration: motionShortDuration ?? this.motionShortDuration,
      shadowBlurRadius: shadowBlurRadius ?? this.shadowBlurRadius,
      shadowOffsetY: shadowOffsetY ?? this.shadowOffsetY,
    );
  }

  @override
  LiquidGlassTokens lerp(covariant ThemeExtension<LiquidGlassTokens>? other, double t) {
    if (other is! LiquidGlassTokens) return this;
    return LiquidGlassTokens(
      primaryColor: Color.lerp(primaryColor, other.primaryColor, t)!,
      secondaryColor: Color.lerp(secondaryColor, other.secondaryColor, t)!,
      backgroundColor: Color.lerp(backgroundColor, other.backgroundColor, t)!,
      cardColor: Color.lerp(cardColor, other.cardColor, t)!,
      buttonColor: Color.lerp(buttonColor, other.buttonColor, t)!,
      textColor: Color.lerp(textColor, other.textColor, t)!,
      blurSigma: lerpDouble(blurSigma, other.blurSigma, t)!,
      borderOpacity: lerpDouble(borderOpacity, other.borderOpacity, t)!,
      backgroundOpacity: lerpDouble(backgroundOpacity, other.backgroundOpacity, t)!,
      borderRadiusLarge: lerpDouble(borderRadiusLarge, other.borderRadiusLarge, t)!,
      borderRadiusMedium: lerpDouble(borderRadiusMedium, other.borderRadiusMedium, t)!,
      borderRadiusSmall: lerpDouble(borderRadiusSmall, other.borderRadiusSmall, t)!,
      spacingTiny: lerpDouble(spacingTiny, other.spacingTiny, t)!,
      spacingSmall: lerpDouble(spacingSmall, other.spacingSmall, t)!,
      spacingMedium: lerpDouble(spacingMedium, other.spacingMedium, t)!,
      spacingLarge: lerpDouble(spacingLarge, other.spacingLarge, t)!,
      motionShortDuration: motionShortDuration, // duration not lerped typically
      shadowBlurRadius: lerpDouble(shadowBlurRadius, other.shadowBlurRadius, t)!,
      shadowOffsetY: lerpDouble(shadowOffsetY, other.shadowOffsetY, t)!,
    );
  }
}
