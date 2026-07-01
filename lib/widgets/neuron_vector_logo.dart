import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:simple_icons/simple_icons.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';

class NeuronVectorLogo extends StatelessWidget {
  const NeuronVectorLogo({
    required this.neuronId,
    super.key,
    this.size = 24.0,
    this.color,
  });

  final String neuronId;
  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final normalized = neuronId.toLowerCase();
    
    IconData? iconData;
    Color? iconColor;

    if (normalized.contains('openai')) {
      iconData = SimpleIcons.openai;
      iconColor = SimpleIconColors.openai;
    } else if (normalized.contains('anthropic')) {
      iconData = SimpleIcons.anthropic;
      iconColor = SimpleIconColors.anthropic;
    } else if (normalized.contains('gmail') || normalized.contains('mail')) {
      iconData = SimpleIcons.gmail;
      iconColor = SimpleIconColors.gmail;
    } else if (normalized.contains('github') || normalized.contains('issue')) {
      iconData = SimpleIcons.github;
      iconColor = Colors.white; // SimpleIconColors.github is black, which might not be visible in dark mode
    } else if (normalized.contains('stripe')) {
      iconData = SimpleIcons.stripe;
      iconColor = SimpleIconColors.stripe;
    } else if (normalized.contains('telegram')) {
      iconData = SimpleIcons.telegram;
      iconColor = SimpleIconColors.telegram;
    } else if (normalized.contains('youtube')) {
      iconData = SimpleIcons.youtube;
      iconColor = SimpleIconColors.youtube;
    } else if (normalized.contains('sqlite')) {
      iconData = SimpleIcons.sqlite;
      iconColor = SimpleIconColors.sqlite;
    } else if (normalized.contains('postgres') || normalized.contains('postgresql')) {
      iconData = SimpleIcons.postgresql;
      iconColor = SimpleIconColors.postgresql;
    } else if (normalized.contains('flutter')) {
      iconData = SimpleIcons.flutter;
      iconColor = SimpleIconColors.flutter;
    } else if (normalized.contains('google')) {
      iconData = SimpleIcons.google;
      iconColor = SimpleIconColors.google;
    } else if (normalized.contains('travel') || normalized.contains('tripadvisor')) {
      iconData = SimpleIcons.tripadvisor;
      iconColor = SimpleIconColors.tripadvisor;
    } else if (normalized.contains('ai') || normalized.contains('llm')) {
      iconData = SimpleIcons.openai; // default to OpenAI for generic AI
      iconColor = SimpleIconColors.openai;
    }

    if (iconData != null) {
      return Icon(
        iconData,
        size: size,
        color: color ?? iconColor,
      );
    }

    final category = resolveCategory(neuronId);
    return SizedBox(
      width: size,
      height: size,
      child: CustomPaint(
        painter: LogoPainter(category: category, overrideColor: color),
      ),
    );
  }

  static String resolveCategory(String id) {
    final normalized = id.toLowerCase();
    
    if (normalized.contains('creator')) return 'creator';
    if (normalized.contains('identity') || normalized.contains('auth')) return 'identity';
    if (normalized.contains('travel')) return 'travel';
    if (normalized.contains('gmail') || normalized.contains('mail')) return 'gmail';
    if (normalized.contains('ai') || normalized.contains('llm')) return 'ai';
    if (normalized.contains('sqlite') || normalized.contains('query')) return 'sqlite';
    if (normalized.contains('taskmanager') || normalized.contains('alarm')) return 'taskmanager';
    if (normalized.contains('github') || normalized.contains('issue')) return 'github';
    if (normalized.contains('csharp') || normalized.contains('c#') || normalized.contains('csharpfile')) return 'csharp';
    if (normalized.contains('digitalbrain') || normalized.contains('brand')) return 'brand';
    
    return 'default';
  }
}

class LogoPainter extends CustomPainter {
  LogoPainter({required this.category, this.overrideColor});

  final String category;
  final Color? overrideColor;

  @override
  void paint(Canvas canvas, Size size) {
    final w = size.width;
    final h = size.height;
    final cx = w / 2;
    final cy = h / 2;

    switch (category) {
      case 'brand':
      case 'default':
        _paintBrandBrain(canvas, size, cx, cy);
      case 'creator':
        _paintCreatorSpark(canvas, size, cx, cy);
      case 'identity':
        _paintIdentityShield(canvas, size, cx, cy);
      case 'travel':
        _paintTravelPlane(canvas, size, cx, cy);
      case 'gmail':
        _paintGmailEnvelope(canvas, size, cx, cy);
      case 'ai':
        _paintAiChip(canvas, size, cx, cy);
      case 'sqlite':
        _paintSqliteDb(canvas, size, cx, cy);
      case 'taskmanager':
        _paintTaskManagerClipboard(canvas, size, cx, cy);
      case 'github':
        _paintGithubOcto(canvas, size, cx, cy);
      case 'csharp':
        _paintCSharpHex(canvas, size, cx, cy);
      default:
        _paintBrandBrain(canvas, size, cx, cy);
    }
  }

  void _paintBrandBrain(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;
    
    final leftPaint = Paint()
      ..shader = LinearGradient(
        colors: [
          overrideColor ?? DigitalBrainColors.violetSoft,
          overrideColor ?? DigitalBrainColors.indigoSoft,
        ],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill
      ..isAntiAlias = true;

    final rightPaint = Paint()
      ..shader = LinearGradient(
        colors: [
          overrideColor ?? DigitalBrainColors.indigoSoft,
          overrideColor ?? DigitalBrainColors.tealSoft,
        ],
        begin: Alignment.topRight,
        end: Alignment.bottomLeft,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill
      ..isAntiAlias = true;

    final connectorPaint = Paint()
      ..color = (overrideColor ?? Colors.white).withValues(alpha: 0.6)
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.04
      ..strokeCap = StrokeCap.round;

    final dotPaint = Paint()
      ..color = overrideColor ?? Colors.white
      ..style = PaintingStyle.fill;

    final leftPath = Path()
      ..addOval(Rect.fromLTWH(w * 0.08, h * 0.22, w * 0.35, h * 0.35))
      ..addOval(Rect.fromLTWH(w * 0.16, h * 0.12, w * 0.35, h * 0.38))
      ..addOval(Rect.fromLTWH(w * 0.14, h * 0.44, w * 0.32, h * 0.32))
      ..addOval(Rect.fromLTWH(w * 0.22, h * 0.50, w * 0.28, h * 0.30));

    final rightPath = Path()
      ..addOval(Rect.fromLTWH(w * 0.57, h * 0.22, w * 0.35, h * 0.35))
      ..addOval(Rect.fromLTWH(w * 0.49, h * 0.12, w * 0.35, h * 0.38))
      ..addOval(Rect.fromLTWH(w * 0.54, h * 0.44, w * 0.32, h * 0.32))
      ..addOval(Rect.fromLTWH(w * 0.50, h * 0.50, w * 0.28, h * 0.30));

    canvas.drawPath(leftPath, leftPaint);
    canvas.drawPath(rightPath, rightPaint);

    canvas.drawLine(Offset(cx - w * 0.12, cy - h * 0.05), Offset(cx + w * 0.12, cy - h * 0.05), connectorPaint);
    canvas.drawLine(Offset(cx - w * 0.15, cy + h * 0.10), Offset(cx + w * 0.15, cy + h * 0.10), connectorPaint);
    canvas.drawLine(Offset(cx - w * 0.08, cy + h * 0.22), Offset(cx + w * 0.08, cy + h * 0.22), connectorPaint);

    canvas.drawCircle(Offset(cx - w * 0.12, cy - h * 0.05), w * 0.05, dotPaint);
    canvas.drawCircle(Offset(cx + w * 0.12, cy - h * 0.05), w * 0.05, dotPaint);
    canvas.drawCircle(Offset(cx - w * 0.15, cy + h * 0.10), w * 0.05, dotPaint);
    canvas.drawCircle(Offset(cx + w * 0.15, cy + h * 0.10), w * 0.05, dotPaint);
  }

  void _paintCreatorSpark(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;
    
    final paint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? DigitalBrainColors.violetSoft, overrideColor ?? Colors.indigoAccent],
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    final path = Path();
    path.moveTo(cx, h * 0.1);
    path.quadraticBezierTo(cx + w * 0.05, cy - h * 0.05, w * 0.9, cy);
    path.quadraticBezierTo(cx + w * 0.05, cy + h * 0.05, cx, h * 0.9);
    path.quadraticBezierTo(cx - w * 0.05, cy + h * 0.05, w * 0.1, cy);
    path.quadraticBezierTo(cx - w * 0.05, cy - h * 0.05, cx, h * 0.1);

    canvas.drawPath(path, paint);

    canvas.drawCircle(
      Offset(cx, cy),
      w * 0.15,
      Paint()..color = Colors.white,
    );
  }

  void _paintIdentityShield(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final paint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.purple, overrideColor ?? Colors.pinkAccent],
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    final path = Path()
      ..moveTo(w * 0.15, h * 0.15)
      ..lineTo(w * 0.85, h * 0.15)
      ..lineTo(w * 0.85, h * 0.50)
      ..quadraticBezierTo(w * 0.85, h * 0.80, cx, h * 0.90)
      ..quadraticBezierTo(w * 0.15, h * 0.80, w * 0.15, h * 0.50)
      ..close();

    canvas.drawPath(path, paint);

    final innerPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.8)
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.06;
    canvas.drawCircle(Offset(cx, cy - h * 0.05), w * 0.15, innerPaint);
    
    final bodyPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.8)
      ..style = PaintingStyle.fill;
    canvas.drawRect(Rect.fromLTWH(cx - w * 0.03, cy + h * 0.1, w * 0.06, h * 0.15), bodyPaint);
  }

  void _paintTravelPlane(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final paint1 = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.orange, overrideColor ?? Colors.amber],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    final paint2 = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.deepOrangeAccent, overrideColor ?? Colors.orange],
        begin: Alignment.topRight,
        end: Alignment.bottomLeft,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    final leftPath = Path()
      ..moveTo(cx, h * 0.1)
      ..lineTo(w * 0.15, h * 0.7)
      ..lineTo(cx, h * 0.55)
      ..close();

    final rightPath = Path()
      ..moveTo(cx, h * 0.1)
      ..lineTo(w * 0.85, h * 0.7)
      ..lineTo(cx, h * 0.55)
      ..close();

    canvas.drawPath(leftPath, paint1);
    canvas.drawPath(rightPath, paint2);

    final shadowPaint = Paint()..color = Colors.black.withValues(alpha: 0.15);
    final shadowPath = Path()
      ..moveTo(cx, h * 0.55)
      ..lineTo(w * 0.40, h * 0.70)
      ..lineTo(cx, h * 0.80)
      ..close();
    canvas.drawPath(shadowPath, shadowPaint);
  }

  void _paintGmailEnvelope(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final bgPaint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.red, overrideColor ?? Colors.redAccent],
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    canvas.drawRRect(RRect.fromRectAndRadius(Rect.fromLTWH(w * 0.1, h * 0.2, w * 0.8, h * 0.6), Radius.circular(w * 0.08)), bgPaint);

    final flapPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.3)
      ..style = PaintingStyle.fill;

    final flapPath = Path()
      ..moveTo(w * 0.1, h * 0.2)
      ..lineTo(cx, h * 0.55)
      ..lineTo(w * 0.9, h * 0.2)
      ..close();
    canvas.drawPath(flapPath, flapPaint);

    final foldPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.15)
      ..style = PaintingStyle.fill;
    final foldPath = Path()
      ..moveTo(w * 0.1, h * 0.8)
      ..lineTo(cx, h * 0.5)
      ..lineTo(w * 0.9, h * 0.8)
      ..close();
    canvas.drawPath(foldPath, foldPaint);
  }

  void _paintAiChip(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final chipPaint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.teal, overrideColor ?? Colors.greenAccent],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    canvas.drawRRect(RRect.fromRectAndRadius(Rect.fromLTWH(w * 0.2, h * 0.2, w * 0.6, h * 0.6), Radius.circular(w * 0.1)), chipPaint);

    final pinPaint = Paint()
      ..color = (overrideColor ?? Colors.white).withValues(alpha: 0.7)
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.05
      ..strokeCap = StrokeCap.round;

    canvas.drawLine(Offset(w * 0.08, h * 0.35), Offset(w * 0.20, h * 0.35), pinPaint);
    canvas.drawLine(Offset(w * 0.08, h * 0.50), Offset(w * 0.20, h * 0.50), pinPaint);
    canvas.drawLine(Offset(w * 0.08, h * 0.65), Offset(w * 0.20, h * 0.65), pinPaint);

    canvas.drawLine(Offset(w * 0.80, h * 0.35), Offset(w * 0.92, h * 0.35), pinPaint);
    canvas.drawLine(Offset(w * 0.80, h * 0.50), Offset(w * 0.92, h * 0.50), pinPaint);
    canvas.drawLine(Offset(w * 0.80, h * 0.65), Offset(w * 0.92, h * 0.65), pinPaint);

    canvas.drawCircle(Offset(cx, cy), w * 0.12, Paint()..color = Colors.white);
  }

  void _paintSqliteDb(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final paint1 = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.cyan, overrideColor ?? Colors.teal],
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    final rect1 = Rect.fromLTWH(w * 0.15, h * 0.18, w * 0.7, h * 0.25);
    final rect2 = Rect.fromLTWH(w * 0.15, h * 0.40, w * 0.7, h * 0.25);
    final rect3 = Rect.fromLTWH(w * 0.15, h * 0.62, w * 0.7, h * 0.25);

    _drawDisk(canvas, rect1, paint1, w);
    _drawDisk(canvas, rect2, paint1, w);
    _drawDisk(canvas, rect3, paint1, w);
  }

  void _drawDisk(Canvas canvas, Rect r, Paint p, double w) {
    canvas.drawOval(r, p);
    final borderPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.4)
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.03;
    canvas.drawOval(r, borderPaint);
  }

  void _paintTaskManagerClipboard(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final bgPaint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.indigo, overrideColor ?? Colors.blueAccent],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    canvas.drawRRect(RRect.fromRectAndRadius(Rect.fromLTWH(w * 0.15, h * 0.15, w * 0.7, h * 0.7), Radius.circular(w * 0.08)), bgPaint);

    final linePaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.8)
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.04
      ..strokeCap = StrokeCap.round;

    canvas.drawLine(Offset(w * 0.35, h * 0.4), Offset(w * 0.7, h * 0.4), linePaint);
    canvas.drawLine(Offset(w * 0.35, h * 0.55), Offset(w * 0.7, h * 0.55), linePaint);
    canvas.drawLine(Offset(w * 0.35, h * 0.7), Offset(w * 0.65, h * 0.7), linePaint);

    final checkPaint = Paint()
      ..color = Colors.white
      ..style = PaintingStyle.fill;
    canvas.drawCircle(Offset(w * 0.26, h * 0.4), w * 0.04, checkPaint);
    canvas.drawCircle(Offset(w * 0.26, h * 0.55), w * 0.04, checkPaint);
    canvas.drawCircle(Offset(w * 0.26, h * 0.7), w * 0.04, checkPaint);
  }

  void _paintGithubOcto(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final paint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.blueGrey.shade800, overrideColor ?? Colors.indigo.shade900],
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    canvas.drawRRect(RRect.fromRectAndRadius(Rect.fromLTWH(w * 0.1, h * 0.1, w * 0.8, h * 0.8), Radius.circular(w * 0.12)), paint);

    final gitPaint = Paint()
      ..color = Colors.white.withValues(alpha: 0.85)
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.06
      ..strokeCap = StrokeCap.round;

    final nodePaint = Paint()
      ..color = DigitalBrainColors.tealSoft
      ..style = PaintingStyle.fill;

    canvas.drawLine(Offset(w * 0.35, h * 0.25), Offset(w * 0.35, h * 0.75), gitPaint);
    final branchPath = Path()
      ..moveTo(w * 0.35, h * 0.5)
      ..quadraticBezierTo(w * 0.5, h * 0.5, w * 0.65, h * 0.35);
    canvas.drawPath(branchPath, gitPaint);

    canvas.drawCircle(Offset(w * 0.35, h * 0.3), w * 0.07, nodePaint);
    canvas.drawCircle(Offset(w * 0.35, h * 0.7), w * 0.07, nodePaint);
    canvas.drawCircle(Offset(w * 0.65, h * 0.35), w * 0.07, Paint()..color = DigitalBrainColors.violetSoft);
  }

  void _paintCSharpHex(Canvas canvas, Size size, double cx, double cy) {
    final w = size.width;
    final h = size.height;

    final paint = Paint()
      ..shader = LinearGradient(
        colors: [overrideColor ?? Colors.deepPurple, overrideColor ?? Colors.purpleAccent],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ).createShader(Offset.zero & size)
      ..style = PaintingStyle.fill;

    // Hexagon path
    final hexPath = Path();
    final angle = math.pi / 3;
    final r = w * 0.48;
    for (int i = 0; i < 6; i++) {
      final x = cx + r * math.cos(i * angle - math.pi / 6);
      final y = cy + r * math.sin(i * angle - math.pi / 6);
      if (i == 0) {
        hexPath.moveTo(x, y);
      } else {
        hexPath.lineTo(x, y);
      }
    }
    hexPath.close();
    canvas.drawPath(hexPath, paint);

    final textPaint = Paint()
      ..color = Colors.white
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.07;

    canvas.drawArc(
      Rect.fromLTWH(cx - w * 0.22, cy - h * 0.22, w * 0.36, h * 0.44),
      3 * math.pi / 4,
      3 * math.pi / 2,
      false,
      textPaint,
    );

    final sharpPaint = Paint()
      ..color = DigitalBrainColors.tealSoft
      ..style = PaintingStyle.stroke
      ..strokeWidth = w * 0.04;
    canvas.drawLine(Offset(cx + w * 0.15, cy - h * 0.15), Offset(cx + w * 0.15, cy + h * 0.15), sharpPaint);
    canvas.drawLine(Offset(cx + w * 0.25, cy - h * 0.15), Offset(cx + w * 0.25, cy + h * 0.15), sharpPaint);
    canvas.drawLine(Offset(cx + w * 0.1, cy - h * 0.05), Offset(Offset.zero.dx + cx + w * 0.3, cy - h * 0.05), sharpPaint);
    canvas.drawLine(Offset(cx + w * 0.1, cy + h * 0.05), Offset(Offset.zero.dx + cx + w * 0.3, cy + h * 0.05), sharpPaint);
  }

  @override
  bool shouldRepaint(covariant LogoPainter old) =>
      old.category != category || old.overrideColor != overrideColor;
}
