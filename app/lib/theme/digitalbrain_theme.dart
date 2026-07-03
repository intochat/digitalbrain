import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:google_fonts/google_fonts.dart';

class DigitalBrainColors {
  const DigitalBrainColors._();

  static const pitchBlack = Color(0xFF000000);
  static const obsidian = Color(0xFF070708);
  static const obsidianSlate = Color(0xFF0A0A0C);
  static const panelGlass = Color(0x06FFFFFF);

  static Color glassBg([double alpha = 0.04]) => const Color(0xFFFFFFFF).withValues(alpha: alpha);

  static Color hslColor(double hue, double saturation, double lightness, [double alpha = 1.0]) {
    return HSLColor.fromAHSL(alpha, hue, saturation, lightness).toColor();
  }

  static Color get spaceGlassOverlay => const Color(0x0CFFFFFF);
  static Color get spaceGlassLight => const Color(0x08FFFFFF);

  static final glassBorderGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [
      const Color(0xFFFFFFFF).withValues(alpha: 0.12),
      const Color(0xFFFFFFFF).withValues(alpha: 0.08),
      const Color(0x00FFFFFF),
      const Color(0xFFFFFFFF).withValues(alpha: 0.04),
      const Color(0xFFFFFFFF).withValues(alpha: 0.08),
    ],
    stops: const [0.0, 0.25, 0.5, 0.75, 1.0],
  );

  static const ink = Color(0xFFF5F5F7); // Primary platinum white
  static const inkMid = Color(0xFFE5E5E5); // Silver chrome body text
  static const inkLow = Color(0xFF86868B); // Slate grey captions
  static const inkFade = Color(0x24FFFFFF); // Faded
  static const hairline = Color(0x0CFFFFFF); // 5% white keyline catch
  static const hairlineStrong = Color(0x16FFFFFF); // 9% white keyline catch

  static const indigo = Color(0xFFE5E5E5); // Silver chrome
  static const indigoSoft = Color(0xFFF5F5F7); // Platinum white
  static const indigoDeep = Color(0xFF27272A); // Dark charcoal obsidian
  static const lavender = Color(0xFFD4D4D8); // Light silver

  static const gold = Color(0xFFF5F5F7); // Spatial silver
  static const goldSoft = Color(0xFFE5E5E5);

  static const teal = Color(0xFF27272A); // Charcoal obsidian
  static const tealSoft = Color(0xFFE5E5E5); // Silver

  static const violet = Color(0xFF86868B); // Slate grey
  static const violetSoft = Color(0xFFE5E5E5); // Silver chrome

  static const rose = Color(0xFFFF9500); // Precise Apple Hardware Amber indicator
}

class DigitalBrainTypography {
  const DigitalBrainTypography._();

  static TextStyle display({
    double size = 56,
    FontStyle? style,
    FontWeight weight = FontWeight.w300,
    Color color = DigitalBrainColors.ink,
    double letterSpacing = -0.04,
    double height = 1.05,
  }) {
    return GoogleFonts.inter(
      fontSize: size,
      fontStyle: style ?? FontStyle.normal,
      fontWeight: weight,
      color: color,
      letterSpacing: size * letterSpacing,
      height: height,
    );
  }

  static TextStyle body({
    double size = 15,
    FontWeight weight = FontWeight.w400,
    Color color = DigitalBrainColors.inkMid,
    double height = 1.6,
    double letterSpacing = 0,
  }) {
    return GoogleFonts.inter(
      fontSize: size,
      fontWeight: weight,
      color: color,
      height: height,
      letterSpacing: letterSpacing,
    );
  }

  static TextStyle mono({
    double size = 12,
    FontWeight weight = FontWeight.w500,
    Color color = DigitalBrainColors.inkLow,
    double letterSpacing = 1.6,
    double height = 1.4,
  }) {
    return GoogleFonts.jetBrainsMono(
      fontSize: size,
      fontWeight: weight,
      color: color,
      letterSpacing: letterSpacing,
      height: height,
    );
  }
}

ThemeData buildDigitalBrainTheme() {
  final scheme = const ColorScheme.dark(
    primary: DigitalBrainColors.indigoSoft,
    onPrimary: Color(0xFF09090B),
    secondary: DigitalBrainColors.tealSoft,
    onSecondary: Color(0xFF09090B),
    tertiary: DigitalBrainColors.gold,
    onTertiary: Color(0xFF09090B),
    surface: DigitalBrainColors.pitchBlack,
    onSurface: DigitalBrainColors.ink,
    surfaceContainerHighest: DigitalBrainColors.obsidianSlate,
    surfaceContainerHigh: DigitalBrainColors.obsidian,
    onSurfaceVariant: DigitalBrainColors.inkMid,
    outline: DigitalBrainColors.hairlineStrong,
    outlineVariant: DigitalBrainColors.hairline,
    error: DigitalBrainColors.rose,
    onError: Color(0xFF09090B),
  );

  final base = ThemeData(
    colorScheme: scheme,
    useMaterial3: true,
    scaffoldBackgroundColor: DigitalBrainColors.pitchBlack,
    canvasColor: DigitalBrainColors.pitchBlack,
    visualDensity: VisualDensity.adaptivePlatformDensity,
    splashFactory: InkSparkle.splashFactory,
    pageTransitionsTheme: const PageTransitionsTheme(builders: {
      TargetPlatform.android: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.iOS: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.linux: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.macOS: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.windows: FadeForwardsPageTransitionsBuilder(),
    }),
  );

  final interText = GoogleFonts.interTextTheme(base.textTheme);

  return base.copyWith(
    textTheme: interText.copyWith(
      displayLarge: interText.displayLarge?.copyWith(
        color: DigitalBrainColors.ink,
        fontWeight: FontWeight.w300,
        letterSpacing: -2.4,
        height: 1.0,
      ),
      displayMedium: interText.displayMedium?.copyWith(
        color: DigitalBrainColors.ink,
        fontWeight: FontWeight.w300,
        letterSpacing: -1.6,
        height: 1.05,
      ),
      displaySmall: interText.displaySmall?.copyWith(
        color: DigitalBrainColors.ink,
        fontWeight: FontWeight.w300,
        letterSpacing: -1.2,
        height: 1.1,
      ),
      headlineLarge: interText.headlineLarge?.copyWith(
        color: DigitalBrainColors.ink,
        fontWeight: FontWeight.w400,
        letterSpacing: -0.8,
      ),
      headlineMedium: interText.headlineMedium?.copyWith(
        color: DigitalBrainColors.ink,
        fontWeight: FontWeight.w400,
        letterSpacing: -0.6,
      ),
      headlineSmall: interText.headlineSmall?.copyWith(
        color: DigitalBrainColors.ink,
        fontWeight: FontWeight.w500,
        letterSpacing: -0.4,
      ),
    ),
  );
}

class GlassBorder extends StatelessWidget {
  final Widget child;
  final BorderRadius borderRadius;
  final double strokeWidth;
  final Gradient? gradient;

  const GlassBorder({
    super.key,
    required this.child,
    this.borderRadius = const BorderRadius.all(Radius.circular(28)),
    this.strokeWidth = 1.0,
    this.gradient,
  });

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      painter: GlassBorderPainter(
        borderRadius: borderRadius,
        strokeWidth: strokeWidth,
        gradient: gradient ?? DigitalBrainColors.glassBorderGradient,
      ),
      child: child,
    );
  }
}

class GlassBorderPainter extends CustomPainter {
  final BorderRadius borderRadius;
  final double strokeWidth;
  final Gradient gradient;

  GlassBorderPainter({
    required this.borderRadius,
    required this.strokeWidth,
    required this.gradient,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final rrect = borderRadius.toRRect(rect);
    final paint = Paint()
      ..strokeWidth = strokeWidth
      ..style = PaintingStyle.stroke
      ..shader = gradient.createShader(rect);
    canvas.drawRRect(rrect, paint);
  }

  @override
  bool shouldRepaint(covariant GlassBorderPainter oldDelegate) {
    return borderRadius != oldDelegate.borderRadius ||
        strokeWidth != oldDelegate.strokeWidth ||
        gradient != oldDelegate.gradient;
  }
}

